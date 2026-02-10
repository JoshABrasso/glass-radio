import AppKit
import SwiftUI

struct StationArtworkView: View {
    let station: RadioStation
    let cornerRadius: CGFloat

    @StateObject private var model: StationArtworkModel

    init(station: RadioStation, cornerRadius: CGFloat = 8) {
        self.station = station
        self.cornerRadius = cornerRadius
        _model = StateObject(wrappedValue: StationArtworkModel(station: station))
    }

    private var fallbackColors: [Color] {
        let hash = abs(station.stationuuid.hashValue)
        let palettes: [[Color]] = [
            [Color(red: 0.20, green: 0.28, blue: 0.45), Color(red: 0.14, green: 0.18, blue: 0.28)],
            [Color(red: 0.46, green: 0.24, blue: 0.20), Color(red: 0.24, green: 0.14, blue: 0.12)],
            [Color(red: 0.20, green: 0.40, blue: 0.34), Color(red: 0.11, green: 0.21, blue: 0.18)],
            [Color(red: 0.34, green: 0.24, blue: 0.45), Color(red: 0.18, green: 0.14, blue: 0.24)]
        ]
        return palettes[hash % palettes.count]
    }

    var body: some View {
        GeometryReader { geo in
            Group {
                if let image = model.image {
                    Image(nsImage: image)
                        .resizable()
                        .scaledToFill()
                } else {
                    ZStack(alignment: .bottomLeading) {
                        LinearGradient(
                            colors: fallbackColors,
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )

                        Text(station.name)
                            .font(.caption2.weight(.semibold))
                            .foregroundStyle(.white.opacity(0.9))
                            .lineLimit(2)
                            .padding(8)
                    }
                }
            }
            .frame(width: geo.size.width, height: geo.size.height)
            .clipped()
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
        }
        .clipped()
        .task(id: station.stationuuid) {
            await model.loadIfNeeded(for: station)
        }
    }
}

@MainActor
final class StationArtworkModel: ObservableObject {
    @Published var image: NSImage?

    private var loadedStationID: String?

    init(station: RadioStation) {
        loadedStationID = nil
    }

    func loadIfNeeded(for station: RadioStation) async {
        if loadedStationID == station.stationuuid && image != nil {
            return
        }

        loadedStationID = station.stationuuid

        if let data = await StationLogoProvider.shared.logoData(for: station),
           let img = NSImage(data: data) {
            image = img
        } else {
            image = nil
        }
    }
}
