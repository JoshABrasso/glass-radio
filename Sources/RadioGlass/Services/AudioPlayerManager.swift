import AVFoundation
import Foundation
import MediaPlayer
import Network

@MainActor
final class AudioPlayerManager: ObservableObject {
    @Published private(set) var currentStation: RadioStation?
    @Published private(set) var isPlaying = false
    @Published private(set) var isConnecting = false
    @Published private(set) var isExternalPlaybackActive = false
    @Published private(set) var streamBitrateKbps: Int?
    @Published private(set) var streamThroughputKbps: Int?
    @Published private(set) var meterLevel: Float = 0.0
    @Published var volume: Float = 0.8 {
        didSet {
            applyOutputGain()
        }
    }

    private var player: AVPlayer?
    private var queue: [RadioStation] = []
    private var currentIndex: Int?
    private var streamURLs: [URL] = []
    private var streamIndex = 0
    private var statusObservation: NSKeyValueObservation?
    private var timeControlObservation: NSKeyValueObservation?
    private var rateObservation: NSKeyValueObservation?
    private var externalPlaybackObservation: NSKeyValueObservation?
    private var stationVariantsByPrimaryId: [String: [RadioStation]] = [:]
    private let pathMonitor = NWPathMonitor()
    private let pathMonitorQueue = DispatchQueue(label: "RadioGlass.NetworkPathMonitor")
    private let systemVolumeController = SystemVolumeController()
    private let volumeQueue = DispatchQueue(label: "RadioGlass.Volume", qos: .utility)
    private var pendingVolumeWork: DispatchWorkItem?
    private var prefersLowerBitrate = false
    private var playSessionID = 0
    private var stalledObserver: NSObjectProtocol?
    private var failedToEndObserver: NSObjectProtocol?
    private var connectTimeoutTask: Task<Void, Never>?
    private let externalSafetyCap: Float = 0.55
    private let audioOnlyAirPlayMode = true
    private var statsTimer: Timer?

    init() {
        pathMonitor.pathUpdateHandler = { [weak self] path in
            guard let self else { return }
            Task { @MainActor in
                self.prefersLowerBitrate = path.isConstrained || path.isExpensive
            }
        }
        pathMonitor.start(queue: pathMonitorQueue)
    }

    deinit {
        pathMonitor.cancel()
        connectTimeoutTask?.cancel()
        statsTimer?.invalidate()
        if let stalledObserver {
            NotificationCenter.default.removeObserver(stalledObserver)
        }
        if let failedToEndObserver {
            NotificationCenter.default.removeObserver(failedToEndObserver)
        }
    }

    func setStationVariants(_ variantsByPrimaryId: [String: [RadioStation]]) {
        stationVariantsByPrimaryId = variantsByPrimaryId
    }

    func play(_ station: RadioStation, in queue: [RadioStation]? = nil) {
        playSessionID += 1
        let sessionID = playSessionID
        connectTimeoutTask?.cancel()

        if let queue, !queue.isEmpty {
            self.queue = queue
            self.currentIndex = queue.firstIndex(where: { $0.id == station.id })
        } else if self.queue.isEmpty {
            self.queue = [station]
            self.currentIndex = 0
        } else if let idx = self.queue.firstIndex(where: { $0.id == station.id }) {
            self.currentIndex = idx
        }

        streamURLs = buildStreamURLs(for: station)
        streamIndex = 0
        currentStation = station
        isPlaying = false
        isConnecting = true
        streamBitrateKbps = inferredBitrate(for: station)
        streamThroughputKbps = nil
        meterLevel = 0
        guard !streamURLs.isEmpty else {
            isConnecting = false
            return
        }

        startStream(at: streamIndex, sessionID: sessionID)

        MPNowPlayingInfoCenter.default().nowPlayingInfo = [
            MPMediaItemPropertyTitle: station.name,
            MPMediaItemPropertyArtist: station.country,
            MPNowPlayingInfoPropertyIsLiveStream: true,
            MPNowPlayingInfoPropertyPlaybackRate: 1.0
        ]
    }

    func togglePlayback() {
        guard let player else { return }
        if isPlaying {
            player.pause()
            isPlaying = false
        } else {
            player.play()
            isPlaying = true
            isConnecting = false
        }
    }

    func playNext() {
        guard !queue.isEmpty else { return }
        let next = ((currentIndex ?? -1) + 1) % queue.count
        currentIndex = next
        play(queue[next])
    }

    func playPrevious() {
        guard !queue.isEmpty else { return }
        let previous = ((currentIndex ?? 1) - 1 + queue.count) % queue.count
        currentIndex = previous
        play(queue[previous])
    }

    private func startStream(at index: Int, sessionID: Int) {
        guard streamURLs.indices.contains(index) else {
            isPlaying = false
            isConnecting = false
            return
        }

        let item = AVPlayerItem(url: streamURLs[index])
        isConnecting = true
        statusObservation = nil
        statusObservation = item.observe(\.status, options: [.new]) { [weak self] item, _ in
            guard let self else { return }
            Task { @MainActor in
                guard sessionID == self.playSessionID else { return }
                if item.status == .failed {
                    self.tryNextStream(sessionID: sessionID)
                } else if item.status == .readyToPlay {
                    self.isConnecting = false
                    self.isPlaying = true
                }
            }
        }

        if player == nil {
            player = AVPlayer(playerItem: item)
            player?.allowsExternalPlayback = !audioOnlyAirPlayMode
            observePlayerTimeControl()
            observePlayerRate()
            observeExternalPlayback()
        } else {
            player?.replaceCurrentItem(with: item)
        }

        installItemObservers(item: item, sessionID: sessionID)
        scheduleConnectTimeout(sessionID: sessionID)
        applyOutputGain()
        player?.play()
        isPlaying = true
    }

    private func tryNextStream(sessionID: Int) {
        guard sessionID == playSessionID else { return }
        connectTimeoutTask?.cancel()
        let next = streamIndex + 1
        guard streamURLs.indices.contains(next) else {
            isPlaying = false
            isConnecting = false
            return
        }
        streamIndex = next
        startStream(at: streamIndex, sessionID: sessionID)
    }

    private func buildStreamURLs(for station: RadioStation) -> [URL] {
        var candidates = stationVariantsByPrimaryId[station.id] ?? [station]
        if !candidates.contains(where: { $0.id == station.id }) {
            candidates.insert(station, at: 0)
        }

        candidates = dedupeStations(candidates)
        candidates.sort { lhs, rhs in
            let lhsBitrate = inferredBitrate(for: lhs)
            let rhsBitrate = inferredBitrate(for: rhs)
            if lhsBitrate != rhsBitrate {
                return prefersLowerBitrate ? lhsBitrate < rhsBitrate : lhsBitrate > rhsBitrate
            }

            let lhsPopularity = (lhs.votes ?? 0) + (lhs.clickcount ?? 0)
            let rhsPopularity = (rhs.votes ?? 0) + (rhs.clickcount ?? 0)
            return lhsPopularity > rhsPopularity
        }

        var urls = candidates
            .flatMap { station in
                [station.urlResolved, station.url]
                    .compactMap { $0 }
                    .compactMap { URL(string: $0) }
            }
        urls = Array(NSOrderedSet(array: urls)) as? [URL] ?? urls
        return urls
    }

    func isCurrentStation(_ station: RadioStation) -> Bool {
        currentStation?.id == station.id
    }

    func isCurrentlyPlaying(_ station: RadioStation) -> Bool {
        isCurrentStation(station) && isPlaying
    }

    private func dedupeStations(_ stations: [RadioStation]) -> [RadioStation] {
        var seen = Set<String>()
        var output: [RadioStation] = []

        for station in stations where !seen.contains(station.id) {
            seen.insert(station.id)
            output.append(station)
        }

        return output
    }

    private func inferredBitrate(for station: RadioStation) -> Int {
        let patterns = [station.name, station.urlResolved, station.url ?? ""]
        for value in patterns {
            if let bitrate = extractBitrate(from: value) {
                return bitrate
            }
        }

        // Neutral default when bitrate metadata is missing.
        return 128
    }

    private func extractBitrate(from text: String) -> Int? {
        let lowered = text.lowercased()
        let regex = #"(32|48|64|96|112|128|160|192|256|320)\s?(k|kbps)\b"#
        guard let range = lowered.range(of: regex, options: .regularExpression) else {
            return nil
        }

        let token = String(lowered[range])
        let digits = token.prefix { $0.isNumber }
        return Int(digits)
    }

    private func observePlayerTimeControl() {
        guard let player else { return }
        timeControlObservation = player.observe(\.timeControlStatus, options: [.new, .initial]) { [weak self] player, _ in
            guard let self else { return }
            Task { @MainActor in
                switch player.timeControlStatus {
                case .playing:
                    self.isPlaying = true
                    self.isConnecting = false
                    self.startStatsTimer()
                case .paused:
                    self.isPlaying = false
                    self.stopStatsTimerIfIdle()
                case .waitingToPlayAtSpecifiedRate:
                    self.isPlaying = false
                    self.isConnecting = true
                    self.startStatsTimer()
                @unknown default:
                    self.isPlaying = false
                }
            }
        }
    }

    private func observePlayerRate() {
        guard let player else { return }
        rateObservation = player.observe(\.rate, options: [.new, .initial]) { [weak self] player, _ in
            guard let self else { return }
            Task { @MainActor in
                if player.rate > 0 {
                    self.isPlaying = true
                    self.isConnecting = false
                } else if player.timeControlStatus == .paused {
                    self.isPlaying = false
                }
            }
        }
    }

    private func observeExternalPlayback() {
        guard let player else { return }
        externalPlaybackObservation = player.observe(\.isExternalPlaybackActive, options: [.new, .initial]) { [weak self] player, _ in
            guard let self else { return }
            Task { @MainActor in
                self.isExternalPlaybackActive = player.isExternalPlaybackActive
                self.applyOutputGain()
            }
        }
    }

    private func applyOutputGain() {
        let clampedVolume = max(0, min(1, volume))
        let shouldCap = audioOnlyAirPlayMode || isExternalPlaybackActive
        let effective = shouldCap ? min(clampedVolume, externalSafetyCap) : clampedVolume
        player?.volume = effective
        applyItemAudioMix(gain: effective)

        // Avoid blocking the main thread with CoreAudio calls.
        let shouldControlSystem = isExternalPlaybackActive || audioOnlyAirPlayMode
        guard shouldControlSystem else { return }
        pendingVolumeWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            _ = self.systemVolumeController.setOutputVolume(effective)
        }
        pendingVolumeWork = work
        volumeQueue.asyncAfter(deadline: .now() + 0.15, execute: work)
    }

    private func applyItemAudioMix(gain: Float) {
        guard let item = player?.currentItem else { return }
        Task { @MainActor in
            let audioTracks: [AVAssetTrack]
            do {
                if #available(macOS 13.0, *) {
                    audioTracks = try await item.asset.loadTracks(withMediaType: .audio)
                } else {
                    audioTracks = item.asset.tracks(withMediaType: .audio)
                }
            } catch {
                return
            }

            guard !audioTracks.isEmpty else { return }
            let mix = AVMutableAudioMix()
            mix.inputParameters = audioTracks.map { track in
                let params = AVMutableAudioMixInputParameters(track: track)
                params.setVolume(gain, at: .zero)
                return params
            }
            item.audioMix = mix
        }
    }

    private func installItemObservers(item: AVPlayerItem, sessionID: Int) {
        if let stalledObserver {
            NotificationCenter.default.removeObserver(stalledObserver)
        }
        if let failedToEndObserver {
            NotificationCenter.default.removeObserver(failedToEndObserver)
        }

        stalledObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemPlaybackStalled,
            object: item,
            queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in
                self.tryNextStream(sessionID: sessionID)
            }
        }

        failedToEndObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemFailedToPlayToEndTime,
            object: item,
            queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in
                self.tryNextStream(sessionID: sessionID)
            }
        }
    }

    private func scheduleConnectTimeout(sessionID: Int) {
        connectTimeoutTask?.cancel()
        connectTimeoutTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 12_000_000_000)
            guard let self else { return }
            await MainActor.run {
                guard sessionID == self.playSessionID, self.isConnecting else { return }
                self.tryNextStream(sessionID: sessionID)
            }
        }
    }

    private func startStatsTimer() {
        guard statsTimer == nil else { return }
        statsTimer = Timer.scheduledTimer(withTimeInterval: 0.6, repeats: true) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in
                self.refreshStreamStats()
            }
        }
    }

    private func stopStatsTimerIfIdle() {
        if !isPlaying && !isConnecting {
            statsTimer?.invalidate()
            statsTimer = nil
        }
    }

    private func refreshStreamStats() {
        if let player {
            let status = player.timeControlStatus
            if status == .playing {
                isPlaying = true
                isConnecting = false
            }
        }

        guard let item = player?.currentItem else { return }
        if let event = item.accessLog()?.events.last {
            if event.indicatedBitrate > 0 {
                streamBitrateKbps = Int(event.indicatedBitrate / 1000.0)
            }
            if event.observedBitrate > 0 {
                streamThroughputKbps = Int(event.observedBitrate / 1000.0)
                isPlaying = true
                isConnecting = false
            }
        }

        let now = Date().timeIntervalSinceReferenceDate
        let throughput = Double(streamThroughputKbps ?? 0)
        let base = min(1.0, max(0.15, throughput / 320.0))
        let pulse = 0.25 * (sin(now * 3.2) + 1) * 0.5
        let target = isPlaying ? min(1.0, base + pulse) : 0.05
        meterLevel = Float(target)
    }
}
