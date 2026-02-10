import SwiftUI

struct StationRowView: View {
    let station: RadioStation
    let isPreset: Bool
    let isPlaying: Bool
    let onPlay: () -> Void
    let onTogglePreset: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            StationArtworkView(station: station, cornerRadius: 8)
                .frame(width: 46, height: 46)

            VStack(alignment: .leading, spacing: 2) {
                Text(station.name)
                    .foregroundStyle(.white)
                    .font(.headline)
                    .lineLimit(1)
                Text(station.country)
                    .foregroundStyle(.white.opacity(0.62))
                    .font(.caption)
                    .lineLimit(1)
            }

            Spacer()

            if isPlaying {
                Text("LIVE")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(.black)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(.mint, in: Capsule())
            }

            Button(action: onTogglePreset) {
                Image(systemName: isPreset ? "star.fill" : "star")
                    .foregroundStyle(isPreset ? .yellow : .white.opacity(0.75))
            }
            .buttonStyle(.plain)

            Button(action: onPlay) {
                Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                    .foregroundStyle(.white)
                    .frame(width: 34, height: 34)
                    .background(.white.opacity(0.16), in: Circle())
            }
            .buttonStyle(.plain)
        }
        .padding(10)
        .glassPanel()
    }
}
