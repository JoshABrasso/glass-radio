import Foundation

final class FavoritesStore {
    private let key = "rg_presets_v2"

    func load() -> [RadioStation] {
        guard let data = UserDefaults.standard.data(forKey: key) else { return [] }
        return (try? JSONDecoder().decode([RadioStation].self, from: data)) ?? []
    }

    func save(_ stations: [RadioStation]) {
        guard let data = try? JSONEncoder().encode(stations) else { return }
        UserDefaults.standard.set(data, forKey: key)
    }
}
