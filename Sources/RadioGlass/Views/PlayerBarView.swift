import SwiftUI

struct PlayerBarView: View {
    @EnvironmentObject var viewModel: AppViewModel
    @ObservedObject var player: AudioPlayerManager

    var body: some View {
        HStack(spacing: 14) {
            leftNowPlaying
            Spacer(minLength: 16)
            centerTransport
            Spacer(minLength: 16)
            rightControls
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 6)
        .playerPanel()
    }

    private var leftNowPlaying: some View {
        HStack(spacing: 7) {
            if let station = viewModel.nowPlayingStation ?? viewModel.player.currentStation {
                StationArtworkView(station: station, cornerRadius: 4)
                    .frame(width: 32, height: 32)

                VStack(alignment: .leading, spacing: 0) {
                    Text(station.name)
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.white)
                        .lineLimit(1)
                    Text(station.country)
                        .font(.caption2)
                        .foregroundStyle(.white.opacity(0.58))
                        .lineLimit(1)
                }
                
                Button(action: { viewModel.togglePreset(station) }) {
                    Image(systemName: viewModel.isPreset(station) ? "star.fill" : "star")
                        .font(.caption)
                        .foregroundStyle(viewModel.isPreset(station) ? .yellow : .white.opacity(0.7))
                }
                .buttonStyle(.plain)
            } else {
                Text("No Station")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))
            }
        }
        .frame(width: 250, alignment: .leading)
    }

    private var centerTransport: some View {
        HStack(spacing: 12) {
            Button(action: { viewModel.playPrevious() }) {
                Image(systemName: "backward.fill")
                    .font(.caption)
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(0.82))

            Button(action: { viewModel.player.togglePlayback() }) {
                Image(systemName: viewModel.player.isPlaying ? "pause.fill" : "play.fill")
                    .font(.caption.weight(.bold))
                    .frame(width: 26, height: 26)
                    .background(.white, in: Circle())
                    .foregroundStyle(.black)
            }
            .buttonStyle(.plain)

            Button(action: { viewModel.playNext() }) {
                Image(systemName: "forward.fill")
                    .font(.caption)
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(0.82))

            if player.isConnecting {
                ProgressView()
                    .controlSize(.small)
                    .tint(.white.opacity(0.82))
            } else if player.isPlaying || player.streamBitrateKbps != nil || player.streamThroughputKbps != nil {
                StreamStatusView(
                    level: player.meterLevel,
                    bitrateKbps: player.streamBitrateKbps,
                    throughputKbps: player.streamThroughputKbps
                )
            }
        }
    }

    private var rightControls: some View {
        HStack(spacing: 10) {
            HStack(spacing: 6) {
                Image(systemName: "speaker.fill")
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.62))

                Slider(value: Binding(
                    get: { Double(player.volume) },
                    set: { player.volume = Float($0) }
                ), in: 0...1)
                .frame(width: 84)
                .tint(.white.opacity(0.82))
            }

            if player.isExternalPlaybackActive {
                Image(systemName: "airplayaudio")
                    .font(.caption2)
                    .foregroundStyle(.blue.opacity(0.9))
            }

            AirPlayRoutePicker()
                .frame(width: 24, height: 18)
        }
        .frame(width: 250, alignment: .trailing)
    }
}

private struct StreamStatusView: View {
    let level: Float
    let bitrateKbps: Int?
    let throughputKbps: Int?

    var body: some View {
        HStack(spacing: 6) {
            TimelineView(.animation) { context in
                HStack(spacing: 2) {
                    ForEach(0..<4, id: \.self) { idx in
                        Capsule()
                            .fill(Color.green.opacity(0.85))
                            .frame(width: 3, height: barHeight(for: idx, time: context.date.timeIntervalSinceReferenceDate))
                    }
                }
            }

            Text(labelText)
                .font(.caption2)
                .foregroundStyle(.white.opacity(0.7))
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 3)
        .background(Color.white.opacity(0.10), in: Capsule())
    }

    private func barHeight(for index: Int, time: TimeInterval) -> CGFloat {
        let phase = Double(index) * 0.7
        let pulse = 0.35 * (sin(time * 4.2 + phase) + 1) * 0.5
        let base = Double(level)
        let height = max(4, min(16, (base + pulse) * 16))
        return height
    }

    private var labelText: String {
        let bitrate = bitrateKbps.map { "Bitrate \($0) kbps" } ?? "Bitrate --"
        let throughput = throughputKbps.map { "Throughput \($0) kbps" } ?? "Throughput --"
        return "\(bitrate) • \(throughput)"
    }
}
