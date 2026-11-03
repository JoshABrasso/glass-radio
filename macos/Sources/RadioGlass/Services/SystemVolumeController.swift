import CoreAudio
import AudioToolbox
import Foundation

struct SystemVolumeApplyResult {
    let applied: Bool
    let deviceName: String
    let strategy: String
    let reason: String?
}

final class SystemVolumeController {
    func setOutputVolume(_ volume: Float) -> SystemVolumeApplyResult {
        let clamped = max(0, min(1, volume))
        guard let deviceID = defaultOutputDeviceID() else {
            return .init(applied: false, deviceName: "Unknown", strategy: "none", reason: "No default output device")
        }

        let name = "Device \(deviceID)"
        let strategies = volumeAddresses()

        for candidate in strategies {
            if setVolume(clamped, deviceID: deviceID, address: candidate.address) {
                return .init(applied: true, deviceName: name, strategy: candidate.label, reason: nil)
            }
        }

        return .init(
            applied: false,
            deviceName: name,
            strategy: "none",
            reason: "Output device does not expose a writable software volume"
        )
    }

    private func defaultOutputDeviceID() -> AudioDeviceID? {
        var deviceID = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )

        let status = AudioObjectGetPropertyData(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            0,
            nil,
            &size,
            &deviceID
        )

        guard status == noErr else { return nil }
        return deviceID
    }

    private func volumeAddresses() -> [(label: String, address: AudioObjectPropertyAddress)] {
        [
            (
                "virtual-master-output-main",
                AudioObjectPropertyAddress(
                    mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
                    mScope: kAudioDevicePropertyScopeOutput,
                    mElement: kAudioObjectPropertyElementMain
                )
            ),
            (
                "virtual-master-global-main",
                AudioObjectPropertyAddress(
                    mSelector: kAudioHardwareServiceDeviceProperty_VirtualMainVolume,
                    mScope: kAudioObjectPropertyScopeGlobal,
                    mElement: kAudioObjectPropertyElementMain
                )
            ),
            (
                "scalar-output-main",
                AudioObjectPropertyAddress(
                    mSelector: kAudioDevicePropertyVolumeScalar,
                    mScope: kAudioDevicePropertyScopeOutput,
                    mElement: kAudioObjectPropertyElementMain
                )
            ),
            (
                "scalar-output-ch1",
                AudioObjectPropertyAddress(
                    mSelector: kAudioDevicePropertyVolumeScalar,
                    mScope: kAudioDevicePropertyScopeOutput,
                    mElement: 1
                )
            ),
            (
                "scalar-output-ch2",
                AudioObjectPropertyAddress(
                    mSelector: kAudioDevicePropertyVolumeScalar,
                    mScope: kAudioDevicePropertyScopeOutput,
                    mElement: 2
                )
            )
        ]
    }

    private func setVolume(_ volume: Float, deviceID: AudioDeviceID, address: AudioObjectPropertyAddress) -> Bool {
        var mutableAddress = address
        guard AudioObjectHasProperty(deviceID, &mutableAddress) else { return false }

        var writable = DarwinBoolean(false)
        let canSetStatus = AudioObjectIsPropertySettable(deviceID, &mutableAddress, &writable)
        guard canSetStatus == noErr, writable.boolValue else { return false }

        var mutableVolume = volume
        let size = UInt32(MemoryLayout<Float32>.size)
        let setStatus = AudioObjectSetPropertyData(
            deviceID,
            &mutableAddress,
            0,
            nil,
            size,
            &mutableVolume
        )
        return setStatus == noErr
    }
}
