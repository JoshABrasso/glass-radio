import SwiftUI

struct PlayerBarView: View {
    @EnvironmentObject var viewModel: AppViewModel
    @ObservedObject var player: AudioPlayerManager

    var body: some View {
        HStack(alignment: .center, spacing: 18) {
            leftNowPlaying
                .frame(width: 340, alignment: .leading)
                .frame(height: 44, alignment: .center)

            HStack(spacing: 18) {
                centerTransport
                StreamStatusView(
                    level: player.meterLevel,
                    throughputKbps: player.streamThroughputKbps,
                    fallbackBitrateKbps: player.streamBitrateKbps
                )
                .frame(width: 150, alignment: .leading)
            }
            .frame(width: 360, alignment: .center)

            rightControls
                .frame(width: 240, alignment: .trailing)
                .frame(height: 44, alignment: .center)
        }
        .frame(height: 52, alignment: .center)
        .padding(.horizontal, 18)
        .padding(.vertical, 4)
        .playerPanel()
    }

    private var leftNowPlaying: some View {
        HStack(spacing: 9) {
            if let station = viewModel.nowPlayingStation ?? viewModel.player.currentStation {
                StationArtworkView(station: station, cornerRadius: 4)
                    .frame(width: 38, height: 38)

                VStack(alignment: .leading, spacing: 2) {
                    Text(station.name)
                        .font(.callout.weight(.semibold))
                        .foregroundStyle(.white)
                        .lineLimit(1)
                    Text(station.country)
                        .font(.footnote)
                        .foregroundStyle(.white.opacity(0.62))
                        .lineLimit(1)
                }
                
                Button(action: { viewModel.togglePreset(station) }) {
                    Image(systemName: viewModel.isPreset(station) ? "star.fill" : "star")
                        .font(.body)
                        .foregroundStyle(viewModel.isPreset(station) ? .yellow : .white.opacity(0.75))
                }
                .buttonStyle(.plain)
            } else {
                Text("No Station")
                    .font(.callout)
                    .foregroundStyle(.white.opacity(0.72))
            }
        }
        .frame(width: 340, alignment: .leading)
    }

    private var centerTransport: some View {
        HStack(spacing: 16) {
            Button(action: { viewModel.playPrevious() }) {
                Image(systemName: "backward.fill")
                    .font(.body)
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(0.82))

            Button(action: { viewModel.player.togglePlayback() }) {
                Image(systemName: viewModel.player.isPlaying ? "pause.fill" : "play.fill")
                    .font(.body.weight(.bold))
                    .frame(width: 32, height: 32)
                    .background(.white, in: Circle())
                    .foregroundStyle(.black)
            }
            .buttonStyle(.plain)

            Button(action: { viewModel.playNext() }) {
                Image(systemName: "forward.fill")
                    .font(.body)
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(0.82))

        }
        .frame(maxWidth: .infinity, alignment: .center)
    }

    private var rightControls: some View {
        HStack(spacing: 10) {
            HStack(spacing: 6) {
                Image(systemName: "speaker.fill")
                    .font(.body)
                    .foregroundStyle(.white.opacity(0.62))

                Slider(value: Binding(
                    get: { Double(player.volume) },
                    set: { player.volume = Float($0) }
                ), in: 0...1)
                .frame(width: 120)
                .tint(.white.opacity(0.82))
            }

            if player.isExternalPlaybackActive {
                Image(systemName: "airplayaudio")
                    .font(.body)
                    .foregroundStyle(.blue.opacity(0.9))
            }

            AirPlayRoutePicker()
                .frame(width: 26, height: 20)
        }
        .frame(width: 240, alignment: .trailing)
    }
}

private struct StreamStatusView: View {
    let level: Float
    let throughputKbps: Int?
    let fallbackBitrateKbps: Int?

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
                .font(.caption.weight(.semibold))
                .foregroundStyle(.white.opacity(0.9))
                .lineLimit(1)
                .minimumScaleFactor(0.9)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(Color.white.opacity(0.10), in: Capsule())
        .fixedSize()
    }

    private func barHeight(for index: Int, time: TimeInterval) -> CGFloat {
        let phase = Double(index) * 0.7
        let pulse = 0.35 * (sin(time * 4.2 + phase) + 1) * 0.5
        let base = Double(level)
        let height = max(4, min(16, (base + pulse) * 16))
        return height
    }

    private var labelText: String {
        if let throughput = throughputKbps {
            return "\(throughput) kbps"
        } else if let bitrate = fallbackBitrateKbps {
            return "\(bitrate) kbps"
        } else {
            return "-- kbps"
        }
    }
}
