import SwiftUI

struct CountryStationRowView: View {
    let station: RadioStation
    let isPreset: Bool
    let onPlay: () -> Void
    let onTogglePreset: () -> Void

    @EnvironmentObject private var player: AudioPlayerManager

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
                    .foregroundStyle(RadioGlassTheme.primaryText)
                    .lineLimit(1)

                Text(station.country)
                    .font(.caption2)
                    .foregroundStyle(RadioGlassTheme.secondaryText)
                    .lineLimit(1)
            }

            Spacer(minLength: 8)

            if isPlaying {
                Text("LIVE")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(RadioGlassTheme.prominentControlText)
                    .padding(.horizontal, 7)
                    .padding(.vertical, 3)
                    .background(RadioGlassTheme.live, in: Capsule())
            }

            Button(action: onTogglePreset) {
                Image(systemName: isPreset ? "star.fill" : "star")
                    .font(.caption)
                    .foregroundStyle(isPreset ? RadioGlassTheme.favorite : RadioGlassTheme.secondaryText)
            }
            .buttonStyle(.plain)

            Button(action: onPlay) {
                Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                    .font(.caption)
                    .foregroundStyle(RadioGlassTheme.primaryText)
                    .frame(width: 24, height: 24)
                    .background(RadioGlassTheme.hoverFill, in: Circle())
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
        .background(isPlaying ? RadioGlassTheme.hoverFill : RadioGlassTheme.subtleFill, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
    }
}
