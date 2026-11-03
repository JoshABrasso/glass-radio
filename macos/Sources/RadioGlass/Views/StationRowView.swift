import SwiftUI

struct StationRowView: View {
    let station: RadioStation
    let isPreset: Bool
    let onPlay: () -> Void
    let onTogglePreset: () -> Void

    @EnvironmentObject private var player: AudioPlayerManager

    private var isPlaying: Bool {
        player.isCurrentlyPlaying(station)
    }

    var body: some View {
        HStack(spacing: 12) {
            StationArtworkView(station: station, cornerRadius: 8)
                .frame(width: 46, height: 46)

            VStack(alignment: .leading, spacing: 2) {
                Text(station.name)
                    .foregroundStyle(RadioGlassTheme.primaryText)
                    .font(.headline)
                    .lineLimit(1)
                Text(station.country)
                    .foregroundStyle(RadioGlassTheme.secondaryText)
                    .font(.caption)
                    .lineLimit(1)
            }

            Spacer()

            if isPlaying {
                Text("LIVE")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(RadioGlassTheme.prominentControlText)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(RadioGlassTheme.live, in: Capsule())
            }

            Button(action: onTogglePreset) {
                Image(systemName: isPreset ? "star.fill" : "star")
                    .foregroundStyle(isPreset ? RadioGlassTheme.favorite : RadioGlassTheme.secondaryText)
            }
            .buttonStyle(.plain)

            Button(action: onPlay) {
                Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                    .foregroundStyle(RadioGlassTheme.primaryText)
                    .frame(width: 34, height: 34)
                    .background(RadioGlassTheme.hoverFill, in: Circle())
            }
            .buttonStyle(.plain)
        }
        .padding(10)
        .glassPanel()
    }
}
