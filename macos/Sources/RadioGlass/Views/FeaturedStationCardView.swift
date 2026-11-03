import SwiftUI

struct FeaturedStationCardView: View {
    let station: RadioStation
    let onPlay: () -> Void
    let onTogglePreset: () -> Void
    let isPreset: Bool

    @EnvironmentObject private var player: AudioPlayerManager

    private var isPlaying: Bool { player.isCurrentlyPlaying(station) }
    private var isCurrent: Bool { player.isCurrentStation(station) }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            artworkButton
            stationName
            stationMeta
        }
        .padding(8)
        .background(cardBackground)
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        .overlay(cardBorder)
        .contextMenu {
            Button {
                onPlay()
            } label: {
                Label(isPlaying ? "Pause" : "Play", systemImage: isPlaying ? "pause.fill" : "play.fill")
            }

            Button {
                onTogglePreset()
            } label: {
                Label(isPreset ? "Remove from Favourites" : "Add to Favourites",
                      systemImage: isPreset ? "star.slash" : "star")
            }

            Divider()

            Button {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(station.name, forType: .string)
            } label: {
                Label("Copy Station Name", systemImage: "doc.on.doc")
            }

            if let homepage = station.homepage, let url = URL(string: homepage) {
                Button {
                    NSWorkspace.shared.open(url)
                } label: {
                    Label("Open Station Website", systemImage: "safari")
                }
            }
        }
    }

    private var artworkButton: some View {
        Button(action: onPlay) {
            ZStack(alignment: .bottomTrailing) {
                StationArtworkView(station: station)
                    .frame(maxWidth: .infinity)
                    .aspectRatio(1, contentMode: .fit)

                if !isPlaying {
                    Circle()
                        .fill(.regularMaterial)
                        .frame(width: 38, height: 38)
                        .overlay(
                            Image(systemName: "play.fill")
                                .font(.body.weight(.semibold))
                                .foregroundStyle(RadioGlassTheme.primaryText)
                        )
                        .padding(6)
                }

                if isCurrent {
                    ZStack {
                        Capsule()
                            .fill(isPlaying ? RadioGlassTheme.live : RadioGlassTheme.paused)
                        HStack(spacing: 3) {
                            Image(systemName: isPlaying ? "waveform" : "pause.fill")
                                .font(.caption2.weight(.semibold))
                            Text(isPlaying ? "LIVE" : "PAUSED")
                                .font(.caption2.weight(.bold))
                        }
                        .foregroundStyle(RadioGlassTheme.prominentControlText)
                        .padding(.horizontal, 7)
                        .padding(.vertical, 3)
                    }
                    .fixedSize()
                    .padding(6)
                    .transition(.opacity)
                }
            }
        }
        .buttonStyle(.plain)
    }

    private var stationName: some View {
        Text(station.name)
            .font(.caption.weight(.semibold))
            .foregroundStyle(RadioGlassTheme.primaryText)
            .lineLimit(1)
    }

    private var stationMeta: some View {
        HStack(spacing: 6) {
            Text(station.country)
                .font(.caption2)
                .foregroundStyle(RadioGlassTheme.secondaryText)
                .lineLimit(1)

            Spacer(minLength: 0)

            Button(action: onTogglePreset) {
                Image(systemName: isPreset ? "star.fill" : "star")
                    .font(.caption2)
                    .foregroundStyle(isPreset ? RadioGlassTheme.favorite : RadioGlassTheme.secondaryText)
            }
            .buttonStyle(.plain)
        }
    }

    private var cardBackground: some View {
        RoundedRectangle(cornerRadius: 10, style: .continuous)
            .fill(isCurrent ? RadioGlassTheme.hoverFill : RadioGlassTheme.subtleFill)
    }

    private var cardBorder: some View {
        RoundedRectangle(cornerRadius: 10, style: .continuous)
            .strokeBorder(
                isCurrent ? Color.accentColor.opacity(0.35) : RadioGlassTheme.separator.opacity(0.55),
                lineWidth: 1
            )
    }
}
