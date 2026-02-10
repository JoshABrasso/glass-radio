import SwiftUI

struct CountryStationRowView: View {
    let station: RadioStation
    let isPreset: Bool
    let onPlay: () -> Void
    let onTogglePreset: () -> Void

    @EnvironmentObject private var player: AudioPlayerManager
    @State private var isHovering = false

    private var isPlaying: Bool {
        player.isCurrentlyPlaying(station)
    }

    var body: some View {
        HStack(spacing: 10) {
            StationArtworkView(station: station, cornerRadius: 5)
                .frame(width: 34, height: 34)

            VStack(alignment: .leading, spacing: 1) {
                Text(station.name)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.white)
                    .lineLimit(1)

                Text(station.country)
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.62))
                    .lineLimit(1)
            }

            Spacer(minLength: 8)

            if isPlaying {
                Text("LIVE")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(.black)
                    .padding(.horizontal, 7)
                    .padding(.vertical, 3)
                    .background(Color.mint, in: Capsule())
            }

            Button(action: onTogglePreset) {
                Image(systemName: isPreset ? "star.fill" : "star")
                    .font(.caption)
                    .foregroundStyle(isPreset ? .yellow : .white.opacity(0.75))
                    .opacity((isHovering || isPreset) ? 1 : 0)
            }
            .buttonStyle(.plain)

            Button(action: onPlay) {
                Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                    .font(.caption)
                    .foregroundStyle(.white)
                    .frame(width: 24, height: 24)
                    .background(Color.white.opacity(0.14), in: Circle())
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
        .background(Color.white.opacity(isHovering ? 0.07 : 0.03), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .onHover { hovering in
            isHovering = hovering
        }
    }
}
