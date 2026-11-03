import Combine
import Foundation

enum GlassRadioPreferenceKey {
    static let showReligiousContent = "GlassRadio.ShowReligiousContent"
    static let showSportsContent = "GlassRadio.ShowSportsContent"
    static let showNewsContent = "GlassRadio.ShowNewsContent"
    static let apiSmartPortEnabled = "GlassRadio.API.SmartPortEnabled"
    static let apiEnabled = "GlassRadio.API.Enabled"
    static let apiPreferredPort = "GlassRadio.API.PreferredPort"
    static let apiActivePort = "GlassRadio.API.ActivePort"
    static let apiKeys = "GlassRadio.API.Keys"
    static let lastPlayedStation = "GlassRadio.LastPlayedStation"
}

struct StationContentPreferences {
    var showReligiousContent: Bool
    var showSportsContent: Bool
    var showNewsContent: Bool

    static func load(from defaults: UserDefaults = .standard) -> StationContentPreferences {
        StationContentPreferences(
            showReligiousContent: defaults.object(forKey: GlassRadioPreferenceKey.showReligiousContent) as? Bool ?? true,
            showSportsContent: defaults.object(forKey: GlassRadioPreferenceKey.showSportsContent) as? Bool ?? true,
            showNewsContent: defaults.object(forKey: GlassRadioPreferenceKey.showNewsContent) as? Bool ?? true
        )
    }
}

@MainActor
final class AppViewModel: ObservableObject {
    private struct CountrySnapshot: Codable {
        let schemaVersion: Int
        let topStations: [RadioStation]
        let allStations: [RadioStation]
        let stationCount: Int
        let variantsByPrimary: [String: [RadioStation]]
        let majorBrandScoreById: [String: Int]
    }

    private struct SnapshotBuilder {
        static func buildSnapshot(from rawStations: [RadioStation], for country: CountryPreset) -> CountrySnapshot {
            let sanitized = sanitizeStations(rawStations, for: country)
            let consolidated = consolidateStationVariants(sanitized)
            let allStations = consolidated.primaryStations
            let topStations = curatedTopStations(from: sanitized, for: country)
            let brandScoreById = majorBrandScoreById(for: allStations, brands: country.topBrands)
            return CountrySnapshot(
                schemaVersion: AppViewModel.countrySnapshotVersion,
                topStations: topStations,
                allStations: allStations,
                stationCount: allStations.count,
                variantsByPrimary: consolidated.variantsByPrimary,
                majorBrandScoreById: brandScoreById
            )
        }

        private static func sanitizeStations(_ stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
            let arabicContextIds: Set<String> = ["eg", "sa", "ae"]
            let islamicTerms = ["quran", "quraan", "koran", "islamic", "muslim",
                                "salafi", "dawah", "nooran", "deen fm"]
            let isArabicContext = arabicContextIds.contains(country.id)

            return uniqueById(stations)
                .filter { station in
                    let hasStream = !(station.urlResolved.isEmpty) || !(station.url?.isEmpty ?? true)
                    let lower = station.name.lowercased()
                    guard hasStream,
                          !lower.contains("scanner"),
                          !lower.contains("test"),
                          !lower.contains("localhost") else { return false }

                    if !isArabicContext {
                        let lang = station.language?.lowercased() ?? ""
                        let tags = station.tags?.lowercased() ?? ""
                        if lang.contains("arabic") { return false }
                        if islamicTerms.contains(where: { tags.contains($0) || lower.contains($0) }) { return false }
                    }
                    return true
                }
        }

        private static func curatedTopStations(from stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
            let curated = stations
                .filter { isEligibleForTopStations($0, in: country) }
                .compactMap { station -> (Int, RadioStation)? in
                    guard let idx = country.topBrands.firstIndex(where: { brandMatches(stationName: station.name, brand: $0) }) else {
                        return nil
                    }
                    return (idx, station)
                }
                .sorted { lhs, rhs in
                    if lhs.0 != rhs.0 { return lhs.0 < rhs.0 }
                    let lhsBrand = country.topBrands[lhs.0]
                    let rhsBrand = country.topBrands[rhs.0]
                    let lhsQuality = brandMatchQuality(stationName: lhs.1.name, brand: lhsBrand)
                    let rhsQuality = brandMatchQuality(stationName: rhs.1.name, brand: rhsBrand)
                    if lhsQuality != rhsQuality { return lhsQuality > rhsQuality }
                    let lhsArtwork = artworkQuality(lhs.1)
                    let rhsArtwork = artworkQuality(rhs.1)
                    if lhsArtwork != rhsArtwork { return lhsArtwork > rhsArtwork }
                    return popularity(lhs.1) > popularity(rhs.1)
                }
                .map(\.1)

            return uniqueByTopStationSlot(curated, for: country)
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

                    let lhsArtwork = artworkQuality(lhs)
                    let rhsArtwork = artworkQuality(rhs)
                    if lhsArtwork != rhsArtwork { return lhsArtwork > rhsArtwork }

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

        private static func isReligiousContent(_ station: RadioStation) -> Bool {
            let haystack = normalizeStationName([
                station.name,
                station.tags ?? "",
                station.language ?? ""
            ].joined(separator: " "))

            return religiousTopStationTerms.contains { term in
                haystack.contains(term)
            }
        }

        private static func isEligibleForTopStations(_ station: RadioStation, in country: CountryPreset) -> Bool {
            guard !isReligiousContent(station) else { return false }
            if country.id == "uk",
               (isUKLocalBBCStation(station) || isUKAudienceSpecificTopStation(station)) {
                return false
            }
            return true
        }

        private static func isUKAudienceSpecificTopStation(_ station: RadioStation) -> Bool {
            let name = normalizeStationName(station.name)
            return name.contains("bbc asian network")
        }

        private static func isUKLocalBBCStation(_ station: RadioStation) -> Bool {
            let name = normalizeStationName(station.name)
            guard name.hasPrefix("bbc radio ") else { return false }
            let nationalTerms = [
                "bbc radio 1", "bbc radio 1xtra", "bbc radio 2", "bbc radio 3",
                "bbc radio 4", "bbc radio 5 live", "bbc radio 6 music",
                "bbc asian network", "bbc world service"
            ]
            return !nationalTerms.contains { name.contains($0) }
        }

        private static func hasArtwork(_ station: RadioStation) -> Bool {
            artworkQuality(station) > 0
        }

        private static func artworkQuality(_ station: RadioStation) -> Int {
            guard let favicon = station.favicon?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased(),
                  !favicon.isEmpty else { return 0 }
            if favicon.contains(".png") || favicon.contains(".jpg") || favicon.contains(".jpeg") || favicon.contains(".webp") {
                return 3
            }
            if favicon.contains(".svg") { return 2 }
            if favicon.contains(".ico") { return 1 }
            return 1
        }

        private static let religiousTopStationTerms = [
            "quran", "quraan", "koran", "islamic", "muslim", "salafi", "dawah",
            "deen fm", "christian", "christ", "jesus", "church", "bible",
            "catholic", "evangelical", "worship", "prayer", "ministry",
            "ministries", "sermon", "praise", "hindu", "bhajan", "buddhist",
            "dharma", "torah", "jewish", "synagogue"
        ]

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
                .replacingOccurrences(of: #"\b\d{2,3}\s?(kb?|kbps)\b"#, with: " ", options: .regularExpression)
                .replacingOccurrences(of: #"\b(aac\+?|mp3|ogg|stream|live|hq|lq)\b"#, with: " ", options: .regularExpression)

            return normalizeStationName(simplified)
        }

        private static func isLikelyVariantName(_ name: String) -> Bool {
            let lowered = name.lowercased()
            return lowered.range(of: #"\b\d{2,3}\s?(kb?|kbps)\b"#, options: .regularExpression) != nil
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

        private static func brandMatchQuality(stationName: String, brand: String) -> Int {
            let raw = stationName.lowercased()
            let station = normalizeStationName(stationName)
            let target = normalizeStationName(brand)
            guard !station.isEmpty, !target.isEmpty else { return 0 }
            if station.hasPrefix(target + " "), raw.contains(":") || raw.contains(" - ") { return 5 }
            if station == target { return 4 }
            if station.hasPrefix(target + " ") { return 3 }
            if station.contains(target) { return 2 }
            return brandMatches(stationName: stationName, brand: brand) ? 1 : 0
        }

        private static func topStationIdentity(_ station: RadioStation, for country: CountryPreset) -> String {
            if let idx = country.topBrands.firstIndex(where: { brandMatches(stationName: station.name, brand: $0) }) {
                return "brand:\(idx)"
            }
            return "station:\(canonicalStationName(station.name))"
        }

        private static func uniqueByTopStationSlot(_ stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
            var seen = Set<String>()
            var output: [RadioStation] = []

            for station in stations {
                let key = topStationIdentity(station, for: country)
                guard !key.isEmpty, !seen.contains(key) else { continue }
                seen.insert(key)
                output.append(station)
            }

            return output
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
    @Published var countryAllStations: [RadioStation] = [] {
        didSet { rebuildContentFilterCache(); scheduleFilterRebuild() }
    }
    @Published private(set) var topStationPresentation: [RadioStation] = []
    @Published var selectedSort: StationSort = .alphabetical {
        didSet { scheduleFilterRebuild() }
    }
    @Published var selectedFilter: StationFilter = .all {
        didSet { scheduleFilterRebuild() }
    }
    @Published var selectedGenre: String = "All" {
        didSet { scheduleFilterRebuild() }
    }
    @Published private(set) var filteredCountryStations: [RadioStation] = []
    @Published private(set) var genreFilteredStations: [RadioStation] = []
    @Published private(set) var genreButtons: [String] = ["All"]
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
    @Published var isMiniGamePresented = false
    @Published var isBackgroundRefreshing = false
    @Published var backgroundRefreshProgress: Double = 0
    @Published var backgroundRefreshMessage: String = "Refreshing stations..."
    @Published var isOnboarding: Bool = false
    @Published private(set) var fullPullInProgress: Set<String> = []
    @Published private var contentPreferences = StationContentPreferences.load() {
        didSet { rebuildContentFilterCache(); scheduleFilterRebuild() }
    }

    let player = AudioPlayerManager()
    let apiServer = LocalAPIServer()

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
    private var filterRebuildTask: Task<Void, Never>?
    private var contentAllowedSet: Set<String> = []
    private var cancellables = Set<AnyCancellable>()
    private var recentCountryIDs: [String] = []
    private let selectedCountryKey = "GlassRadio.SelectedCountryID"
    private let initialCacheKey = "GlassRadio.InitialCacheComplete"
    private let failedCacheKey = "GlassRadio.FailedInitialCacheCounts"
    private let maxInitialCacheRetries = 5
    private static let countrySnapshotVersion = 3
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
            self.isOnboarding = false
        } else {
            // First boot — no country has been chosen. Auto-detect locale as the suggested pick.
            let localeRegion = Locale.current.region?.identifier.lowercased() ?? ""
            let localeCountry = CountryPreset.all.first {
                $0.id == localeRegion || $0.countryCode.lowercased() == localeRegion
            }
            self.selectedCountry = localeCountry ?? CountryPreset.all.first { $0.id == "uk" } ?? fallback
            self.isOnboarding = true
        }
        restoreLastPlayedStation()
        NotificationCenter.default.publisher(for: UserDefaults.didChangeNotification)
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                guard let self else { return }
                self.reloadContentPreferences()
                self.apiServer.applySettingsIfNeeded()
            }
            .store(in: &cancellables)
        apiServer.start(viewModel: self)
        guard !isOnboarding else { return }
        let needsInitialCache = !UserDefaults.standard.bool(forKey: initialCacheKey)
            || !AppAssetStore.hasStationSnapshots()
            || !AppAssetStore.hasStationSnapshot(countryID: selectedCountry.id)
        if needsInitialCache {
            Task {
                await cacheCountryForLaunch(selectedCountry)
                if AppAssetStore.hasStationSnapshot(countryID: selectedCountry.id) {
                    UserDefaults.standard.set(true, forKey: initialCacheKey)
                }
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

    // MARK: - Async filter pipeline

    private func rebuildContentFilterCache() {
        let prefs = contentPreferences
        let religious = religiousTopStationTerms
        let sports = sportsContentTerms
        let news = newsContentTerms
        contentAllowedSet = Set(countryAllStations.compactMap { station -> String? in
            let hay = (station.name + " " + (station.tags ?? "")).lowercased()
            if !prefs.showReligiousContent, religious.contains(where: { hay.contains($0) }) { return nil }
            if !prefs.showSportsContent,   sports.contains(where:    { hay.contains($0) }) { return nil }
            if !prefs.showNewsContent,     news.contains(where:      { hay.contains($0) }) { return nil }
            return station.stationuuid
        })
    }

    private func scheduleFilterRebuild() {
        filterRebuildTask?.cancel()
        filterRebuildTask = Task { @MainActor in
            // 16ms debounce — batches rapid changes (e.g., slider drag)
            try? await Task.sleep(nanoseconds: 16_000_000)
            guard !Task.isCancelled else { return }

            // Capture all inputs on main actor before going off-thread
            let all            = countryAllStations
            let allowed        = contentAllowedSet
            let filter         = selectedFilter
            let sort           = selectedSort
            let genre          = selectedGenre
            let presetsSet     = Set(presets.map(\.stationuuid))
            let brands         = majorBrandScoreById
            let country        = selectedCountry
            let countryGenres  = countrySpecificGenres(for: country)

            let (newFiltered, newGenreFiltered, newButtons) = await Task.detached(priority: .userInitiated) {
                // --- Filter ---
                var base = all.filter { allowed.contains($0.stationuuid) }

                let score: (RadioStation) -> Int = { s in
                    (s.votes ?? 0) + ((s.clickcount ?? 0) / 3) + (brands[s.id] ?? 0)
                }

                switch filter {
                case .all:       break
                case .presetsOnly: base = base.filter { presetsSet.contains($0.stationuuid) }
                case .popular:
                    base = base.sorted { score($0) > score($1) }
                    if base.count > 400 { base = Array(base.prefix(400)) }
                case .reliable:
                    base = base.filter { (($0.votes ?? 0) + ($0.clickcount ?? 0)) >= 50 }
                case .majorBrands:
                    base = base.filter { (brands[$0.id] ?? 0) > 0 }
                case .withGenres:
                    base = base.filter { !($0.tags?.isEmpty ?? true) }
                }

                // --- Sort ---
                let sorted: [RadioStation]
                switch sort {
                case .alphabetical:
                    sorted = base.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
                case .popularity:
                    sorted = base.sorted { score($0) > score($1) }
                case .trending:
                    sorted = base.sorted { ($0.clickcount ?? 0) > ($1.clickcount ?? 0) }
                case .bitrateHigh:
                    sorted = base.sorted { Self.quickBitrate($0) > Self.quickBitrate($1) }
                case .bitrateLow:
                    sorted = base.sorted { Self.quickBitrate($0) < Self.quickBitrate($1) }
                case .favoritesFirst:
                    sorted = base.sorted {
                        let lf = presetsSet.contains($0.stationuuid) ? 1 : 0
                        let rf = presetsSet.contains($1.stationuuid) ? 1 : 0
                        if lf != rf { return lf > rf }
                        return score($0) > score($1)
                    }
                case .networksFirst:
                    sorted = base.sorted {
                        let lb = brands[$0.id] ?? 0
                        let rb = brands[$1.id] ?? 0
                        if lb != rb { return lb > rb }
                        return score($0) > score($1)
                    }
                }

                // --- Genre filter ---
                let genreFiltered: [RadioStation]
                if genre == "All" {
                    genreFiltered = sorted
                } else {
                    let target = genre.lowercased()
                    genreFiltered = sorted.filter { station in
                        guard let tags = station.tags else { return false }
                        return tags
                            .split(separator: ",")
                            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
                            .contains { $0.replacingOccurrences(of: "-", with: " ")
                                          .replacingOccurrences(of: "_", with: " ")
                                          .contains(target) }
                    }
                }

                // --- Genre buttons ---
                let base40 = [
                    "All", "80s", "90s", "Adult Contemporary", "Alternative", "Ambient",
                    "Blues", "Classical", "Classic Rock", "Country", "Dance", "Disco",
                    "Drum & Bass", "EDM", "Electronic", "Folk", "Funk", "Gospel",
                    "Hip-Hop", "House", "Indie", "Jazz", "Latin", "Lounge", "Metal",
                    "News", "Oldies", "Pop", "R&B", "Reggae", "Rock", "Soul",
                    "Sports", "Talk", "Top 40", "Urban"
                ] + countryGenres
                var ordered: [String] = []
                for g in base40 where !ordered.contains(g) { ordered.append(g) }

                // Build genre index in one pass over stations
                var representedGenres = Set<String>()
                for station in all where allowed.contains(station.stationuuid) {
                    guard let tags = station.tags else { continue }
                    for raw in tags.split(separator: ",") {
                        let norm = raw.trimmingCharacters(in: .whitespacesAndNewlines)
                            .lowercased()
                            .replacingOccurrences(of: "-", with: " ")
                            .replacingOccurrences(of: "_", with: " ")
                        for displayGenre in ordered where displayGenre != "All" {
                            if norm.contains(displayGenre.lowercased()) {
                                representedGenres.insert(displayGenre)
                            }
                        }
                    }
                }

                let buttons = ["All"] + ordered
                    .filter { $0 != "All" && representedGenres.contains($0) }
                    .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }

                return (sorted, genreFiltered, buttons)
            }.value

            guard !Task.isCancelled else { return }
            filteredCountryStations = newFiltered
            genreFilteredStations   = newGenreFiltered
            genreButtons            = newButtons
        }
    }

    // Fast bitrate extraction without regex — nonisolated so it can run off main thread
    private nonisolated static func quickBitrate(_ station: RadioStation) -> Int {
        let name = station.name.lowercased()
        let suffixes = ["kbps", "kbit", "kb", "k"]
        let digits = "0123456789"
        for sfx in suffixes {
            guard let r = name.range(of: sfx) else { continue }
            var i = name.index(before: r.lowerBound)
            while i > name.startIndex, digits.contains(name[i]) {
                i = name.index(before: i)
            }
            let numStr = String(name[name.index(after: i)..<r.lowerBound])
            if let v = Int(numStr), v >= 32, v <= 320 { return v }
        }
        return 128
    }

    private func rebuildTopStationPresentation() {
        let primaryLimit = 30
        let totalLimit = 150
        var output: [RadioStation] = []
        var seenIDs = Set<String>()
        var seenPresentation = Set<String>()
        let rankedCandidates = popularityRankedTopCandidates()

        func append(_ station: RadioStation) {
            let presentationKey = topStationIdentity(station, for: selectedCountry)
            guard !seenIDs.contains(station.stationuuid),
                  !seenPresentation.contains(presentationKey),
                  isEligibleForTopStations(station, in: selectedCountry),
                  isAllowedBySportsAndNewsPreferences(station) else { return }
            seenIDs.insert(station.stationuuid)
            seenPresentation.insert(presentationKey)
            output.append(station)
        }

        for station in countryTopStations {
            guard output.count < primaryLimit else { break }
            append(station)
        }

        if output.count < primaryLimit {
            for station in rankedCandidates {
                guard output.count < primaryLimit else { break }
                append(station)
            }
        }

        for station in rankedCandidates {
            guard output.count < totalLimit else { break }
            append(station)
        }

        topStationPresentation = output
    }
    
    func setSort(_ sort: StationSort) { selectedSort = sort }
    func setFilter(_ filter: StationFilter) { selectedFilter = filter }
    func setGenre(_ genre: String) { selectedGenre = genre }

    var stationContentPreferences: StationContentPreferences {
        contentPreferences
    }

    func updateContentPreferences(
        showReligiousContent: Bool? = nil,
        showSportsContent: Bool? = nil,
        showNewsContent: Bool? = nil
    ) {
        let defaults = UserDefaults.standard
        if let showReligiousContent {
            defaults.set(showReligiousContent, forKey: GlassRadioPreferenceKey.showReligiousContent)
        }
        if let showSportsContent {
            defaults.set(showSportsContent, forKey: GlassRadioPreferenceKey.showSportsContent)
        }
        if let showNewsContent {
            defaults.set(showNewsContent, forKey: GlassRadioPreferenceKey.showNewsContent)
        }
        reloadContentPreferences()
    }

    private func reloadContentPreferences() {
        contentPreferences = StationContentPreferences.load()  // didSet triggers rebuild
        rebuildTopStationPresentation()

        if quickSearchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            quickSearchResults = filterForContentPreferences(quickSearchResults)
        } else {
            Task { await performQuickSearch(for: quickSearchText, limit: 60) }
        }

        if searchPageQuery.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            searchPageResults = filterForContentPreferences(searchPageResults)
        } else {
            Task { await performSearchPageQuery(for: searchPageQuery) }
        }
    }

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
        } else if let diskSnapshot = loadSnapshotFromDisk(for: country) {
            countryCache[country.id] = diskSnapshot
            applySnapshot(diskSnapshot)
            countryInfoMessage = "Showing saved \(country.displayName) stations while refreshing..."
        }

        do {
            let quickStations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 600)
            guard isLoadActive(loadToken, for: country) else { return }
            await applyStations(quickStations, for: country)

            async let expandedStationsTask = service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 1800)
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
            if countryAllStations.isEmpty {
                let presetFallback = presets.filter { stationBelongsToCountry($0, country: country) }
                if presetFallback.isEmpty {
                    errorMessage = "Failed loading \(country.displayName)."
                    countryTopStations = []
                    countryAllStations = []
                    countryStationCount = 0
                    stationVariantsByPrimaryId = [:]
                    player.setStationVariants([:])
                } else {
                    let fallbackSnapshot = await buildSnapshot(from: presetFallback, for: country)
                    countryCache[country.id] = fallbackSnapshot
                    applySnapshot(fallbackSnapshot)
                    errorMessage = nil
                    countryInfoMessage = "Could not refresh \(country.displayName). Showing saved presets."
                }
            } else {
                errorMessage = nil
                countryInfoMessage = "Could not refresh \(country.displayName). Showing saved stations."
            }
        }

        if isLoadActive(loadToken, for: country) {
            isLoading = false
        }
    }

    func completeOnboarding(selectedCountries: [CountryPreset]) async {
        let primary = selectedCountries.first ?? selectedCountry
        selectedCountry = primary
        UserDefaults.standard.set(primary.id, forKey: selectedCountryKey)
        isOnboarding = false
        // Full pull for the primary country — shows loading screen via cacheCountryForLaunch
        await cacheCountryForLaunch(primary)
        if AppAssetStore.hasStationSnapshot(countryID: primary.id) {
            UserDefaults.standard.set(true, forKey: initialCacheKey)
        }
        await loadCountry(primary)
        await loadDiscover()
        // Background: full pulls for remaining selected + basic pulls for everyone else
        let fullIDs = Set(selectedCountries.map(\.id))
        startBackgroundRefresh(fullPullIDs: fullIDs)
    }

    func loadDiscover() async {
        let spotlight = Array(CountryPreset.all.prefix(6))
        var merged: [RadioStation] = []

        for country in spotlight {
            do {
                let stations = try await service.fetchCountryStations(country: country.apiName, countryCode: country.countryCode, limit: 300)
                let top = curatedTopStations(from: sanitizeStations(stations, for: country), for: country)
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
            quickSearchResults = filterForContentPreferences(results)
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
            searchPageResults = filterForContentPreferences(results)
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
        saveLastPlayedStation(station)
        player.play(station, in: list)
    }

    func playNext() {
        player.playNext()
        nowPlayingStation = player.currentStation
        if let nowPlayingStation {
            saveLastPlayedStation(nowPlayingStation)
        }
    }

    func playPrevious() {
        player.playPrevious()
        nowPlayingStation = player.currentStation
        if let nowPlayingStation {
            saveLastPlayedStation(nowPlayingStation)
        }
    }

    private func restoreLastPlayedStation() {
        guard let data = UserDefaults.standard.data(forKey: GlassRadioPreferenceKey.lastPlayedStation),
              let station = try? JSONDecoder().decode(RadioStation.self, from: data) else { return }
        nowPlayingStation = station
        player.prepare(station)
    }

    private func saveLastPlayedStation(_ station: RadioStation) {
        guard let data = try? JSONEncoder().encode(station) else { return }
        UserDefaults.standard.set(data, forKey: GlassRadioPreferenceKey.lastPlayedStation)
    }

    private func sanitizeStations(_ stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
        let arabicContextIds: Set<String> = ["eg", "sa", "ae"]
        let islamicTerms = ["quran", "quraan", "koran", "islamic", "muslim",
                            "salafi", "dawah", "nooran", "deen fm"]
        let isArabicContext = arabicContextIds.contains(country.id)

        return uniqueById(stations)
            .filter { station in
                let hasStream = !(station.urlResolved.isEmpty) || !(station.url?.isEmpty ?? true)
                let lower = station.name.lowercased()
                guard hasStream,
                      !lower.contains("scanner"),
                      !lower.contains("test"),
                      !lower.contains("localhost") else { return false }

                if !isArabicContext {
                    let lang = station.language?.lowercased() ?? ""
                    let tags = station.tags?.lowercased() ?? ""
                    if lang.contains("arabic") { return false }
                    if islamicTerms.contains(where: { tags.contains($0) || lower.contains($0) }) { return false }
                }
                return true
            }
    }

    private func curatedTopStations(from stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
        let curated = stations
            .filter { isEligibleForTopStations($0, in: country) }
            .compactMap { station -> (Int, RadioStation)? in
                guard let idx = country.topBrands.firstIndex(where: { brandMatches(stationName: station.name, brand: $0) }) else {
                    return nil
                }
                return (idx, station)
            }
            .sorted { lhs, rhs in
                if lhs.0 != rhs.0 { return lhs.0 < rhs.0 }
                let lhsBrand = country.topBrands[lhs.0]
                let rhsBrand = country.topBrands[rhs.0]
                let lhsQuality = brandMatchQuality(stationName: lhs.1.name, brand: lhsBrand)
                let rhsQuality = brandMatchQuality(stationName: rhs.1.name, brand: rhsBrand)
                if lhsQuality != rhsQuality { return lhsQuality > rhsQuality }
                let lhsArtwork = artworkQuality(lhs.1)
                let rhsArtwork = artworkQuality(rhs.1)
                if lhsArtwork != rhsArtwork { return lhsArtwork > rhsArtwork }
                return popularity(lhs.1) > popularity(rhs.1)
            }
            .map(\.1)

        return uniqueByTopStationSlot(curated, for: country)
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

                let lhsArtwork = artworkQuality(lhs)
                let rhsArtwork = artworkQuality(rhs)
                if lhsArtwork != rhsArtwork { return lhsArtwork > rhsArtwork }

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

    private func isReligiousContent(_ station: RadioStation) -> Bool {
        let haystack = normalizeStationName([
            station.name,
            station.tags ?? "",
            station.language ?? ""
        ].joined(separator: " "))

        return religiousTopStationTerms.contains { term in
            haystack.contains(term)
        }
    }

    private func isSportsContent(_ station: RadioStation) -> Bool {
        let haystack = normalizeStationName([
            station.name,
            station.tags ?? "",
            station.language ?? ""
        ].joined(separator: " "))

        return sportsContentTerms.contains { term in
            haystack.contains(term)
        }
    }

    private func isNewsContent(_ station: RadioStation) -> Bool {
        let haystack = normalizeStationName([
            station.name,
            station.tags ?? "",
            station.language ?? ""
        ].joined(separator: " "))

        return newsContentTerms.contains { term in
            haystack.contains(term)
        }
    }

    private func isAllowedBySportsAndNewsPreferences(_ station: RadioStation) -> Bool {
        if !contentPreferences.showSportsContent, isSportsContent(station) { return false }
        if !contentPreferences.showNewsContent, isNewsContent(station) { return false }
        return true
    }

    private func isAllowedByContentPreferences(_ station: RadioStation) -> Bool {
        if !contentPreferences.showReligiousContent, isReligiousContent(station) { return false }
        return isAllowedBySportsAndNewsPreferences(station)
    }

    private func filterForContentPreferences(_ stations: [RadioStation]) -> [RadioStation] {
        stations.filter { isAllowedByContentPreferences($0) }
    }

    private func isEligibleForTopStations(_ station: RadioStation, in country: CountryPreset) -> Bool {
        guard !isReligiousContent(station) else { return false }
        if country.id == "uk",
           (isUKLocalBBCStation(station) || isUKAudienceSpecificTopStation(station)) {
            return false
        }
        return true
    }

    private func isUKAudienceSpecificTopStation(_ station: RadioStation) -> Bool {
        let name = normalizeStationName(station.name)
        return name.contains("bbc asian network")
    }

    private func isUKLocalBBCStation(_ station: RadioStation) -> Bool {
        let name = normalizeStationName(station.name)
        guard name.hasPrefix("bbc radio ") else { return false }
        let nationalTerms = [
            "bbc radio 1", "bbc radio 1xtra", "bbc radio 2", "bbc radio 3",
            "bbc radio 4", "bbc radio 5 live", "bbc radio 6 music",
            "bbc asian network", "bbc world service"
        ]
        return !nationalTerms.contains { name.contains($0) }
    }

    private func hasArtwork(_ station: RadioStation) -> Bool {
        artworkQuality(station) > 0
    }

    private func artworkQuality(_ station: RadioStation) -> Int {
        guard let favicon = station.favicon?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased(),
              !favicon.isEmpty else { return 0 }
        if favicon.contains(".png") || favicon.contains(".jpg") || favicon.contains(".jpeg") || favicon.contains(".webp") {
            return 3
        }
        if favicon.contains(".svg") { return 2 }
        if favicon.contains(".ico") { return 1 }
        return 1
    }

    private let religiousTopStationTerms = [
        "quran", "quraan", "koran", "islamic", "muslim", "salafi", "dawah",
        "deen fm", "christian", "christ", "jesus", "church", "bible",
        "catholic", "evangelical", "worship", "prayer", "ministry",
        "ministries", "sermon", "praise", "hindu", "bhajan", "buddhist",
        "dharma", "torah", "jewish", "synagogue"
    ]

    private let sportsContentTerms = [
        "sport", "sports", "football", "soccer", "match day", "matchday",
        "talksport", "talk sport", "espn", "fox sports", "sky sports",
        "bbc sport", "5 live sports extra", "radio marca", "rugby", "cricket",
        "baseball", "basketball", "hockey", "nfl", "nba", "mlb", "nhl",
        "f1", "formula 1", "racing"
    ]

    private let newsContentTerms = [
        "news", "noticias", "nieuws", "nachrichten", "actualites", "actualité",
        "actualite", "cnn", "bbc world service", "bbc news", "sky news",
        "fox news", "npr news", "france info", "euronews", "al jazeera",
        "cna938", "lbc news", "world service"
    ]
    
    private func weightedPopularity(_ station: RadioStation) -> Int {
        popularity(station) + majorBrandPriority(station)
    }
    
    private func majorBrandPriority(_ station: RadioStation) -> Int {
        majorBrandScoreById[station.id] ?? 0
    }
    
    private func matchesMajorBrand(_ station: RadioStation) -> Bool {
        (majorBrandScoreById[station.id] ?? 0) > 0
    }

    private func popularityRankedTopCandidates() -> [RadioStation] {
        countryAllStations
            .filter { isEligibleForTopStations($0, in: selectedCountry) && isAllowedBySportsAndNewsPreferences($0) }
            .sorted {
                let lhsScore = weightedPopularity($0)
                let rhsScore = weightedPopularity($1)
                if lhsScore != rhsScore { return lhsScore > rhsScore }
                return clickCount($0) > clickCount($1)
            }
    }

    func brandClusters(for query: String) -> [BrandCluster] {
        let trimmed = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.count >= 3 else { return [] }
        let target = normalizeStationName(trimmed)
        guard !target.isEmpty else { return [] }

        let base = searchPageResults.isEmpty ? filterForContentPreferences(countryAllStations) : searchPageResults
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
            .replacingOccurrences(of: #"\b\d{2,3}\s?(kb?|kbps)\b"#, with: " ", options: .regularExpression)
            .replacingOccurrences(of: #"\b(aac\+?|mp3|ogg|stream|live|hq|lq)\b"#, with: " ", options: .regularExpression)

        return normalizeStationName(simplified)
    }

    private func isLikelyVariantName(_ name: String) -> Bool {
        let lowered = name.lowercased()
        return lowered.range(of: #"\b\d{2,3}\s?(kb?|kbps)\b"#, options: .regularExpression) != nil
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

    private func brandMatchQuality(stationName: String, brand: String) -> Int {
        let raw = stationName.lowercased()
        let station = normalizeStationName(stationName)
        let target = normalizeStationName(brand)
        guard !station.isEmpty, !target.isEmpty else { return 0 }
        if station.hasPrefix(target + " "), raw.contains(":") || raw.contains(" - ") { return 5 }
        if station == target { return 4 }
        if station.hasPrefix(target + " ") { return 3 }
        if station.contains(target) { return 2 }
        return brandMatches(stationName: stationName, brand: brand) ? 1 : 0
    }

    private func topStationIdentity(_ station: RadioStation, for country: CountryPreset) -> String {
        if let idx = country.topBrands.firstIndex(where: { brandMatches(stationName: station.name, brand: $0) }) {
            return "brand:\(idx)"
        }
        return "station:\(canonicalStationName(station.name))"
    }

    private func uniqueByTopStationSlot(_ stations: [RadioStation], for country: CountryPreset) -> [RadioStation] {
        var seen = Set<String>()
        var output: [RadioStation] = []

        for station in stations {
            let key = topStationIdentity(station, for: country)
            guard !key.isEmpty, !seen.contains(key) else { continue }
            seen.insert(key)
            output.append(station)
        }

        return output
    }
    
    private func stationBelongsToCountry(_ station: RadioStation, country: CountryPreset) -> Bool {
        let lhs = normalizeStationName(station.country)
        let a = normalizeStationName(country.apiName)
        let b = normalizeStationName(country.displayName)
        return lhs == a || lhs == b || lhs.contains(a) || lhs.contains(b)
    }
    
    private func fetchMajorBrandStations(for country: CountryPreset) async throws -> [RadioStation] {
        try Task.checkCancellation()
        return await fetchSearchTerms(country.topBrands, for: country, limit: 80, batchSize: 4)
    }

    private func fetchRegionalStations(for country: CountryPreset) async throws -> [RadioStation] {
        let terms = regionalSearchTerms(for: country)
        guard !terms.isEmpty else { return [] }
        try Task.checkCancellation()
        return await fetchSearchTerms(terms, for: country, limit: 100, batchSize: 3)
    }

    private func fetchSearchTerms(_ terms: [String], for country: CountryPreset, limit: Int, batchSize: Int) async -> [RadioStation] {
        var merged: [RadioStation] = []
        let clampedBatchSize = max(1, batchSize)

        for batchStart in stride(from: 0, to: terms.count, by: clampedBatchSize) {
            if Task.isCancelled { break }
            let batchEnd = min(batchStart + clampedBatchSize, terms.count)
            let batch = Array(terms[batchStart..<batchEnd])
            let batchResults = await withTaskGroup(of: [RadioStation].self) { group in
                for term in batch {
                    group.addTask { [service] in
                        do {
                            return try await service.searchStations(query: term, limit: limit)
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

            merged.append(contentsOf: batchResults)
        }

        return uniqueById(merged)
    }

    private func applyStations(_ rawStations: [RadioStation], for country: CountryPreset) async {
        let snapshot = await buildSnapshot(from: rawStations, for: country)
        countryCache[country.id] = snapshot
        saveSnapshotToDisk(snapshot, for: country)
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
        rebuildTopStationPresentation()
    }

    private func buildSnapshot(from rawStations: [RadioStation], for country: CountryPreset) async -> CountrySnapshot {
        await Task.detached(priority: .userInitiated) {
            SnapshotBuilder.buildSnapshot(from: rawStations, for: country)
        }.value
    }

    private func storeSnapshot(_ rawStations: [RadioStation], for country: CountryPreset) async {
        let snapshot = await buildSnapshot(from: rawStations, for: country)
        countryCache[country.id] = snapshot
        saveSnapshotToDisk(snapshot, for: country)
    }

    private func snapshotURL(for country: CountryPreset) -> URL {
        AppAssetStore.stationSnapshotsDirectory.appendingPathComponent("\(country.id).json")
    }

    private func loadSnapshotFromDisk(for country: CountryPreset) -> CountrySnapshot? {
        let url = snapshotURL(for: country)
        guard let data = try? Data(contentsOf: url),
              let snapshot = try? JSONDecoder().decode(CountrySnapshot.self, from: data),
              snapshot.schemaVersion == Self.countrySnapshotVersion,
              !snapshot.allStations.isEmpty else {
            return nil
        }
        return snapshot
    }

    private func saveSnapshotToDisk(_ snapshot: CountrySnapshot, for country: CountryPreset) {
        guard !snapshot.allStations.isEmpty,
              let data = try? JSONEncoder().encode(snapshot) else { return }
        AppAssetStore.ensureDirectories()
        try? data.write(to: snapshotURL(for: country), options: [.atomic])
    }

    private func recordRecentCountry(_ id: String) {
        recentCountryIDs.removeAll { $0 == id }
        recentCountryIDs.insert(id, at: 0)
        if recentCountryIDs.count > 6 {
            recentCountryIDs.removeLast(recentCountryIDs.count - 6)
        }
    }

    private func startBackgroundRefresh(fullPullIDs: Set<String> = []) {
        backgroundCacheTask?.cancel()
        backgroundCacheTask = Task.detached(priority: .background) { [weak self, fullPullIDs] in
            guard let self else { return }
            await self.quickRefreshAtLaunch(fullPullIDs: fullPullIDs)
            await self.periodicCacheRefresh()
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

    private func quickRefreshAtLaunch(fullPullIDs: Set<String> = []) async {
        let seed = await MainActor.run { selectedCountry }
        let retryCandidates = Set(failedInitialCacheCounts
            .filter { $0.value < maxInitialCacheRetries }
            .keys)

        // Split uncached countries into full-pull (user-selected) vs basic-pull (everyone else)
        var fullPullCountries: [CountryPreset] = []
        var basicPullCountries: [CountryPreset] = []
        for country in CountryPreset.all {
            guard country.id != seed.id else { continue }
            let hasDisk = AppAssetStore.hasStationSnapshot(countryID: country.id)
            let isRetry = retryCandidates.contains(country.id)
            guard !hasDisk || isRetry else { continue }
            if fullPullIDs.contains(country.id) {
                fullPullCountries.append(country)
            } else {
                basicPullCountries.append(country)
            }
        }

        let allToFetch = fullPullCountries + basicPullCountries
        guard !allToFetch.isEmpty else { return }

        await MainActor.run {
            isBackgroundRefreshing = true
            backgroundRefreshProgress = 0
            backgroundRefreshMessage = fullPullIDs.isEmpty ? "Building station library…" : "Caching your library…"
        }

        let total = max(1, allToFetch.count)
        var completed = 0

        // Full pulls for user-selected countries
        for country in fullPullCountries {
            if Task.isCancelled { break }
            do {
                let timeout = retryCandidates.contains(country.id) ? 60.0 : 40.0
                let merged = try await withTimeout(seconds: timeout) { [service, self] in
                    let stations = try await service.fetchCountryStations(
                        country: country.apiName,
                        countryCode: country.countryCode,
                        limit: 1400
                    )
                    async let major = self.fetchMajorBrandStations(for: country)
                    async let regional = self.fetchRegionalStations(for: country)
                    return stations + (try await major) + (try await regional)
                }
                await storeSnapshot(merged, for: country)
                await updateFailedCacheCount(countryID: country.id, success: true)
            } catch {
                await updateFailedCacheCount(countryID: country.id, success: false)
            }
            completed += 1
            let remaining = total - completed
            await MainActor.run {
                backgroundRefreshProgress = Double(completed) / Double(total)
                backgroundRefreshMessage = remaining > 0 ? "Caching \(country.displayName)… (\(remaining) remaining)" : "Library ready"
            }
        }

        // Basic pulls (400 stations) for all remaining uncached countries
        for country in basicPullCountries {
            if Task.isCancelled { break }
            do {
                let timeout = retryCandidates.contains(country.id) ? 45.0 : 20.0
                let stations = try await withTimeout(seconds: timeout) { [service] in
                    try await service.fetchCountryStations(
                        country: country.apiName,
                        countryCode: country.countryCode,
                        limit: 400
                    )
                }
                await storeSnapshot(stations, for: country)
                await updateFailedCacheCount(countryID: country.id, success: true)
            } catch {
                await updateFailedCacheCount(countryID: country.id, success: false)
            }
            completed += 1
            let remaining = total - completed
            await MainActor.run {
                backgroundRefreshProgress = Double(completed) / Double(total)
                backgroundRefreshMessage = remaining > 0 ? "Building library… (\(remaining) remaining)" : "Library ready"
            }
        }

        await MainActor.run { isBackgroundRefreshing = false }
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

    private func isLoadActive(_ token: UUID, for country: CountryPreset) -> Bool {
        token == activeCountryLoadToken && selectedCountry.id == country.id
    }

    private func regionalSearchTerms(for country: CountryPreset) -> [String] {
        switch country.id {
        case "uk":
            return [
                "Heart UK", "Virgin Radio UK", "Capital UK",
                "Absolute Radio", "KISS UK", "Smooth Radio", "Greatest Hits Radio",
                "Signal 1", "LBC", "talkSPORT",
                "Hits Radio", "Radio X", "Times Radio", "Talk Radio",
                "Kisstory", "Magic Radio", "Scala Radio", "Planet Rock",
                "Jazz FM", "Gold Radio", "BBC Radio 5 Live", "Heart Extra", "Capital Xtra"
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

    private func cacheCountryForLaunch(_ country: CountryPreset) async {
        guard !AppAssetStore.hasStationSnapshot(countryID: country.id) else { return }

        await MainActor.run {
            isInitialLoading = true
            initialLoadingProgress = 0
            initialLoadingMessage = "Caching \(country.displayName)..."
        }

        do {
            let merged = try await withTimeout(seconds: 28) { [service, self] in
                let stations = try await service.fetchCountryStations(
                    country: country.apiName,
                    countryCode: country.countryCode,
                    limit: 1400
                )
                async let major = self.fetchMajorBrandStations(for: country)
                async let regional = self.fetchRegionalStations(for: country)
                return stations + (try await major) + (try await regional)
            }
            await storeSnapshot(merged, for: country)
            await updateFailedCacheCount(countryID: country.id, success: true)
            UserDefaults.standard.set(true, forKey: initialCacheKey)
        } catch {
            await updateFailedCacheCount(countryID: country.id, success: false)
        }

        await MainActor.run {
            initialLoadingProgress = 1
            initialLoadingMessage = "Opening \(country.displayName)..."
            isInitialLoading = false
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

    func forceRefreshAllCountries() {
        backgroundCacheTask?.cancel()
        let dir = AppAssetStore.stationSnapshotsDirectory
        if let files = try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) {
            files.filter { $0.pathExtension == "json" }.forEach { try? FileManager.default.removeItem(at: $0) }
        }
        countryCache.removeAll()
        failedInitialCacheCounts = [:]
        Self.saveFailedCacheCounts([:], key: failedCacheKey)
        UserDefaults.standard.removeObject(forKey: initialCacheKey)
        startBackgroundRefresh()
    }

    func triggerFullPull(for country: CountryPreset) async {
        guard !fullPullInProgress.contains(country.id) else { return }
        fullPullInProgress.insert(country.id)
        do {
            let merged = try await withTimeout(seconds: 40) { [service, self] in
                let stations = try await service.fetchCountryStations(
                    country: country.apiName,
                    countryCode: country.countryCode,
                    limit: 1400
                )
                async let major = self.fetchMajorBrandStations(for: country)
                async let regional = self.fetchRegionalStations(for: country)
                return stations + (try await major) + (try await regional)
            }
            await storeSnapshot(merged, for: country)
            await updateFailedCacheCount(countryID: country.id, success: true)
        } catch {
            await updateFailedCacheCount(countryID: country.id, success: false)
        }
        fullPullInProgress.remove(country.id)
    }

    var cachedCountryCount: Int {
        CountryPreset.all.filter { AppAssetStore.hasStationSnapshot(countryID: $0.id) }.count
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
}
