import SwiftUI

struct PlayerBarView: View {
    @EnvironmentObject var viewModel: AppViewModel
    @ObservedObject var player: AudioPlayerManager
    @State private var showEQ = false

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
                .frame(width: 260, alignment: .trailing)
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
                        .foregroundStyle(RadioGlassTheme.primaryText)
                        .lineLimit(1)
                    Text(station.country)
                        .font(.footnote)
                        .foregroundStyle(RadioGlassTheme.secondaryText)
                        .lineLimit(1)
                }
                
                Button(action: { viewModel.togglePreset(station) }) {
                    Image(systemName: viewModel.isPreset(station) ? "star.fill" : "star")
                        .font(.body)
                        .foregroundStyle(viewModel.isPreset(station) ? RadioGlassTheme.favorite : RadioGlassTheme.secondaryText)
                }
                .buttonStyle(.plain)
            } else {
                Text("No Station")
                    .font(.callout)
                    .foregroundStyle(RadioGlassTheme.secondaryText)
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
            .foregroundStyle(RadioGlassTheme.secondaryText)

            Button(action: { viewModel.player.togglePlayback() }) {
                Image(systemName: viewModel.player.isPlaying ? "pause.fill" : "play.fill")
                    .font(.body.weight(.bold))
                    .frame(width: 32, height: 32)
                    .background(RadioGlassTheme.prominentControlFill, in: Circle())
                    .foregroundStyle(RadioGlassTheme.prominentControlText)
            }
            .buttonStyle(.plain)

            Button(action: { viewModel.playNext() }) {
                Image(systemName: "forward.fill")
                    .font(.body)
            }
            .buttonStyle(.plain)
            .foregroundStyle(RadioGlassTheme.secondaryText)

        }
        .frame(maxWidth: .infinity, alignment: .center)
    }

    private var rightControls: some View {
        HStack(spacing: 10) {
            // EQ toggle button
            Button {
                showEQ.toggle()
            } label: {
                EQStatusBadge(eq: player.eq)
            }
            .buttonStyle(.plain)
            .popover(isPresented: $showEQ, arrowEdge: .top) {
                EQView(eq: player.eq)
                    .padding(0)
            }

            HStack(spacing: 6) {
                Image(systemName: "speaker.fill")
                    .font(.body)
                    .foregroundStyle(RadioGlassTheme.secondaryText)

                Slider(value: Binding(
                    get: { Double(player.volume) },
                    set: { player.volume = Float($0) }
                ), in: 0...1)
                .frame(width: 100)
                .tint(.accentColor)
            }

            if player.isExternalPlaybackActive {
                Image(systemName: "airplayaudio")
                    .font(.body)
                    .foregroundStyle(Color.accentColor)
            }

            AirPlayRoutePicker()
                .frame(width: 26, height: 20)
        }
        .frame(width: 260, alignment: .trailing)
    }
}

private struct StreamStatusView: View {
    let level: Float
    let throughputKbps: Int?
    let fallbackBitrateKbps: Int?

    var body: some View {
        HStack(spacing: 6) {
            HStack(spacing: 2) {
                ForEach(0..<4, id: \.self) { idx in
                    Capsule()
                        .fill(RadioGlassTheme.live.opacity(0.85))
                        .frame(width: 3, height: barHeight(for: idx))
                }
            }

            Text(labelText)
                .font(.caption.weight(.semibold))
                .foregroundStyle(RadioGlassTheme.primaryText)
                .lineLimit(1)
                .minimumScaleFactor(0.9)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(RadioGlassTheme.subtleFill, in: Capsule())
        .fixedSize()
    }

    private func barHeight(for index: Int) -> CGFloat {
        let offsets: [Double] = [0.12, 0.28, 0.18, 0.34]
        let base = Double(level)
        let height = max(4, min(16, (base + offsets[index]) * 16))
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
