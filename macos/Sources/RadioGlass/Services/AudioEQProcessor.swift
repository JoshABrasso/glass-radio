import AudioToolbox
import AVFoundation
import MediaToolbox

// MARK: - Public Types

struct EQBand: Identifiable, Equatable {
    let id: Int
    let frequency: Float
    var gain: Float = 0  // -12 to +12 dB

    var label: String {
        frequency >= 1000 ? "\(Int(frequency / 1000))k" : "\(Int(frequency))"
    }
}

enum EQPreset: String, CaseIterable, Identifiable {
    case flat       = "Flat"
    case bassBoost  = "Bass Boost"
    case treble     = "Treble Boost"
    case classical  = "Classical"
    case electronic = "Electronic"
    case rock       = "Rock"
    case jazz       = "Jazz"
    case vocal      = "Vocal"
    case podcast    = "Podcast"
    case loudness   = "Loudness"

    var id: String { rawValue }

    // 8-band gains: 60Hz 170Hz 310Hz 600Hz 1kHz 3kHz 6kHz 12kHz
    var gains: [Float] {
        switch self {
        case .flat:       return [0,  0,  0,  0,  0,  0,  0,  0]
        case .bassBoost:  return [6,  5,  3,  1,  0,  0,  0,  0]
        case .treble:     return [0,  0,  0,  0,  1,  3,  5,  6]
        case .classical:  return [0,  0,  0,  0, -2, -1,  0,  2]
        case .electronic: return [5,  3,  0, -2,  0,  2,  3,  4]
        case .rock:       return [4,  3, -1, -2,  1,  4,  5,  4]
        case .jazz:       return [0,  0,  0,  3,  3,  2,  1,  2]
        case .vocal:      return [-2,-2,  2,  4,  4,  3,  1, -1]
        case .podcast:    return [-2, 0,  2,  4,  4,  2,  1,  0]
        case .loudness:   return [4,  2,  0,  0,  0,  0,  2,  4]
        }
    }
}

// MARK: - Tap Context stored via Unmanaged to avoid ARC on audio realtime thread

private final class EQTapContext {
    let audioUnit: AudioUnit
    var isSetup = false
    var scratchList: UnsafeMutablePointer<AudioBufferList>?
    var scratchData: [UnsafeMutableRawPointer] = []

    init(audioUnit: AudioUnit) {
        self.audioUnit = audioUnit
    }

    deinit {
        if isSetup { AudioUnitUninitialize(audioUnit) }
        freeScratch()
        AudioComponentInstanceDispose(audioUnit)
    }

    func freeScratch() {
        for ptr in scratchData { ptr.deallocate() }
        scratchData.removeAll()
        scratchList?.deallocate()
        scratchList = nil
    }
}

// MARK: - C-compatible tap callbacks (must be global, non-capturing)

private func eqTapInit(
    _ tap: MTAudioProcessingTap,
    _ clientInfo: UnsafeMutableRawPointer?,
    _ tapStorage: UnsafeMutablePointer<UnsafeMutableRawPointer?>
) {
    tapStorage.pointee = clientInfo
}

private func eqTapFinalize(_ tap: MTAudioProcessingTap) {
    let ptr = MTAudioProcessingTapGetStorage(tap)
    Unmanaged<EQTapContext>.fromOpaque(ptr).release()
}

private func eqTapPrepare(
    _ tap: MTAudioProcessingTap,
    _ maxFrames: CMItemCount,
    _ format: UnsafePointer<AudioStreamBasicDescription>
) {
    let ptr = MTAudioProcessingTapGetStorage(tap)
    let ctx = Unmanaged<EQTapContext>.fromOpaque(ptr).takeUnretainedValue()

    var fmt = format.pointee
    let channels = Int(fmt.mChannelsPerFrame)
    let bytesPerSample = MemoryLayout<Float>.size
    let bytesPerChannel = Int(maxFrames) * bytesPerSample

    ctx.freeScratch()

    // Build per-channel scratch AudioBufferList
    let extraBuffers = max(0, channels - 1)
    let ablBytes = MemoryLayout<AudioBufferList>.size + MemoryLayout<AudioBuffer>.size * extraBuffers
    let listRaw = UnsafeMutableRawPointer.allocate(byteCount: ablBytes, alignment: MemoryLayout<AudioBufferList>.alignment)
    let listPtr = listRaw.bindMemory(to: AudioBufferList.self, capacity: 1)
    listPtr.pointee.mNumberBuffers = UInt32(channels)

    let ablPtr = UnsafeMutableAudioBufferListPointer(listPtr)
    for i in 0..<channels {
        let buf = UnsafeMutableRawPointer.allocate(byteCount: bytesPerChannel, alignment: 16)
        ctx.scratchData.append(buf)
        ablPtr[i].mNumberChannels = 1
        ablPtr[i].mDataByteSize = UInt32(bytesPerChannel)
        ablPtr[i].mData = buf
    }
    ctx.scratchList = listPtr

    // Set stream format on AudioUnit
    AudioUnitSetProperty(ctx.audioUnit, kAudioUnitProperty_StreamFormat,
                         kAudioUnitScope_Input, 0, &fmt,
                         UInt32(MemoryLayout<AudioStreamBasicDescription>.size))
    AudioUnitSetProperty(ctx.audioUnit, kAudioUnitProperty_StreamFormat,
                         kAudioUnitScope_Output, 0, &fmt,
                         UInt32(MemoryLayout<AudioStreamBasicDescription>.size))

    // Input render callback — reads from scratchList when AU pulls input
    var cb = AURenderCallbackStruct(
        inputProc: { refCon, _, _, _, _, outputData -> OSStatus in
            guard let outputData else { return noErr }
            // refCon points to the pre-allocated scratchList AudioBufferList
            let src = refCon.assumingMemoryBound(to: AudioBufferList.self)
            let srcBufs = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: src))
            let dstBufs = UnsafeMutableAudioBufferListPointer(outputData)
            let n = min(srcBufs.count, dstBufs.count)
            for i in 0..<n {
                guard let sp = srcBufs[i].mData, let dp = dstBufs[i].mData else { continue }
                let bytes = Int(min(srcBufs[i].mDataByteSize, dstBufs[i].mDataByteSize))
                memcpy(dp, sp, bytes)
                dstBufs[i].mDataByteSize = UInt32(bytes)
            }
            return noErr
        },
        inputProcRefCon: listPtr
    )
    AudioUnitSetProperty(ctx.audioUnit, kAudioUnitProperty_SetRenderCallback,
                         kAudioUnitScope_Input, 0, &cb,
                         UInt32(MemoryLayout<AURenderCallbackStruct>.size))

    AudioUnitInitialize(ctx.audioUnit)
    ctx.isSetup = true
}

private func eqTapUnprepare(_ tap: MTAudioProcessingTap) {
    let ptr = MTAudioProcessingTapGetStorage(tap)
    let ctx = Unmanaged<EQTapContext>.fromOpaque(ptr).takeUnretainedValue()
    if ctx.isSetup {
        AudioUnitUninitialize(ctx.audioUnit)
        ctx.isSetup = false
    }
    ctx.freeScratch()
}

private func eqTapProcess(
    _ tap: MTAudioProcessingTap,
    _ frameCount: CMItemCount,
    _ flags: MTAudioProcessingTapFlags,
    _ bufferList: UnsafeMutablePointer<AudioBufferList>,
    _ framesOut: UnsafeMutablePointer<CMItemCount>,
    _ flagsOut: UnsafeMutablePointer<MTAudioProcessingTapFlags>
) {
    let ptr = MTAudioProcessingTapGetStorage(tap)
    let ctx = Unmanaged<EQTapContext>.fromOpaque(ptr).takeUnretainedValue()

    var srcFlags: MTAudioProcessingTapFlags = 0
    var srcFrames: CMItemCount = 0

    guard let scratch = ctx.scratchList else {
        MTAudioProcessingTapGetSourceAudio(tap, frameCount, bufferList, &srcFlags, nil, &srcFrames)
        framesOut.pointee = srcFrames
        flagsOut.pointee = srcFlags
        return
    }

    // Fill scratch with source audio
    let getStatus = MTAudioProcessingTapGetSourceAudio(tap, frameCount, scratch, &srcFlags, nil, &srcFrames)
    guard getStatus == noErr, ctx.isSetup else {
        framesOut.pointee = srcFrames
        flagsOut.pointee = srcFlags
        return
    }

    // Render through EQ AudioUnit into the output bufferList
    var actionFlags = AudioUnitRenderActionFlags(rawValue: 0)
    var ts = AudioTimeStamp()
    ts.mSampleTime = 0
    ts.mFlags = .sampleTimeValid

    let renderStatus = AudioUnitRender(ctx.audioUnit, &actionFlags, &ts, 0, UInt32(srcFrames), bufferList)

    if renderStatus != noErr {
        // Fallback: pass scratch audio through unchanged
        let srcABL = UnsafeMutableAudioBufferListPointer(scratch)
        let outABL = UnsafeMutableAudioBufferListPointer(bufferList)
        let n = min(srcABL.count, outABL.count)
        for i in 0..<n {
            guard let sp = srcABL[i].mData, let dp = outABL[i].mData else { continue }
            memcpy(dp, sp, Int(min(srcABL[i].mDataByteSize, outABL[i].mDataByteSize)))
        }
    }

    framesOut.pointee = srcFrames
    flagsOut.pointee = srcFlags
}

// MARK: - AudioEQProcessor

@MainActor
final class AudioEQProcessor: ObservableObject {
    static let bandFrequencies: [Float] = [60, 170, 310, 600, 1_000, 3_000, 6_000, 12_000]

    @Published var bands: [EQBand]
    @Published var isEnabled: Bool = false {
        didSet { onSettingChanged?() }
    }
    @Published var selectedPreset: EQPreset? = .flat

    var onSettingChanged: (() -> Void)?

    private var audioUnit: AudioUnit?

    init() {
        self.bands = Self.bandFrequencies.enumerated().map {
            EQBand(id: $0.offset, frequency: $0.element)
        }
        setupAudioUnit()
    }

    private func setupAudioUnit() {
        var desc = AudioComponentDescription(
            componentType: kAudioUnitType_Effect,
            componentSubType: kAudioUnitSubType_NBandEQ,
            componentManufacturer: kAudioUnitManufacturer_Apple,
            componentFlags: 0,
            componentFlagsMask: 0
        )
        guard let component = AudioComponentFindNext(nil, &desc) else { return }
        var unit: AudioUnit?
        guard AudioComponentInstanceNew(component, &unit) == noErr, let unit else { return }

        var bandCount: UInt32 = 8
        AudioUnitSetProperty(unit, kAUNBandEQProperty_NumberOfBands,
                             kAudioUnitScope_Global, 0,
                             &bandCount, UInt32(MemoryLayout<UInt32>.size))

        for i in 0..<8 {
            let idx = UInt32(i)
            var filterType: UInt32 = UInt32(kAUNBandEQFilterType_Parametric)
            var bandwidth: Float = 0.5
            var frequency: Float = Self.bandFrequencies[i]
            var gain: Float = 0

            AudioUnitSetParameter(unit, kAUNBandEQParam_FilterType + idx, kAudioUnitScope_Global, 0, Float(filterType), 0)
            AudioUnitSetParameter(unit, kAUNBandEQParam_Frequency + idx, kAudioUnitScope_Global, 0, frequency, 0)
            AudioUnitSetParameter(unit, kAUNBandEQParam_Bandwidth + idx, kAudioUnitScope_Global, 0, bandwidth, 0)
            AudioUnitSetParameter(unit, kAUNBandEQParam_Gain + idx, kAudioUnitScope_Global, 0, gain, 0)
            AudioUnitSetParameter(unit, kAUNBandEQParam_BypassBand + idx, kAudioUnitScope_Global, 0, 0, 0)
        }

        self.audioUnit = unit
    }

    func makeTap() -> MTAudioProcessingTap? {
        guard isEnabled, let unit = audioUnit else { return nil }

        let ctx = EQTapContext(audioUnit: unit)
        let retained = Unmanaged.passRetained(ctx)

        var callbacks = MTAudioProcessingTapCallbacks(
            version: kMTAudioProcessingTapCallbacksVersion_0,
            clientInfo: retained.toOpaque(),
            init: eqTapInit,
            finalize: eqTapFinalize,
            prepare: eqTapPrepare,
            unprepare: eqTapUnprepare,
            process: eqTapProcess
        )

        var tapOut: Unmanaged<MTAudioProcessingTap>? = nil
        let status = MTAudioProcessingTapCreate(
            kCFAllocatorDefault, &callbacks,
            kMTAudioProcessingTapCreationFlag_PreEffects,
            &tapOut
        )
        guard status == noErr, let tap = tapOut?.takeRetainedValue() else {
            retained.release()
            return nil
        }
        return tap
    }

    func setGain(_ gain: Float, band: Int) {
        guard bands.indices.contains(band) else { return }
        bands[band].gain = max(-12, min(12, gain))
        selectedPreset = nil
        applyBandToUnit(band)
        onSettingChanged?()
    }

    func applyPreset(_ preset: EQPreset) {
        selectedPreset = preset
        for (i, gain) in preset.gains.enumerated() {
            bands[i].gain = gain
            applyBandToUnit(i)
        }
        onSettingChanged?()
    }

    func resetToFlat() { applyPreset(.flat) }

    private func applyBandToUnit(_ band: Int) {
        guard let unit = audioUnit, bands.indices.contains(band) else { return }
        AudioUnitSetParameter(unit, kAUNBandEQParam_Gain + UInt32(band),
                              kAudioUnitScope_Global, 0, bands[band].gain, 0)
    }
}
