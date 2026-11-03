import Foundation
import Network
import os

// MARK: - Glass Radio Local API
//
// Listens on all interfaces on <port> (default 7373) for local network access.
// Designed for integration with home dashboards / hub projects on the same network.
//
// Endpoints:
//   GET  /status                  → current playback state
//   GET  /metrics                 → playback, catalog, loading, and preference metrics
//   GET  /preferences             → current user preferences
//   POST /preferences             → update content/playback preferences
//   POST /play                    → resume/start; optional body {"uuid":"<stationuuid>"}
//   POST /pause                   → pause playback
//   POST /toggle                  → toggle play/pause
//   POST /next                    → next station in queue
//   POST /previous                → previous station in queue
//   POST /volume                  → set volume; body {"level": 0.0–1.0}
//   POST /country                 → load country; body {"id":"uk"} or {"country":"United Kingdom"}
//   POST /browse                  → set browse state; body {"filter":"Popular Picks","sort":"Most Popular","genre":"Pop"}
//   POST /search                  → run search; body {"query":"BBC Radio 2","limit":160}
//   GET  /favorites               → list favorited stations
//   GET  /countries               → list supported countries
//   GET  /stations                → stations; query scope=top|all|genre|search|favorites|discover&limit=50&offset=0
//
// All responses include CORS headers so browser-based dashboards can call the API.

@MainActor
final class LocalAPIServer {
    private var listener: NWListener?
    private weak var viewModel: AppViewModel?
    private(set) var port: UInt16 = LocalAPIConfiguration.defaultPort
    private var activeConfiguration = LocalAPIConfiguration.load()

    private let logger = Logger(subsystem: "com.jab.glassradio", category: "LocalAPI")
    private let apiVersion = "1.2"
    private let requestLimitPerMinute = 180
    private var recentRequestDates: [Date] = []

    init() {}

    func start(viewModel: AppViewModel) {
        self.viewModel = viewModel
        startListener(using: LocalAPIConfiguration.load())
    }

    func applySettingsIfNeeded() {
        let configuration = LocalAPIConfiguration.load()
        guard configuration.enabled != activeConfiguration.enabled
            || configuration.smartPortEnabled != activeConfiguration.smartPortEnabled
            || configuration.preferredPort != activeConfiguration.preferredPort else { return }
        restart(using: configuration)
    }

    private func restart(using configuration: LocalAPIConfiguration) {
        stop()
        startListener(using: configuration)
    }

    private func startListener(using configuration: LocalAPIConfiguration) {
        activeConfiguration = configuration
        guard configuration.enabled else {
            self.port = 0
            UserDefaults.standard.set(0, forKey: GlassRadioPreferenceKey.apiActivePort)
            logger.info("LocalAPI: disabled")
            return
        }

        let params = NWParameters.tcp
        params.allowLocalEndpointReuse = true

        let candidates = portCandidates(for: configuration)
        for candidate in candidates {
            guard let nwPort = NWEndpoint.Port(rawValue: candidate) else { continue }

            let listener: NWListener
            do {
                listener = try NWListener(using: params, on: nwPort)
            } catch {
                logger.error("LocalAPI: port \(candidate) unavailable — \(error.localizedDescription)")
                continue
            }

            self.listener = listener
            self.port = candidate
            UserDefaults.standard.set(Int(candidate), forKey: GlassRadioPreferenceKey.apiActivePort)

            let logger = self.logger
            listener.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    logger.info("LocalAPI: listening on port \(candidate)")
                case .failed(let error):
                    logger.error("LocalAPI: listener failed on port \(candidate) — \(error.localizedDescription)")
                case .cancelled:
                    logger.info("LocalAPI: listener stopped")
                default:
                    break
                }
            }

            listener.newConnectionHandler = { [weak self] connection in
                guard let server = self else { return }
                Task { @MainActor in
                    server.handleConnection(connection)
                }
            }
            listener.start(queue: .global(qos: .utility))
            return
        }

        logger.error("LocalAPI: no configured port was available")
    }

    func stop() {
        listener?.cancel()
        listener = nil
    }

    private func portCandidates(for configuration: LocalAPIConfiguration) -> [UInt16] {
        if !configuration.smartPortEnabled {
            return [configuration.preferredPort]
        }

        var candidates = [configuration.preferredPort, LocalAPIConfiguration.defaultPort]
        candidates.append(contentsOf: (LocalAPIConfiguration.defaultPort...7399))
        var seen = Set<UInt16>()
        return candidates.filter { seen.insert($0).inserted }
    }

    // MARK: - Connection handling

    private func handleConnection(_ connection: NWConnection) {
        connection.start(queue: .global(qos: .utility))
        receiveRequest(on: connection)
    }

    private func receiveRequest(on connection: NWConnection) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 65_536) { [weak self] data, _, _, _ in
            guard let self, let data, !data.isEmpty else {
                connection.cancel()
                return
            }
            let rawRequest = String(data: data, encoding: .utf8) ?? ""
            Task { @MainActor in
                let response = self.routeRequest(raw: rawRequest)
                self.sendResponse(response, on: connection)
            }
        }
    }

    private func sendResponse(_ response: HTTPAPIResponse, on connection: NWConnection) {
        let headers = [
            "HTTP/1.1 \(response.status)",
            "Content-Type: application/json; charset=utf-8",
            "Content-Length: \(response.body.count)",
            "Access-Control-Allow-Origin: *",
            "Access-Control-Allow-Methods: GET, POST, OPTIONS",
            "Access-Control-Allow-Headers: Content-Type, Authorization, X-Glass-Radio-Key",
            "Connection: close",
            "", ""
        ].joined(separator: "\r\n")

        var responseData = headers.data(using: .utf8)!
        responseData.append(response.body)

        connection.send(content: responseData, completion: .contentProcessed { _ in
            connection.cancel()
        })
    }

    // MARK: - Router

    private func routeRequest(raw: String) -> HTTPAPIResponse {
        let lines = raw.components(separatedBy: "\r\n")
        guard let requestLine = lines.first else { return .notFound }

        let parts = requestLine.components(separatedBy: " ")
        guard parts.count >= 2 else { return .notFound }

        let method = parts[0].uppercased()
        let fullPath = parts[1]
        let pathAndQuery = fullPath.components(separatedBy: "?")
        let path = pathAndQuery[0]
        let queryString = pathAndQuery.count > 1 ? pathAndQuery[1] : ""
        let queryParams = parseQuery(queryString)
        let headers = parseHeaders(lines)

        var bodyJSON: [String: Any] = [:]
        if let bodyRange = raw.range(of: "\r\n\r\n") {
            let bodyText = String(raw[bodyRange.upperBound...])
                .trimmingCharacters(in: .whitespacesAndNewlines)
            if let jsonData = bodyText.data(using: .utf8),
               let parsed = try? JSONSerialization.jsonObject(with: jsonData) as? [String: Any] {
                bodyJSON = parsed
            }
        }

        if method == "OPTIONS" {
            return HTTPAPIResponse(status: "200 OK", body: Data())
        }

        guard allowRequest() else {
            return .tooManyRequests
        }

        if let unauthorized = authorize(headers: headers, queryParams: queryParams) {
            return unauthorized
        }

        switch (method, path) {
        case ("GET", "/status"):    return handleStatus()
        case ("GET", "/metrics"):   return handleMetrics()
        case ("GET", "/preferences"): return handlePreferences()
        case ("POST", "/preferences"): return handleUpdatePreferences(params: bodyJSON)
        case ("POST", "/play"):     return handlePlay(params: bodyJSON)
        case ("POST", "/pause"):    return handlePause()
        case ("POST", "/toggle"):   return handleToggle()
        case ("POST", "/next"):     return handleNext()
        case ("POST", "/previous"): return handlePrevious()
        case ("POST", "/volume"):   return handleVolume(params: bodyJSON)
        case ("POST", "/country"):  return handleCountry(params: bodyJSON)
        case ("POST", "/browse"):   return handleBrowse(params: bodyJSON)
        case ("POST", "/search"):   return handleSearch(params: bodyJSON)
        case ("GET", "/favorites"): return handleFavorites()
        case ("GET", "/countries"): return handleCountries()
        case ("GET", "/stations"):  return handleStations(params: queryParams)
        default:                    return .notFound
        }
    }

    // MARK: - Endpoint handlers

    private func handleStatus() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        let player = vm.player

        var payload: [String: Any] = [
            "playing": player.isPlaying,
            "connecting": player.isConnecting,
            "volume": Double(player.volume),
            "external_playback": player.isExternalPlaybackActive,
            "api_version": apiVersion,
            "app": "Glass Radio"
        ]

        if let station = player.currentStation {
            payload["station"] = stationDict(station)
        } else {
            payload["station"] = NSNull()
        }

        if let bitrate = player.streamBitrateKbps { payload["bitrate_kbps"] = bitrate }
        if let throughput = player.streamThroughputKbps { payload["throughput_kbps"] = throughput }

        return json(payload)
    }

    private func handleMetrics() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        let player = vm.player

        let playback: [String: Any] = [
            "playing": player.isPlaying,
            "connecting": player.isConnecting,
            "volume": Double(player.volume),
            "external_playback": player.isExternalPlaybackActive,
            "bitrate_kbps": player.streamBitrateKbps as Any? ?? NSNull(),
            "throughput_kbps": player.streamThroughputKbps as Any? ?? NSNull(),
            "station": player.currentStation.map { stationDict($0) } as Any? ?? NSNull()
        ]

        let catalog: [String: Any] = [
            "selected_country": countryDict(vm.selectedCountry),
            "top_station_count": vm.topStationPresentation.count,
            "all_station_count": vm.countryAllStations.count,
            "favorite_count": vm.presets.count,
            "discover_count": vm.discoverStations.count,
            "quick_search_count": vm.quickSearchResults.count,
            "search_page_count": vm.searchPageResults.count,
            "reported_country_station_count": vm.countryStationCount
        ]

        let browse: [String: Any] = [
            "filter": vm.selectedFilter.rawValue,
            "sort": vm.selectedSort.rawValue,
            "genre": vm.selectedGenre
        ]

        let loading: [String: Any] = [
            "loading_country": vm.isLoading,
            "initial_loading": vm.isInitialLoading,
            "initial_progress": vm.initialLoadingProgress,
            "initial_message": vm.initialLoadingMessage,
            "background_refreshing": vm.isBackgroundRefreshing,
            "background_progress": vm.backgroundRefreshProgress,
            "background_message": vm.backgroundRefreshMessage,
            "country_message": vm.countryInfoMessage as Any? ?? NSNull(),
            "error": vm.errorMessage as Any? ?? NSNull()
        ]

        return json([
            "ok": true,
            "app": "Glass Radio",
            "api_version": apiVersion,
            "playback": playback,
            "catalog": catalog,
            "browse": browse,
            "loading": loading,
            "preferences": preferencesPayload(for: vm),
            "server": [
                "port": Int(port),
                "base_url": "http://127.0.0.1:\(port)",
                "smart_port_enabled": activeConfiguration.smartPortEnabled,
                "preferred_port": Int(activeConfiguration.preferredPort),
                "api_keys_configured": !LocalAPIKeyStore.shared.keys.isEmpty,
                "api_key_count": LocalAPIKeyStore.shared.keys.count
            ]
        ])
    }

    private func handlePreferences() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        return json(["ok": true, "preferences": preferencesPayload(for: vm)])
    }

    private func handleUpdatePreferences(params: [String: Any]) -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        let merged = flattenedPreferenceParams(params)

        let religious = boolValue(
            merged["show_religious_content"] ?? merged["show_religious"] ?? merged["religion"]
        )
        let sports = boolValue(
            merged["show_sports_content"] ?? merged["show_sports"] ?? merged["sports"]
        )
        let news = boolValue(
            merged["show_news_content"] ?? merged["show_news"] ?? merged["news"]
        )

        if religious != nil || sports != nil || news != nil {
            vm.updateContentPreferences(
                showReligiousContent: religious,
                showSportsContent: sports,
                showNewsContent: news
            )
        }

        let defaults = UserDefaults.standard
        if let defaultVolume = doubleValue(merged["default_volume"]) {
            defaults.set(max(0, min(1, defaultVolume)), forKey: "GlassRadio.DefaultVolume")
        }
        if let preferLowBitrate = boolValue(merged["prefer_low_bitrate"]) {
            defaults.set(preferLowBitrate, forKey: "GlassRadio.PreferLowBitrate")
        }
        if let connectTimeout = doubleValue(merged["connect_timeout_seconds"] ?? merged["connect_timeout"]) {
            defaults.set(max(6, min(30, connectTimeout)), forKey: "GlassRadio.ConnectTimeoutSeconds")
        }
        if let smartPortEnabled = boolValue(merged["smart_port_enabled"]) {
            defaults.set(smartPortEnabled, forKey: GlassRadioPreferenceKey.apiSmartPortEnabled)
        }
        if let enabled = boolValue(merged["api_enabled"] ?? merged["enabled"]) {
            defaults.set(enabled, forKey: GlassRadioPreferenceKey.apiEnabled)
        }
        if let preferredPort = doubleValue(merged["preferred_port"] ?? merged["port"]) {
            let clampedPort = Int(max(1024, min(65_535, preferredPort)))
            defaults.set(clampedPort, forKey: GlassRadioPreferenceKey.apiPreferredPort)
        }
        applySettingsIfNeeded()

        return json(["ok": true, "preferences": preferencesPayload(for: vm)])
    }

    private func handlePlay(params: [String: Any]) -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }

        if let uuid = params["uuid"] as? String {
            let allStations = uniqueStations(
                vm.topStationPresentation
                + vm.genreFilteredStations
                + vm.filteredCountryStations
                + vm.searchPageResults
                + vm.quickSearchResults
                + vm.discoverStations
                + vm.presets
            )
            if let station = allStations.first(where: { $0.stationuuid == uuid }) {
                vm.play(station)
                return json(["ok": true, "action": "play", "station": stationDict(station)])
            }
            return json(["ok": false, "error": "station_not_found"])
        }

        vm.player.togglePlayback()
        return json(["ok": true, "action": "play"])
    }

    private func handlePause() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        if vm.player.isPlaying { vm.player.togglePlayback() }
        return json(["ok": true, "action": "pause"])
    }

    private func handleToggle() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        vm.player.togglePlayback()
        return json(["ok": true, "action": "toggle", "now_playing": vm.player.isPlaying])
    }

    private func handleNext() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        vm.playNext()
        let stationValue: Any = vm.player.currentStation.map { stationDict($0) } as Any? ?? NSNull()
        return json(["ok": true, "action": "next", "station": stationValue])
    }

    private func handlePrevious() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        vm.playPrevious()
        let stationValue: Any = vm.player.currentStation.map { stationDict($0) } as Any? ?? NSNull()
        return json(["ok": true, "action": "previous", "station": stationValue])
    }

    private func handleVolume(params: [String: Any]) -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        guard let level = params["level"] as? Double else {
            return json(["ok": false, "error": "missing_level_field"])
        }
        let clamped = Float(max(0, min(1, level)))
        vm.player.volume = clamped
        return json(["ok": true, "action": "volume", "level": Double(clamped)])
    }

    private func handleCountry(params: [String: Any]) -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        guard let requested = stringValue(params["id"] ?? params["country"] ?? params["code"]) else {
            return .badRequest("missing_country")
        }
        guard let country = countryMatching(requested) else {
            return .badRequest("country_not_found")
        }

        Task { await vm.loadCountry(country) }
        return json(["ok": true, "action": "country", "loading": true, "country": countryDict(country)])
    }

    private func handleBrowse(params: [String: Any]) -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }

        if let rawFilter = stringValue(params["filter"]) {
            guard let filter = enumValue(rawFilter, in: AppViewModel.StationFilter.allCases) else {
                return .badRequest("unknown_filter")
            }
            vm.setFilter(filter)
        }

        if let rawSort = stringValue(params["sort"]) {
            guard let sort = enumValue(rawSort, in: AppViewModel.StationSort.allCases) else {
                return .badRequest("unknown_sort")
            }
            vm.setSort(sort)
        }

        if let rawGenre = stringValue(params["genre"]) {
            let normalizedGenre = rawGenre.trimmingCharacters(in: .whitespacesAndNewlines)
            if let match = vm.genreButtons.first(where: { normalize($0) == normalize(normalizedGenre) }) {
                vm.setGenre(match)
            } else if !normalizedGenre.isEmpty {
                vm.setGenre(normalizedGenre)
            }
        }

        return json([
            "ok": true,
            "action": "browse",
            "browse": [
                "filter": vm.selectedFilter.rawValue,
                "sort": vm.selectedSort.rawValue,
                "genre": vm.selectedGenre,
                "visible_station_count": vm.genreFilteredStations.count
            ]
        ])
    }

    private func handleSearch(params: [String: Any]) -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        guard let query = stringValue(params["query"])?.trimmingCharacters(in: .whitespacesAndNewlines) else {
            return .badRequest("missing_query")
        }

        if query.isEmpty {
            vm.searchPageQuery = ""
            vm.searchPageResults = []
            return json(["ok": true, "action": "search", "query": "", "accepted": false])
        }

        let limit = clampedInt(params["limit"], defaultValue: 160, minValue: 1, maxValue: 500)
        vm.searchPageQuery = query
        Task { await vm.performSearchPageQuery(for: query, limit: limit) }
        return json(["ok": true, "action": "search", "query": query, "limit": limit, "accepted": true])
    }

    private func handleFavorites() -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        let list = vm.presets.map { stationDict($0) }
        return json(["ok": true, "favorites": list, "count": list.count])
    }

    private func handleCountries() -> HTTPAPIResponse {
        let countries = CountryPreset.all.map { countryDict($0) }
        return json(["ok": true, "countries": countries, "count": countries.count])
    }

    private func handleStations(params: [String: String]) -> HTTPAPIResponse {
        guard let vm = viewModel else { return .unavailable }
        let scope = (params["scope"] ?? "top").lowercased()
        let maxLimit = scope == "top" ? 150 : 500
        let limit = clampedInt(params["limit"], defaultValue: 50, minValue: 1, maxValue: maxLimit)
        let offset = clampedInt(params["offset"], defaultValue: 0, minValue: 0, maxValue: Int.max)

        let source: [RadioStation]
        switch scope {
        case "top":
            source = vm.topStationPresentation
        case "all", "visible", "browse":
            source = vm.filteredCountryStations
        case "genre":
            source = vm.genreFilteredStations
        case "favorites", "favourites":
            source = vm.presets
        case "search":
            source = uniqueStations(vm.searchPageResults + vm.quickSearchResults)
        case "discover":
            source = vm.discoverStations
        default:
            return .badRequest("unknown_station_scope")
        }

        let page = Array(source.dropFirst(min(offset, source.count)).prefix(limit))
        let mapped = page.map { stationDict($0) }
        return json([
            "ok": true,
            "scope": scope,
            "country": countryDict(vm.selectedCountry),
            "stations": mapped,
            "count": mapped.count,
            "total": source.count,
            "limit": limit,
            "offset": offset
        ])
    }

    // MARK: - Utilities

    private func allowRequest(now: Date = Date()) -> Bool {
        let windowStart = now.addingTimeInterval(-60)
        recentRequestDates.removeAll { $0 < windowStart }
        guard recentRequestDates.count < requestLimitPerMinute else { return false }
        recentRequestDates.append(now)
        return true
    }

    private func authorize(headers: [String: String], queryParams: [String: String]) -> HTTPAPIResponse? {
        let keys = LocalAPIKeyStore.shared.keys.filter(\.isEnabled)
        guard !keys.isEmpty else { return .unauthorized }

        let submitted = headers["x-glass-radio-key"]
            ?? bearerToken(from: headers["authorization"])
            ?? queryParams["api_key"]
            ?? queryParams["key"]

        guard let submitted, LocalAPIKeyStore.shared.recordUse(forSecret: submitted) != nil else {
            return .unauthorized
        }
        return nil
    }

    private func bearerToken(from header: String?) -> String? {
        guard let header else { return nil }
        let parts = header.split(separator: " ", maxSplits: 1).map(String.init)
        guard parts.count == 2, parts[0].lowercased() == "bearer" else { return nil }
        return parts[1]
    }

    private func preferencesPayload(for vm: AppViewModel) -> [String: Any] {
        let defaults = UserDefaults.standard
        let content = vm.stationContentPreferences
        let defaultVolume: Double = (defaults.object(forKey: "GlassRadio.DefaultVolume") as? Double) ?? 0.8
        let preferLowBitrate: Bool = (defaults.object(forKey: "GlassRadio.PreferLowBitrate") as? Bool) ?? false
        let connectTimeout: Double = (defaults.object(forKey: "GlassRadio.ConnectTimeoutSeconds") as? Double) ?? 12
        return [
            "content": [
                "show_religious_content": content.showReligiousContent,
                "show_sports_content": content.showSportsContent,
                "show_news_content": content.showNewsContent,
                "top_stations_religion_promoted": false
            ],
            "playback": [
                "default_volume": defaultVolume,
                "prefer_low_bitrate": preferLowBitrate,
                "connect_timeout_seconds": connectTimeout
            ],
            "api": [
                "enabled": activeConfiguration.enabled,
                "smart_port_enabled": activeConfiguration.smartPortEnabled,
                "preferred_port": Int(activeConfiguration.preferredPort),
                "active_port": Int(port),
                "base_url": activeConfiguration.enabled ? "http://127.0.0.1:\(port)" : NSNull(),
                "keys": LocalAPIKeyStore.shared.keys.map { apiKeyDict($0, includeSecret: false) }
            ]
        ]
    }

    private func apiKeyDict(_ key: LocalAPIKey, includeSecret: Bool) -> [String: Any] {
        var output: [String: Any] = [
            "id": key.id,
            "name": key.name,
            "created_at": isoDate(key.createdAt),
            "last_used_at": key.lastUsedAt.map { isoDate($0) } as Any? ?? NSNull(),
            "use_count": key.useCount,
            "enabled": key.isEnabled
        ]
        if includeSecret {
            output["key"] = key.secret
        }
        return output
    }

    private func isoDate(_ date: Date) -> String {
        ISO8601DateFormatter().string(from: date)
    }

    private func countryDict(_ country: CountryPreset) -> [String: Any] {
        [
            "id": country.id,
            "name": country.displayName,
            "api_name": country.apiName,
            "country_code": country.countryCode
        ]
    }

    private func stationDict(_ station: RadioStation) -> [String: Any] {
        var d: [String: Any] = [
            "uuid": station.stationuuid,
            "name": station.name,
            "country": station.country,
            "url": station.urlResolved
        ]
        if let favicon = station.favicon { d["favicon"] = favicon }
        if let homepage = station.homepage { d["homepage"] = homepage }
        if let votes = station.votes { d["votes"] = votes }
        if let clicks = station.clickcount { d["clicks"] = clicks }
        if let tags = station.tags { d["tags"] = tags }
        if let language = station.language { d["language"] = language }
        return d
    }

    private func uniqueStations(_ stations: [RadioStation]) -> [RadioStation] {
        var seen = Set<String>()
        var output: [RadioStation] = []
        for station in stations where !seen.contains(station.stationuuid) {
            seen.insert(station.stationuuid)
            output.append(station)
        }
        return output
    }

    private func countryMatching(_ raw: String) -> CountryPreset? {
        let target = normalize(raw)
        return CountryPreset.all.first { country in
            normalize(country.id) == target
                || normalize(country.countryCode) == target
                || normalize(country.displayName) == target
                || normalize(country.apiName) == target
        }
    }

    private func enumValue<T>(_ raw: String, in values: [T]) -> T? where T: RawRepresentable, T.RawValue == String {
        let target = normalize(raw)
        return values.first { value in
            normalize(value.rawValue) == target
        }
    }

    private func flattenedPreferenceParams(_ params: [String: Any]) -> [String: Any] {
        var merged = params
        if let content = params["content"] as? [String: Any] {
            for (key, value) in content {
                merged[key] = value
            }
        }
        if let playback = params["playback"] as? [String: Any] {
            for (key, value) in playback {
                merged[key] = value
            }
        }
        if let api = params["api"] as? [String: Any] {
            for (key, value) in api {
                merged[key] = value
            }
        }
        return merged
    }

    private func clampedInt(_ value: Any?, defaultValue: Int, minValue: Int, maxValue: Int) -> Int {
        let parsed: Int?
        if let intValue = value as? Int {
            parsed = intValue
        } else if let doubleValue = value as? Double {
            parsed = Int(doubleValue)
        } else if let numberValue = value as? NSNumber {
            parsed = numberValue.intValue
        } else if let stringValue = value as? String {
            parsed = Int(stringValue)
        } else {
            parsed = nil
        }

        return max(minValue, min(maxValue, parsed ?? defaultValue))
    }

    private func doubleValue(_ value: Any?) -> Double? {
        if let doubleValue = value as? Double { return doubleValue }
        if let floatValue = value as? Float { return Double(floatValue) }
        if let intValue = value as? Int { return Double(intValue) }
        if let numberValue = value as? NSNumber { return numberValue.doubleValue }
        if let stringValue = value as? String { return Double(stringValue) }
        return nil
    }

    private func boolValue(_ value: Any?) -> Bool? {
        if let boolValue = value as? Bool { return boolValue }
        if let numberValue = value as? NSNumber { return numberValue.boolValue }
        guard let stringValue = value as? String else { return nil }
        switch stringValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "true", "yes", "1", "on": return true
        case "false", "no", "0", "off": return false
        default: return nil
        }
    }

    private func stringValue(_ value: Any?) -> String? {
        if let stringValue = value as? String { return stringValue }
        if let numberValue = value as? NSNumber { return numberValue.stringValue }
        return nil
    }

    private func normalize(_ raw: String) -> String {
        raw
            .lowercased()
            .replacingOccurrences(of: "&", with: "and")
            .components(separatedBy: CharacterSet.alphanumerics.inverted)
            .joined(separator: " ")
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func json(_ payload: Any) -> HTTPAPIResponse {
        let data = (try? JSONSerialization.data(withJSONObject: payload, options: [.prettyPrinted])) ?? Data()
        return HTTPAPIResponse(status: "200 OK", body: data)
    }

    private func parseHeaders(_ lines: [String]) -> [String: String] {
        var headers: [String: String] = [:]
        for line in lines.dropFirst() {
            if line.isEmpty { break }
            let parts = line.split(separator: ":", maxSplits: 1)
            guard parts.count == 2 else { continue }
            let key = parts[0].trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            let value = parts[1].trimmingCharacters(in: .whitespacesAndNewlines)
            headers[key] = value
        }
        return headers
    }

    private func parseQuery(_ query: String) -> [String: String] {
        var result: [String: String] = [:]
        for pair in query.split(separator: "&") {
            let kv = pair.split(separator: "=", maxSplits: 1)
            if kv.count == 2 {
                result[String(kv[0]).removingPercentEncoding ?? String(kv[0])] =
                    String(kv[1]).removingPercentEncoding ?? String(kv[1])
            }
        }
        return result
    }
}

// MARK: - HTTP Response

private struct HTTPAPIResponse {
    let status: String
    let body: Data

    static let notFound = HTTPAPIResponse(
        status: "404 Not Found",
        body: #"{"ok":false,"error":"not_found"}"#.data(using: .utf8)!
    )

    static let unavailable = HTTPAPIResponse(
        status: "503 Service Unavailable",
        body: #"{"ok":false,"error":"app_not_ready"}"#.data(using: .utf8)!
    )

    static let unauthorized = HTTPAPIResponse(
        status: "401 Unauthorized",
        body: #"{"ok":false,"error":"invalid_api_key"}"#.data(using: .utf8)!
    )

    static let tooManyRequests = HTTPAPIResponse(
        status: "429 Too Many Requests",
        body: #"{"ok":false,"error":"rate_limited"}"#.data(using: .utf8)!
    )

    static func badRequest(_ error: String) -> HTTPAPIResponse {
        let escaped = error.replacingOccurrences(of: #"""#, with: #"\""#)
        return HTTPAPIResponse(
            status: "400 Bad Request",
            body: #"{"ok":false,"error":"\#(escaped)"}"#.data(using: .utf8)!
        )
    }
}

struct LocalAPIConfiguration: Equatable {
    static let defaultPort: UInt16 = 7373

    var enabled: Bool
    var smartPortEnabled: Bool
    var preferredPort: UInt16

    static func load(from defaults: UserDefaults = .standard) -> LocalAPIConfiguration {
        let enabled = defaults.object(forKey: GlassRadioPreferenceKey.apiEnabled) as? Bool ?? true
        let smartPortEnabled = defaults.object(forKey: GlassRadioPreferenceKey.apiSmartPortEnabled) as? Bool ?? true
        let savedPort = defaults.object(forKey: GlassRadioPreferenceKey.apiPreferredPort) as? Int ?? Int(defaultPort)
        let clampedPort = UInt16(max(1024, min(65_535, savedPort)))
        return LocalAPIConfiguration(enabled: enabled, smartPortEnabled: smartPortEnabled, preferredPort: clampedPort)
    }
}

struct LocalAPIKey: Codable, Identifiable, Hashable {
    let id: String
    var name: String
    var secret: String
    var createdAt: Date
    var lastUsedAt: Date?
    var useCount: Int
    var isEnabled: Bool

    var shortSecret: String {
        guard secret.count > 8 else { return secret }
        return "\(secret.prefix(4))...\(secret.suffix(4))"
    }
}

final class LocalAPIKeyStore: ObservableObject {
    static let shared = LocalAPIKeyStore()

    @Published private(set) var keys: [LocalAPIKey]

    private let defaults: UserDefaults

    private init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        self.keys = Self.load(from: defaults)
    }

    @discardableResult
    func createKey(named name: String) -> LocalAPIKey {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let key = LocalAPIKey(
            id: UUID().uuidString,
            name: trimmed.isEmpty ? "New Integration" : trimmed,
            secret: Self.generateSecret(),
            createdAt: Date(),
            lastUsedAt: nil,
            useCount: 0,
            isEnabled: true
        )
        keys.insert(key, at: 0)
        save()
        return key
    }

    func deleteKey(id: String) {
        keys.removeAll { $0.id == id }
        save()
    }

    func setEnabled(_ enabled: Bool, for id: String) {
        guard let index = keys.firstIndex(where: { $0.id == id }) else { return }
        keys[index].isEnabled = enabled
        save()
    }

    func renameKey(id: String, name: String) {
        guard let index = keys.firstIndex(where: { $0.id == id }) else { return }
        keys[index].name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        save()
    }

    @discardableResult
    func recordUse(forSecret secret: String) -> LocalAPIKey? {
        guard let index = keys.firstIndex(where: { $0.secret == secret && $0.isEnabled }) else {
            return nil
        }
        keys[index].lastUsedAt = Date()
        keys[index].useCount += 1
        save()
        return keys[index]
    }

    private func save() {
        guard let data = try? JSONEncoder().encode(keys) else { return }
        defaults.set(data, forKey: GlassRadioPreferenceKey.apiKeys)
    }

    private static func load(from defaults: UserDefaults) -> [LocalAPIKey] {
        guard let data = defaults.data(forKey: GlassRadioPreferenceKey.apiKeys),
              let decoded = try? JSONDecoder().decode([LocalAPIKey].self, from: data) else {
            return []
        }
        return decoded
    }

    private static func generateSecret() -> String {
        let alphabet = Array("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        let token = String((0..<40).map { _ in alphabet.randomElement() ?? "0" })
        return "gr_\(token)"
    }
}
