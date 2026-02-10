import Combine
import Foundation

@MainActor
final class AppViewModel: ObservableObject {
    private struct CountrySnapshot {
        let topStations: [RadioStation]
        let allStations: [RadioStation]
        let stationCount: Int
        let variantsByPrimary: [String: [RadioStation]]
    }

    enum StationSort: String, CaseIterable, Identifiable {
        case alphabetical = "A-Z"
        case mostPopular = "Most Popular"
        case mostListened = "Most Listened"
        case majorBrandsFirst = "Major Brands First"
        case leastPopular = "Least Popular"

        var id: String { rawValue }
    }
    
    enum StationFilter: String, CaseIterable, Identifiable {
        case all = "All"
        case presetsOnly = "Presets"
        case withArtwork = "With Artwork"
        case majorBrands = "Major Brands"
        case withGenres = "With Genres"

        var id: String { rawValue }
    }

    @Published var presets: [RadioStation] = []
    @Published var selectedCountry = CountryPreset.all.first ?? CountryPreset(id: "uk", displayName: "United Kingdom", apiName: "United Kingdom", topBrands: [])

    @Published var countryTopStations: [RadioStation] = []
    @Published var countryAllStations: [RadioStation] = []
    @Published var selectedSort: StationSort = .alphabetical
    @Published var selectedFilter: StationFilter = .all
    @Published var selectedGenre: String = "All"
    @Published private(set) var countryStationCount = 0

    @Published var discoverStations: [RadioStation] = []
    @Published var searchText = ""
    @Published var searchResults: [RadioStation] = []
    @Published var isLoading = false
    @Published var errorMessage: String?
    @Published var countryInfoMessage: String?
    @Published var nowPlayingStation: RadioStation?

    let player = AudioPlayerManager()

    private let service: RadioServing
    private let store = FavoritesStore()
    private var playerCancellable: AnyCancellable?
    private var stationVariantsByPrimaryId: [String: [RadioStation]] = [:]
    private var countryCache: [String: CountrySnapshot] = [:]
    private var activeCountryLoadToken = UUID()
    private var backgroundCacheTask: Task<Void, Never>?
    private var recentCountryIDs: [String] = []

    init(service: RadioServing = RadioBrowserService()) {
        self.service = service
        self.presets = store.load()
        self.playerCancellable = player.objectWillChange.sink { [weak self] _ in
            self?.objectWillChange.send()
        }

        Task {
            await loadCountry(selectedCountry)
            await loadDiscover()
        }

        startBackgroundCaching()
    }

    var filteredCountryStations: [RadioStation] {
        var base = countryAllStations

        switch selectedFilter {
        case .all:
            break
        case .presetsOnly:
            base = base.filter { presets.contains($0) }
        case .withArtwork:
            base = base.filter { !($0.favicon?.isEmpty ?? true) }
        case .majorBrands:
            base = base.filter { matchesMajorBrand($0) }
        case .withGenres:
            base = base.filter { !$0.genres.isEmpty }
        }

        switch selectedSort {
        case .alphabetical:
            return base.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        case .mostPopular:
            return base.sorted { weightedPopularity($0) > weightedPopularity($1) }
        case .mostListened:
            return base.sorted { ($0.clickcount ?? 0) > ($1.clickcount ?? 0) }
        case .majorBrandsFirst:
            return base.sorted { majorBrandPriority($0) > majorBrandPriority($1) }
        case .leastPopular:
            return base.sorted { popularity($0) < popularity($1) }
        }
    }
    
    var genreButtons: [String] {
        let canonicalGenres = ["All", "Pop", "Alternative", "Dance", "R&B", "Hip-Hop", "Rock", "Classic Rock", "Electronic", "Jazz", "Classical", "News", "Talk"]
        return canonicalGenres.filter { genre in
            if genre == "All" { return true }
            return countryAllStations.contains { station in
                station.genres.contains { normalizeGenre($0).localizedCaseInsensitiveContains(genre) }
            }
        }
    }

    func setSort(_ sort: StationSort) { selectedSort = sort }
    func setFilter(_ filter: StationFilter) { selectedFilter = filter }
    func setGenre(_ genre: String) { selectedGenre = genre }

    func loadCountry(_ country: CountryPreset) async {
        let loadToken = UUID()
        activeCountryLoadToken = loadToken
        recordRecentCountry(country.id)
        selectedCountry = country
        selectedSort = .alphabetical
        selectedFilter = .all
        selectedGenre = "All"
        isLoading = true
        errorMessage = nil
        countryInfoMessage = nil

        if let cached = countryCache[country.id] {
            applySnapshot(cached)
            countryInfoMessage = "Refreshing \(country.displayName) stations..."
        }

        do {
            let quickStations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 700)
            guard isLoadActive(loadToken, for: country) else { return }
            applyStations(quickStations, for: country)

            async let expandedStationsTask = service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 2400)
            async let majorBrandTask = fetchMajorBrandStations(for: country)
            async let regionalTask = fetchRegionalStations(for: country)

            let expandedStations = try await expandedStationsTask
            let majorBrandStations = try await majorBrandTask
            let regionalStations = try await regionalTask
            guard isLoadActive(loadToken, for: country) else { return }

            applyStations(quickStations + expandedStations + majorBrandStations + regionalStations, for: country)
            countryInfoMessage = nil
        } catch {
            guard isLoadActive(loadToken, for: country) else { return }
            errorMessage = "Failed loading \(country.displayName)."
            countryTopStations = []
            countryAllStations = []
            countryStationCount = 0
            stationVariantsByPrimaryId = [:]
            player.setStationVariants([:])
        }

        if isLoadActive(loadToken, for: country) {
            isLoading = false
        }
    }

    func loadDiscover() async {
        let spotlight = Array(CountryPreset.all.prefix(6))
        var merged: [RadioStation] = []

        for country in spotlight {
            do {
                let stations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 300)
                let top = curatedTopStations(from: sanitizeStations(stations), for: country)
                merged.append(contentsOf: top.prefix(5))
            } catch {
                continue
            }
        }

        discoverStations = uniqueById(merged)
    }

    func performSearch() async {
        guard !searchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            searchResults = []
            return
        }

        do {
            searchResults = try await service.searchStations(query: searchText, limit: 120)
        } catch {
            errorMessage = "Search failed."
            searchResults = []
        }
    }

    func togglePreset(_ station: RadioStation) {
        if let idx = presets.firstIndex(of: station) {
            presets.remove(at: idx)
        } else {
            presets.append(station)
        }
        store.save(presets)
    }

    func isPreset(_ station: RadioStation) -> Bool {
        presets.contains(station)
    }

    func isCurrentStation(_ station: RadioStation) -> Bool {
        if let nowPlayingStation {
            return nowPlayingStation.id == station.id
        }
        return player.isCurrentStation(station)
    }

    func isCurrentlyPlaying(_ station: RadioStation) -> Bool {
        isCurrentStation(station) && player.isPlaying
    }

    func play(_ station: RadioStation, in list: [RadioStation]? = nil) {
        nowPlayingStation = station
        player.play(station, in: list)
    }

    func playNext() {
        player.playNext()
        nowPlayingStation = player.currentStation
    }

    func playPrevious() {
        player.playPrevious()
        nowPlayingStation = player.currentStation
    }

    private func sanitizeStations(_ stations: [RadioStation]) -> [RadioStation] {
        uniqueById(stations)
            .filter { station in
                let hasStream = !(station.urlResolved.isEmpty) || !(station.url?.isEmpty ?? true)
                let lower = station.name.lowercased()
                return hasStream && !lower.contains("scanner") && !lower.contains("test") && !lower.contains("localhost")
            }
    }

    private func curatedTopStations(from stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
        let curated = stations
            .compactMap { station -> (Int, RadioStation)? in
                guard let idx = country.topBrands.firstIndex(where: { brandMatches(stationName: station.name, brand: $0) }) else {
                    return nil
                }
                return (idx, station)
            }
            .sorted { lhs, rhs in
                if lhs.0 != rhs.0 { return lhs.0 < rhs.0 }
                return popularity(lhs.1) > popularity(rhs.1)
            }
            .map(\.1)

        return uniqueById(curated)
    }

    private func consolidateStationVariants(_ stations: [RadioStation]) -> (primaryStations: [RadioStation], variantsByPrimary: [String: [RadioStation]]) {
        var grouped: [String: [RadioStation]] = [:]

        for station in stations {
            let key = canonicalStationName(station.name)
            grouped[key, default: []].append(station)
        }

        var primaries: [RadioStation] = []
        var variantsByPrimary: [String: [RadioStation]] = [:]

        for group in grouped.values {
            let sortedGroup = group.sorted { lhs, rhs in
                if isLikelyVariantName(lhs.name) != isLikelyVariantName(rhs.name) {
                    return !isLikelyVariantName(lhs.name)
                }

                let lhsHasArtwork = !(lhs.favicon?.isEmpty ?? true)
                let rhsHasArtwork = !(rhs.favicon?.isEmpty ?? true)
                if lhsHasArtwork != rhsHasArtwork { return lhsHasArtwork }

                let lhsPopularity = popularity(lhs)
                let rhsPopularity = popularity(rhs)
                if lhsPopularity != rhsPopularity { return lhsPopularity > rhsPopularity }

                return lhs.name.count < rhs.name.count
            }

            guard let primary = sortedGroup.first else { continue }
            primaries.append(primary)
            variantsByPrimary[primary.id] = sortedGroup
        }

        return (uniqueById(primaries), variantsByPrimary)
    }

    private func popularity(_ station: RadioStation) -> Int {
        (station.votes ?? 0) + ((station.clickcount ?? 0) / 3)
    }
    
    private func weightedPopularity(_ station: RadioStation) -> Int {
        popularity(station) + majorBrandPriority(station)
    }
    
    private func majorBrandPriority(_ station: RadioStation) -> Int {
        guard let idx = selectedCountry.topBrands.firstIndex(where: { brandMatches(stationName: station.name, brand: $0) }) else {
            return 0
        }
        // Ensure flagship brands surface in "Most Popular" views.
        return 1_000_000 - (idx * 10_000)
    }
    
    private func matchesMajorBrand(_ station: RadioStation) -> Bool {
        selectedCountry.topBrands.contains { brandMatches(stationName: station.name, brand: $0) }
    }

    var genreFilteredStations: [RadioStation] {
        let base = countryAllStations.filter { station in
            if selectedGenre == "All" { return true }
            return station.genres.contains { normalizeGenre($0).localizedCaseInsensitiveContains(selectedGenre) }
        }

        return base.sorted { popularity($0) > popularity($1) }
    }
    
    private func normalizeGenre(_ raw: String) -> String {
        raw
            .replacingOccurrences(of: "-", with: " ")
            .replacingOccurrences(of: "_", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
    
    private func normalizeStationName(_ raw: String) -> String {
        raw
            .lowercased()
            .replacingOccurrences(of: "&", with: "and")
            .replacingOccurrences(of: "-", with: " ")
            .replacingOccurrences(of: "_", with: " ")
            .components(separatedBy: CharacterSet.alphanumerics.inverted)
            .joined(separator: " ")
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func canonicalStationName(_ raw: String) -> String {
        let simplified = raw
            .lowercased()
            .replacingOccurrences(of: #"\([^)]*\)"#, with: " ", options: .regularExpression)
            .replacingOccurrences(of: #"\[[^\]]*\]"#, with: " ", options: .regularExpression)
            .replacingOccurrences(of: #"\b\d{2,3}\s?(k|kbps)\b"#, with: " ", options: .regularExpression)
            .replacingOccurrences(of: #"\b(aac\+?|mp3|ogg|stream|live|hq|lq)\b"#, with: " ", options: .regularExpression)

        return normalizeStationName(simplified)
    }

    private func isLikelyVariantName(_ name: String) -> Bool {
        let lowered = name.lowercased()
        return lowered.range(of: #"\b\d{2,3}\s?(k|kbps)\b"#, options: .regularExpression) != nil
            || lowered.contains("aac")
            || lowered.contains("mp3")
            || lowered.contains("stream")
    }
    
    private func brandMatches(stationName: String, brand: String) -> Bool {
        let station = normalizeStationName(stationName)
        let target = normalizeStationName(brand)
        guard !station.isEmpty, !target.isEmpty else { return false }
        
        if station.contains(target) { return true }
        
        // brand token fallback for labels like "Heart London", "Virgin Radio UK", etc.
        let tokens = target.split(separator: " ").map(String.init)
        if tokens.count > 1 {
            return tokens.allSatisfy { station.contains($0) }
        }
        return false
    }
    
    private func stationBelongsToCountry(_ station: RadioStation, country: CountryPreset) -> Bool {
        let lhs = normalizeStationName(station.country)
        let a = normalizeStationName(country.apiName)
        let b = normalizeStationName(country.displayName)
        return lhs == a || lhs == b || lhs.contains(a) || lhs.contains(b)
    }
    
    private func fetchMajorBrandStations(for country: CountryPreset) async throws -> [RadioStation] {
        let merged = await withTaskGroup(of: [RadioStation].self) { group in
            for brand in country.topBrands {
                group.addTask { [service] in
                    do {
                        return try await service.searchStations(query: brand, limit: 80)
                    } catch {
                        return []
                    }
                }
            }

            var aggregate: [RadioStation] = []
            for await results in group {
                aggregate.append(contentsOf: results.filter { stationBelongsToCountry($0, country: country) })
            }
            return aggregate
        }

        return uniqueById(merged)
    }

    private func fetchRegionalStations(for country: CountryPreset) async throws -> [RadioStation] {
        let terms = regionalSearchTerms(for: country)
        guard !terms.isEmpty else { return [] }

        let merged = await withTaskGroup(of: [RadioStation].self) { group in
            for term in terms {
                group.addTask { [service] in
                    do {
                        return try await service.searchStations(query: term, limit: 120)
                    } catch {
                        return []
                    }
                }
            }

            var aggregate: [RadioStation] = []
            for await results in group {
                aggregate.append(contentsOf: results.filter { stationBelongsToCountry($0, country: country) })
            }
            return aggregate
        }

        return uniqueById(merged)
    }

    private func applyStations(_ rawStations: [RadioStation], for country: CountryPreset) {
        let snapshot = buildSnapshot(from: rawStations, for: country)
        countryCache[country.id] = snapshot
        applySnapshot(snapshot)

        if snapshot.allStations.isEmpty {
            countryInfoMessage = "No stations available for \(country.displayName) right now."
        }
    }

    private func applySnapshot(_ snapshot: CountrySnapshot) {
        countryTopStations = snapshot.topStations
        countryAllStations = snapshot.allStations
        countryStationCount = snapshot.stationCount
        stationVariantsByPrimaryId = snapshot.variantsByPrimary
        player.setStationVariants(snapshot.variantsByPrimary)
    }

    private func buildSnapshot(from rawStations: [RadioStation], for country: CountryPreset) -> CountrySnapshot {
        let sanitized = sanitizeStations(rawStations)
        let consolidated = consolidateStationVariants(sanitized)
        let allStations = consolidated.primaryStations
        let topStations = curatedTopStations(from: allStations, for: country)
        return CountrySnapshot(
            topStations: topStations,
            allStations: allStations,
            stationCount: allStations.count,
            variantsByPrimary: consolidated.variantsByPrimary
        )
    }

    private func storeSnapshot(_ rawStations: [RadioStation], for country: CountryPreset) {
        let snapshot = buildSnapshot(from: rawStations, for: country)
        countryCache[country.id] = snapshot
    }

    private func recordRecentCountry(_ id: String) {
        recentCountryIDs.removeAll { $0 == id }
        recentCountryIDs.insert(id, at: 0)
        if recentCountryIDs.count > 6 {
            recentCountryIDs.removeLast(recentCountryIDs.count - 6)
        }
    }

    private func startBackgroundCaching() {
        backgroundCacheTask?.cancel()
        backgroundCacheTask = Task.detached(priority: .background) { [weak self] in
            guard let self else { return }
            await self.prewarmCache()
            await self.periodicCacheRefresh()
        }
    }

    private func prewarmCache() async {
        let seed = Array(CountryPreset.all.prefix(8))
        for country in seed {
            do {
                let stations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 500)
                await MainActor.run {
                    self.storeSnapshot(stations, for: country)
                }
                await prefetchLogos(for: Array(stations.prefix(30)))
            } catch {
                continue
            }
        }
    }

    private func periodicCacheRefresh() async {
        while !Task.isCancelled {
            try? await Task.sleep(nanoseconds: 1_800_000_000_000) // 30 minutes
            let ids = await MainActor.run { recentCountryIDs }
            let countries = CountryPreset.all.filter { ids.contains($0.id) }
            for country in countries {
                do {
                    let stations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 600)
                    await MainActor.run {
                        self.storeSnapshot(stations, for: country)
                    }
                    await prefetchLogos(for: Array(stations.prefix(20)))
                } catch {
                    continue
                }
            }
        }
    }

    private func prefetchLogos(for stations: [RadioStation]) async {
        for station in stations {
            _ = await StationLogoProvider.shared.logoData(for: station)
        }
    }

    private func isLoadActive(_ token: UUID, for country: CountryPreset) -> Bool {
        token == activeCountryLoadToken && selectedCountry.id == country.id
    }

    private func regionalSearchTerms(for country: CountryPreset) -> [String] {
        switch country.id {
        case "uk":
            return [
                "BBC Local Radio", "Heart UK", "Virgin Radio UK", "Capital UK",
                "Absolute Radio", "Kiss UK", "Smooth Radio", "Greatest Hits Radio",
                "BBC Radio Stoke", "Signal 1", "LBC", "talkSPORT"
            ]
        case "us":
            return ["iHeartRadio", "NPR", "Public Radio", "Classic Rock", "Top 40", "Hip Hop"]
        case "ca":
            return ["CBC", "Virgin Radio Canada", "CHUM", "Toronto Radio", "Vancouver Radio", "Montreal Radio"]
        case "au":
            return ["ABC Radio", "triple j", "Nova", "Sydney Radio", "Melbourne Radio", "Brisbane Radio"]
        case "nz":
            return ["Newstalk ZB", "ZM", "The Edge", "Auckland Radio", "Wellington Radio"]
        default:
            return Array(country.topBrands.prefix(6))
        }
    }

    private func uniqueById(_ stations: [RadioStation]) -> [RadioStation] {
        var seen = Set<String>()
        var output: [RadioStation] = []

        for station in stations where !seen.contains(station.stationuuid) {
            seen.insert(station.stationuuid)
            output.append(station)
        }

        return output
    }
}
