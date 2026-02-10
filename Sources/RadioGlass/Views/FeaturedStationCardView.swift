import SwiftUI

struct FeaturedStationCardView: View {
    let station: RadioStation
    let onPlay: () -> Void
    let onTogglePreset: () -> Void
    let isPreset: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Button(action: onPlay) {
                StationArtworkView(station: station)
                    .frame(maxWidth: .infinity)
                    .aspectRatio(1, contentMode: .fit)
            }
            .buttonStyle(.plain)

            Text(station.name)
                .font(.caption.weight(.semibold))
                .foregroundStyle(.white)
                .lineLimit(1)

            HStack(spacing: 6) {
                Text(station.country)
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.62))
                    .lineLimit(1)

                Spacer(minLength: 0)

                Button(action: onTogglePreset) {
                    Image(systemName: isPreset ? "star.fill" : "star")
                        .font(.caption2)
                        .foregroundStyle(isPreset ? .yellow : .white.opacity(0.62))
                }
                .buttonStyle(.plain)
            }
        }
        .padding(8)
        .glassPanel()
        .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}
