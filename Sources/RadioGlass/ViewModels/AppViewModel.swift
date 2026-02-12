import Combine
import Foundation

@MainActor
final class AppViewModel: ObservableObject {
    private struct CountrySnapshot {
        let topStations: [RadioStation]
        let allStations: [RadioStation]
        let stationCount: Int
        let variantsByPrimary: [String: [RadioStation]]
        let majorBrandScoreById: [String: Int]
    }

    private struct SnapshotBuilder {
        static func buildSnapshot(from rawStations: [RadioStation], for country: CountryPreset) -> CountrySnapshot {
            let sanitized = sanitizeStations(rawStations)
            let consolidated = consolidateStationVariants(sanitized)
            let allStations = consolidated.primaryStations
            let topStations = curatedTopStations(from: allStations, for: country)
            let brandScoreById = majorBrandScoreById(for: allStations, brands: country.topBrands)
            return CountrySnapshot(
                topStations: topStations,
                allStations: allStations,
                stationCount: allStations.count,
                variantsByPrimary: consolidated.variantsByPrimary,
                majorBrandScoreById: brandScoreById
            )
        }

        private static func sanitizeStations(_ stations: [RadioStation]) -> [RadioStation] {
            uniqueById(stations)
                .filter { station in
                    let hasStream = !(station.urlResolved.isEmpty) || !(station.url?.isEmpty ?? true)
                    let lower = station.name.lowercased()
                    return hasStream && !lower.contains("scanner") && !lower.contains("test") && !lower.contains("localhost")
                }
        }

        private static func curatedTopStations(from stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
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

        private static func consolidateStationVariants(_ stations: [RadioStation]) -> (primaryStations: [RadioStation], variantsByPrimary: [String: [RadioStation]]) {
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

        private static func popularity(_ station: RadioStation) -> Int {
            (station.votes ?? 0) + ((station.clickcount ?? 0) / 3)
        }

        private static func uniqueById(_ stations: [RadioStation]) -> [RadioStation] {
            var seen = Set<String>()
            var output: [RadioStation] = []

            for station in stations where !seen.contains(station.stationuuid) {
                seen.insert(station.stationuuid)
                output.append(station)
            }

            return output
        }

        private static func normalizeStationName(_ raw: String) -> String {
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

        private static func canonicalStationName(_ raw: String) -> String {
            let simplified = raw
                .lowercased()
                .replacingOccurrences(of: #"\([^)]*\)"#, with: " ", options: .regularExpression)
                .replacingOccurrences(of: #"\[[^\]]*\]"#, with: " ", options: .regularExpression)
                .replacingOccurrences(of: #"\b\d{2,3}\s?(k|kbps)\b"#, with: " ", options: .regularExpression)
                .replacingOccurrences(of: #"\b(aac\+?|mp3|ogg|stream|live|hq|lq)\b"#, with: " ", options: .regularExpression)

            return normalizeStationName(simplified)
        }

        private static func isLikelyVariantName(_ name: String) -> Bool {
            let lowered = name.lowercased()
            return lowered.range(of: #"\b\d{2,3}\s?(k|kbps)\b"#, options: .regularExpression) != nil
                || lowered.contains("aac")
                || lowered.contains("mp3")
                || lowered.contains("stream")
        }

        private static func brandMatches(stationName: String, brand: String) -> Bool {
            let station = normalizeStationName(stationName)
            let target = normalizeStationName(brand)
            guard !station.isEmpty, !target.isEmpty else { return false }

            if station.contains(target) { return true }

            let tokens = target.split(separator: " ").map(String.init)
            if tokens.count > 1 {
                return tokens.allSatisfy { station.contains($0) }
            }
            return false
        }

        private static func majorBrandScoreById(for stations: [RadioStation], brands: [String]) -> [String: Int] {
            guard !stations.isEmpty, !brands.isEmpty else { return [:] }
            let normalizedBrands = brands.enumerated().map { (idx, brand) in
                (idx, normalizeStationName(brand))
            }

            var scores: [String: Int] = [:]
            scores.reserveCapacity(stations.count)

            for station in stations {
                let normalized = normalizeStationName(station.name)
                guard !normalized.isEmpty else { continue }

                var bestScore = 0
                for (idx, brand) in normalizedBrands {
                    guard !brand.isEmpty else { continue }
                    if normalized.contains(brand) {
                        let score = 1_000_000 - (idx * 10_000)
                        if score > bestScore { bestScore = score }
                        continue
                    }

                    let tokens = brand.split(separator: " ").map(String.init)
                    if tokens.count > 1 && tokens.allSatisfy({ normalized.contains($0) }) {
                        let score = 1_000_000 - (idx * 10_000)
                        if score > bestScore { bestScore = score }
                    }
                }

                if bestScore > 0 {
                    scores[station.id] = bestScore
                }
            }
            return scores
        }
    }

    enum StationSort: String, CaseIterable, Identifiable {
        case alphabetical = "A–Z"
        case popularity = "Most Popular"
        case trending = "Most Listened"
        case bitrateHigh = "Highest Bitrate"
        case bitrateLow = "Lowest Bitrate"
        case favoritesFirst = "Favorites First"
        case networksFirst = "Networks First"

        var id: String { rawValue }
    }
    
    enum StationFilter: String, CaseIterable, Identifiable {
        case all = "All Stations"
        case presetsOnly = "Favorites"
        case popular = "Popular Picks"
        case reliable = "Stable Streams"
        case majorBrands = "Big Networks"
        case withGenres = "Genre Tagged"

        var id: String { rawValue }

        var subtitle: String {
            switch self {
            case .all: return "Everything in the current country"
            case .presetsOnly: return "Just the stations you’ve starred"
            case .popular: return "Top voted and most clicked"
            case .reliable: return "Streams with steady play history"
            case .majorBrands: return "Large national and global broadcasters"
            case .withGenres: return "Stations with reliable genre tags"
            }
        }
    }

    struct BrandCluster: Identifiable {
        let id: String
        let name: String
        let stations: [RadioStation]
    }

    @Published var presets: [RadioStation] = []
    @Published var selectedCountry: CountryPreset

    @Published var countryTopStations: [RadioStation] = []
    @Published var countryAllStations: [RadioStation] = []
    @Published var selectedSort: StationSort = .alphabetical
    @Published var selectedFilter: StationFilter = .all
    @Published var selectedGenre: String = "All"
    @Published private(set) var countryStationCount = 0

    @Published var discoverStations: [RadioStation] = []
    @Published var quickSearchText = ""
    @Published var quickSearchResults: [RadioStation] = []
    @Published var searchPageQuery = ""
    @Published var searchPageResults: [RadioStation] = []
    @Published var isLoading = false
    @Published var errorMessage: String?
    @Published var countryInfoMessage: String?
    @Published var nowPlayingStation: RadioStation?
    @Published var isInitialLoading = false
    @Published var initialLoadingProgress: Double = 0
    @Published var initialLoadingMessage: String = "Preparing your library..."
    @Published var isBackgroundRefreshing = false
    @Published var backgroundRefreshProgress: Double = 0
    @Published var backgroundRefreshMessage: String = "Refreshing stations..."

    let player = AudioPlayerManager()

    private let service: RadioServing
    private let store = FavoritesStore()
    private var stationVariantsByPrimaryId: [String: [RadioStation]] = [:]
    private var majorBrandScoreById: [String: Int] = [:]
    private var countryCache: [String: CountrySnapshot] = [:]
    private var activeCountryLoadToken = UUID()
    private var quickSearchToken = UUID()
    private var searchPageToken = UUID()
    private var quickSearchTask: Task<Void, Never>?
    private var searchPageTask: Task<Void, Never>?
    private var backgroundCacheTask: Task<Void, Never>?
    private var recentCountryIDs: [String] = []
    private let initialCacheKey = "GlassRadio.InitialCacheComplete"
    private let selectedCountryKey = "GlassRadio.SelectedCountryID"
    private let failedCacheKey = "GlassRadio.FailedInitialCacheCounts"
    private let maxInitialCacheRetries = 5
    private var failedInitialCacheCounts: [String: Int] = [:]

    init(service: RadioServing = RadioBrowserService()) {
        self.service = service
        AppAssetStore.ensureDirectories()
        AppAssetStore.copyBundledFlagsIfNeeded()
        self.presets = store.load()
        self.failedInitialCacheCounts = Self.loadFailedCacheCounts(key: failedCacheKey)
        let savedID = UserDefaults.standard.string(forKey: selectedCountryKey)
        let fallback = CountryPreset.all.first ?? CountryPreset(id: "uk", displayName: "United Kingdom", apiName: "United Kingdom", topBrands: [])
        if let savedID, let savedCountry = CountryPreset.all.first(where: { $0.id == savedID }) {
            self.selectedCountry = savedCountry
        } else {
            self.selectedCountry = fallback
        }
        if !UserDefaults.standard.bool(forKey: initialCacheKey) {
            Task {
                await performInitialCache()
                await loadCountry(selectedCountry)
                await loadDiscover()
                startBackgroundRefresh()
            }
        } else {
            Task {
                await loadCountry(selectedCountry)
                await loadDiscover()
            }
            startBackgroundRefresh()
        }
    }

    var filteredCountryStations: [RadioStation] {
        var base = countryAllStations

        switch selectedFilter {
        case .all:
            break
        case .presetsOnly:
            base = base.filter { presets.contains($0) }
        case .popular:
            base = Array(base.sorted { weightedPopularity($0) > weightedPopularity($1) }.prefix(400))
        case .reliable:
            base = base.filter { reliabilityScore($0) >= 50 }
        case .majorBrands:
            base = base.filter { matchesMajorBrand($0) }
        case .withGenres:
            base = base.filter { !$0.genres.isEmpty }
        }

        switch selectedSort {
        case .alphabetical:
            return base.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        case .popularity:
            return base.sorted { weightedPopularity($0) > weightedPopularity($1) }
        case .trending:
            return base.sorted { clickCount($0) > clickCount($1) }
        case .bitrateHigh:
            return base.sorted { inferredBitrate(for: $0) > inferredBitrate(for: $1) }
        case .bitrateLow:
            return base.sorted { inferredBitrate(for: $0) < inferredBitrate(for: $1) }
        case .favoritesFirst:
            return base.sorted {
                let lhsFav = presets.contains($0) ? 1 : 0
                let rhsFav = presets.contains($1) ? 1 : 0
                if lhsFav != rhsFav { return lhsFav > rhsFav }
                return weightedPopularity($0) > weightedPopularity($1)
            }
        case .networksFirst:
            return base.sorted {
                let lhsBrand = majorBrandPriority($0)
                let rhsBrand = majorBrandPriority($1)
                if lhsBrand != rhsBrand { return lhsBrand > rhsBrand }
                return weightedPopularity($0) > weightedPopularity($1)
            }
        }
    }
    
    var genreButtons: [String] {
        let baseGenres = [
            "All", "80s", "90s", "Adult Contemporary", "Alternative", "Ambient", "Blues", "Classical",
            "Classic Rock", "Country", "Dance", "Disco", "Drum & Bass", "EDM", "Electronic", "Folk",
            "Funk", "Gospel", "Hip-Hop", "House", "Indie", "Jazz", "Latin", "Lounge", "Metal",
            "News", "Oldies", "Pop", "R&B", "Reggae", "Rock", "Soul", "Sports", "Talk", "Top 40", "Urban"
        ]
        let expanded = baseGenres + countrySpecificGenres(for: selectedCountry)
        var ordered: [String] = []
        for genre in expanded where !ordered.contains(genre) {
            ordered.append(genre)
        }
        let filtered = ordered.filter { genre in
            if genre == "All" { return true }
            return countryAllStations.contains { station in
                station.genres.contains { normalizeGenre($0).localizedCaseInsensitiveContains(genre) }
            }
        }
        let rest = filtered.filter { $0 != "All" }
            .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        return ["All"] + rest
    }

    func setSort(_ sort: StationSort) { selectedSort = sort }
    func setFilter(_ filter: StationFilter) { selectedFilter = filter }
    func setGenre(_ genre: String) { selectedGenre = genre }

    private func inferredBitrate(for station: RadioStation) -> Int {
        let patterns = ["(\\d{2,3})\\s*k", "(\\d{2,3})\\s*kbps", "(\\d{2,3})\\s*kbit"]
        for pattern in patterns {
            if let match = station.name.range(of: pattern, options: [.regularExpression, .caseInsensitive]) {
                let number = station.name[match].replacingOccurrences(of: "[^0-9]", with: "", options: .regularExpression)
                if let value = Int(number) { return value }
            }
        }
        return 128
    }

    private func reliabilityScore(_ station: RadioStation) -> Int {
        let clicks = (station.clickcount ?? 0)
        let votes = (station.votes ?? 0)
        let hasURL = !(station.urlResolved.isEmpty) || !(station.url?.isEmpty ?? true)
        let base = votes + clicks
        return base + (hasURL ? 20 : 0)
    }

    private func clickCount(_ station: RadioStation) -> Int {
        station.clickcount ?? 0
    }

    private func countrySpecificGenres(for country: CountryPreset) -> [String] {
        switch country.id {
        case "uk":
            return ["Grime", "Garage", "Drum & Bass"]
        case "us":
            return ["Americana", "Bluegrass", "Gospel", "Country"]
        case "es":
            return ["Urban", "Latin", "Flamenco"]
        case "fr":
            return ["Chanson"]
        case "de":
            return ["Schlager"]
        case "it":
            return ["Italo Disco"]
        case "jp":
            return ["J-Pop", "City Pop"]
        case "kr":
            return ["K-Pop"]
        case "in":
            return ["Bollywood"]
        case "br":
            return ["Sertanejo", "Samba", "Bossa Nova"]
        case "mx":
            return ["Regional", "Banda", "Norteño"]
        case "ar":
            return ["Tango"]
        case "ng":
            return ["Afrobeats"]
        case "za":
            return ["Amapiano"]
        case "ie":
            return ["Celtic"]
        case "au":
            return ["Aussie Rock"]
        default:
            return []
        }
    }

    func loadCountry(_ country: CountryPreset) async {
        let loadToken = UUID()
        activeCountryLoadToken = loadToken
        recordRecentCountry(country.id)
        selectedCountry = country
        UserDefaults.standard.set(country.id, forKey: selectedCountryKey)
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
            await applyStations(quickStations, for: country)

            async let expandedStationsTask = service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 2400)
            async let majorBrandTask = fetchMajorBrandStations(for: country)
            async let regionalTask = fetchRegionalStations(for: country)

            let expandedStations = try await expandedStationsTask
            let majorBrandStations = try await majorBrandTask
            let regionalStations = try await regionalTask
            guard isLoadActive(loadToken, for: country) else { return }

            await applyStations(quickStations + expandedStations + majorBrandStations + regionalStations, for: country)
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

    func performQuickSearch(for text: String? = nil, limit: Int = 80) async {
        let trimmed = (text ?? quickSearchText).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            quickSearchResults = []
            return
        }

        let token = UUID()
        quickSearchToken = token

        do {
            let results = try await service.searchStations(query: trimmed, limit: limit)
            guard quickSearchToken == token else { return }
            quickSearchResults = results
        } catch {
            guard quickSearchToken == token else { return }
            errorMessage = "Search failed."
            quickSearchResults = []
        }
    }

    func scheduleQuickSearch() {
        quickSearchTask?.cancel()
        let current = quickSearchText
        quickSearchTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 260_000_000) // ~0.26s debounce
            await self?.performQuickSearch(for: current, limit: 60)
        }
    }

    func performSearchPageQuery(for text: String? = nil, limit: Int = 160) async {
        let trimmed = (text ?? searchPageQuery).trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            searchPageResults = []
            return
        }

        let token = UUID()
        searchPageToken = token

        do {
            let results = try await service.searchStations(query: trimmed, limit: limit)
            guard searchPageToken == token else { return }
            searchPageResults = results
        } catch {
            guard searchPageToken == token else { return }
            errorMessage = "Search failed."
            searchPageResults = []
        }
    }

    func scheduleSearchPageQuery() {
        searchPageTask?.cancel()
        let current = searchPageQuery
        searchPageTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 320_000_000) // ~0.32s debounce
            await self?.performSearchPageQuery(for: current)
        }
    }

    func commitQuickSearch() async {
        let trimmed = quickSearchText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            quickSearchResults = []
            return
        }
        await performQuickSearch(for: trimmed)
    }

    func togglePreset(_ station: RadioStation) {
        if let idx = presets.firstIndex(of: station) {
            presets.remove(at: idx)
        } else {
            presets.append(station)
        }
        store.save(presets)
    }

    func movePresets(from offsets: IndexSet, to destination: Int) {
        presets.move(fromOffsets: offsets, toOffset: destination)
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
        majorBrandScoreById[station.id] ?? 0
    }
    
    private func matchesMajorBrand(_ station: RadioStation) -> Bool {
        (majorBrandScoreById[station.id] ?? 0) > 0
    }

    var genreFilteredStations: [RadioStation] {
        let base = countryAllStations.filter { station in
            if selectedGenre == "All" { return true }
            return station.genres.contains { normalizeGenre($0).localizedCaseInsensitiveContains(selectedGenre) }
        }

        return base.sorted { popularity($0) > popularity($1) }
    }

    func brandClusters(for query: String) -> [BrandCluster] {
        let trimmed = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.count >= 3 else { return [] }
        let target = normalizeStationName(trimmed)
        guard !target.isEmpty else { return [] }

        let base = searchPageResults.isEmpty ? countryAllStations : searchPageResults
        let filtered = base.filter { normalizeStationName($0.name).contains(target) }
        guard !filtered.isEmpty else { return [] }

        var grouped: [String: [RadioStation]] = [:]
        for station in filtered {
            let key = canonicalStationName(station.name)
            grouped[key, default: []].append(station)
        }

        let clusters = grouped.values.compactMap { stations -> BrandCluster? in
            let sortedStations = stations.sorted { popularity($0) > popularity($1) }
            guard let primary = sortedStations.first else { return nil }
            return BrandCluster(id: primary.id, name: primary.name, stations: sortedStations)
        }

        return clusters
            .sorted { popularity($0.stations[0]) > popularity($1.stations[0]) }
            .prefix(6)
            .map { $0 }
    }

    func stationsForFilter(_ filter: StationFilter, limit: Int) -> [RadioStation] {
        var base = countryAllStations
        switch filter {
        case .all:
            break
        case .presetsOnly:
            base = base.filter { presets.contains($0) }
        case .popular:
            base = Array(base.sorted { weightedPopularity($0) > weightedPopularity($1) }.prefix(400))
        case .reliable:
            base = base.filter { reliabilityScore($0) >= 50 }
        case .majorBrands:
            base = base.filter { matchesMajorBrand($0) }
        case .withGenres:
            base = base.filter { !$0.genres.isEmpty }
        }
        let sorted = base.sorted { popularity($0) > popularity($1) }
        return Array(sorted.prefix(limit))
    }

    func topStationsByCountryPreview(maxCountries: Int = 12, perCountry: Int = 1) -> [(CountryPreset, [RadioStation])] {
        var result: [(CountryPreset, [RadioStation])] = []
        for country in CountryPreset.all {
            guard let snapshot = countryCache[country.id], !snapshot.topStations.isEmpty else { continue }
            result.append((country, Array(snapshot.topStations.prefix(perCountry))))
            if result.count >= maxCountries { break }
        }
        return result
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

    private func applyStations(_ rawStations: [RadioStation], for country: CountryPreset) async {
        let snapshot = await buildSnapshot(from: rawStations, for: country)
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
        majorBrandScoreById = snapshot.majorBrandScoreById
        player.setStationVariants(snapshot.variantsByPrimary)
    }

    private func buildSnapshot(from rawStations: [RadioStation], for country: CountryPreset) async -> CountrySnapshot {
        await Task.detached(priority: .userInitiated) {
            SnapshotBuilder.buildSnapshot(from: rawStations, for: country)
        }.value
    }

    private func storeSnapshot(_ rawStations: [RadioStation], for country: CountryPreset) async {
        let snapshot = await buildSnapshot(from: rawStations, for: country)
        countryCache[country.id] = snapshot
    }

    private func recordRecentCountry(_ id: String) {
        recentCountryIDs.removeAll { $0 == id }
        recentCountryIDs.insert(id, at: 0)
        if recentCountryIDs.count > 6 {
            recentCountryIDs.removeLast(recentCountryIDs.count - 6)
        }
    }

    private func startBackgroundRefresh() {
        backgroundCacheTask?.cancel()
        backgroundCacheTask = Task.detached(priority: .background) { [weak self] in
            guard let self else { return }
            await self.quickRefreshAtLaunch()
            await self.periodicCacheRefresh()
        }
    }

    private func performInitialCache() async {
        await MainActor.run {
            isInitialLoading = true
            initialLoadingProgress = 0
            initialLoadingMessage = "Preparing your library..."
        }

        let countries = CountryPreset.all
        let total = max(1, countries.count)

        for (index, country) in countries.enumerated() {
            if let failed = failedInitialCacheCounts[country.id], failed >= maxInitialCacheRetries {
                await MainActor.run {
                    initialLoadingMessage = "Skipping \(country.displayName)…"
                    initialLoadingProgress = Double(index) / Double(total)
                }
                continue
            }
            await MainActor.run {
                initialLoadingMessage = "Caching \(country.displayName)..."
                initialLoadingProgress = Double(index) / Double(total)
            }

            do {
                let merged = try await withTimeout(seconds: 28) { [service, self] in
                    let stations = try await service.fetchCountryStations(
                        country: country.apiName,
                        countryCode: country.countryCode,
                        limit: 1800
                    )
                    async let major = self.fetchMajorBrandStations(for: country)
                    async let regional = self.fetchRegionalStations(for: country)
                    return stations + (try await major) + (try await regional)
                }
                await storeSnapshot(merged, for: country)
                Task.detached(priority: .background) { [self] in
                    await self.prefetchLogos(for: Array(merged.prefix(20)))
                }
                await updateFailedCacheCount(countryID: country.id, success: true)
            } catch {
                await MainActor.run {
                    initialLoadingMessage = "Skipping \(country.displayName)…"
                }
                await updateFailedCacheCount(countryID: country.id, success: false)
            }
            await Task.yield()
        }

        await MainActor.run {
            initialLoadingProgress = 1
            initialLoadingMessage = "Finishing up..."
        }

        UserDefaults.standard.set(true, forKey: initialCacheKey)

        await MainActor.run {
            isInitialLoading = false
        }
    }

    private func prewarmCache() async {
        let seed = Array(CountryPreset.all.prefix(8))
        for country in seed {
            do {
                let stations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 500)
                await storeSnapshot(stations, for: country)
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
            await MainActor.run {
                isBackgroundRefreshing = !countries.isEmpty
                backgroundRefreshProgress = 0
                backgroundRefreshMessage = "Refreshing stations..."
            }

            let total = max(1, countries.count)
            for country in countries {
                do {
                    let stations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 600)
                    await storeSnapshot(stations, for: country)
                    await prefetchLogos(for: Array(stations.prefix(20)))
                } catch {
                    continue
                }
                await MainActor.run {
                    backgroundRefreshProgress += 1 / Double(total)
                }
            }

            await MainActor.run {
                isBackgroundRefreshing = false
            }
        }
    }

    private func quickRefreshAtLaunch() async {
        let seed = await MainActor.run { selectedCountry }
        let localeRegion = Locale.current.region?.identifier.lowercased()
        let localeCountry = CountryPreset.all.first { preset in
            preset.id == localeRegion || preset.countryCode.lowercased() == localeRegion
        }
        let retryCandidates = failedInitialCacheCounts
            .filter { $0.value < maxInitialCacheRetries }
            .compactMap { id, _ in CountryPreset.all.first { $0.id == id } }
        var countries = [seed]
        if let localeCountry {
            countries.append(localeCountry)
        }
        countries.append(contentsOf: CountryPreset.all.prefix(3))
        countries.append(contentsOf: retryCandidates)
        let unique = Array(Set(countries.map(\.id))).compactMap { id in
            CountryPreset.all.first { $0.id == id }
        }

        guard !unique.isEmpty else { return }
        await MainActor.run {
            isBackgroundRefreshing = true
            backgroundRefreshProgress = 0
            backgroundRefreshMessage = "Refreshing stations..."
        }

        let total = max(1, unique.count)
        for country in unique {
            do {
                let timeout = retryCandidates.contains(country) ? 60.0 : 28.0
                let stations = try await withTimeout(seconds: timeout) { [service] in
                    try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 600)
                }
                await storeSnapshot(stations, for: country)
                await prefetchLogos(for: Array(stations.prefix(16)))
                if retryCandidates.contains(country) {
                    await updateFailedCacheCount(countryID: country.id, success: true)
                }
            } catch {
                if retryCandidates.contains(country) {
                    await updateFailedCacheCount(countryID: country.id, success: false)
                }
                continue
            }
            await MainActor.run {
                backgroundRefreshProgress += 1 / Double(total)
            }
        }

        await MainActor.run {
            isBackgroundRefreshing = false
        }
    }

    private func withTimeout<T>(seconds: Double, operation: @escaping () async throws -> T) async throws -> T {
        try await withThrowingTaskGroup(of: T.self) { group in
            group.addTask {
                try await operation()
            }
            group.addTask {
                try await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                throw URLError(.timedOut)
            }

            let result = try await group.next()!
            group.cancelAll()
            return result
        }
    }

    private func updateFailedCacheCount(countryID: String, success: Bool) async {
        if success {
            failedInitialCacheCounts.removeValue(forKey: countryID)
        } else {
            let current = failedInitialCacheCounts[countryID] ?? 0
            failedInitialCacheCounts[countryID] = current + 1
        }
        Self.saveFailedCacheCounts(failedInitialCacheCounts, key: failedCacheKey)
    }

    private static func loadFailedCacheCounts(key: String) -> [String: Int] {
        guard let data = UserDefaults.standard.data(forKey: key),
              let decoded = try? JSONDecoder().decode([String: Int].self, from: data) else {
            return [:]
        }
        return decoded
    }

    private static func saveFailedCacheCounts(_ counts: [String: Int], key: String) {
        guard let data = try? JSONEncoder().encode(counts) else { return }
        UserDefaults.standard.set(data, forKey: key)
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
