import Foundation

protocol RadioServing {
    func fetchCountryStations(country: String, countryCode: String, limit: Int) async throws -> [RadioStation]
    func searchStations(query: String, limit: Int) async throws -> [RadioStation]
}

final class RadioBrowserService: RadioServing {
    private let baseURLs: [URL] = [
        URL(string: "https://de1.api.radio-browser.info/json")!,
        URL(string: "https://nl1.api.radio-browser.info/json")!,
        URL(string: "https://fr1.api.radio-browser.info/json")!
    ]
    private let decoder = JSONDecoder()

    func fetchCountryStations(country: String, countryCode: String, limit: Int = 800) async throws -> [RadioStation] {
        let safeLimit = max(150, min(limit, 3000))
        let code = countryCode.uppercased()
        let votesQuery = [
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "votes"),
            URLQueryItem(name: "reverse", value: "true"),
            URLQueryItem(name: "limit", value: String(safeLimit))
        ]
        let clicksQuery = [
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "clickcount"),
            URLQueryItem(name: "reverse", value: "true"),
            URLQueryItem(name: "limit", value: String(safeLimit))
        ]
        let alphaQuery = [
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "name"),
            URLQueryItem(name: "reverse", value: "false"),
            URLQueryItem(name: "limit", value: String(safeLimit))
        ]

        let encoded = country.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? country
        let votesByName = [
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "votes"),
            URLQueryItem(name: "reverse", value: "true"),
            URLQueryItem(name: "limit", value: String(safeLimit))
        ]
        let clicksByName = [
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "clickcount"),
            URLQueryItem(name: "reverse", value: "true"),
            URLQueryItem(name: "limit", value: String(safeLimit))
        ]
        let alphaByName = [
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "name"),
            URLQueryItem(name: "reverse", value: "false"),
            URLQueryItem(name: "limit", value: String(safeLimit))
        ]

        async let byCodeVotes = getStationsOrEmpty(path: "stations/bycountrycodeexact/\(code)", queryItems: votesQuery)
        async let byCodeClicks = getStationsOrEmpty(path: "stations/bycountrycodeexact/\(code)", queryItems: clicksQuery)
        async let byCodeAlpha = getStationsOrEmpty(path: "stations/bycountrycodeexact/\(code)", queryItems: alphaQuery)
        async let byNameVotes = getStationsOrEmpty(path: "stations/bycountryexact/\(encoded)", queryItems: votesByName)
        async let byNameClicks = getStationsOrEmpty(path: "stations/bycountryexact/\(encoded)", queryItems: clicksByName)
        async let byNameAlpha = getStationsOrEmpty(path: "stations/bycountryexact/\(encoded)", queryItems: alphaByName)

        let merged = mergeUnique(await byCodeVotes + byCodeClicks + byCodeAlpha + byNameVotes + byNameClicks + byNameAlpha)
        if !merged.isEmpty {
            return merged
        }

        throw URLError(.badServerResponse)
    }

    func searchStations(query: String, limit: Int = 120) async throws -> [RadioStation] {
        let clean = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { return [] }

        let items = [
            URLQueryItem(name: "name", value: clean),
            URLQueryItem(name: "hidebroken", value: "true"),
            URLQueryItem(name: "order", value: "votes"),
            URLQueryItem(name: "reverse", value: "true"),
            URLQueryItem(name: "limit", value: String(limit))
        ]
        return try await get(path: "stations/search", queryItems: items)
    }

    private func get<T: Decodable>(path: String, queryItems: [URLQueryItem]) async throws -> T {
        var lastError: Error = URLError(.cannotFindHost)

        for baseURL in baseURLs {
            var components = URLComponents(url: baseURL.appendingPathComponent(path), resolvingAgainstBaseURL: false)
            components?.queryItems = queryItems

            guard let url = components?.url else {
                lastError = URLError(.badURL)
                continue
            }

            var request = URLRequest(url: url)
            request.timeoutInterval = 12
            request.cachePolicy = .returnCacheDataElseLoad
            request.setValue("GlassRadio/2.0", forHTTPHeaderField: "User-Agent")

            do {
                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode) else {
                    lastError = URLError(.badServerResponse)
                    continue
                }

                return try decoder.decode(T.self, from: data)
            } catch {
                lastError = error
            }
        }

        throw lastError
    }

    private func mergeUnique(_ stations: [RadioStation]) -> [RadioStation] {
        var seen = Set<String>()
        var merged: [RadioStation] = []
        merged.reserveCapacity(stations.count)

        for station in stations where !seen.contains(station.stationuuid) {
            seen.insert(station.stationuuid)
            merged.append(station)
        }
        return merged
    }

    private func getStationsOrEmpty(path: String, queryItems: [URLQueryItem]) async -> [RadioStation] {
        do {
            return try await get(path: path, queryItems: queryItems)
        } catch {
            return []
        }
    }
}
