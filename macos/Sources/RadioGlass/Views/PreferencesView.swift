import SwiftUI

struct PreferencesView: View {
    @EnvironmentObject var viewModel: AppViewModel
    @EnvironmentObject var player: AudioPlayerManager

    private enum PrefTab: String, CaseIterable, Identifiable {
        case playback = "Playback"
        case equalizer = "Equalizer"
        case library = "Library"
        case countries = "Countries"
        case api = "API"
        case content = "Content"
        case about = "About"
        var id: String { rawValue }

        var icon: String {
            switch self {
            case .playback:  return "waveform"
            case .equalizer: return "waveform.path.ecg"
            case .library:   return "antenna.radiowaves.left.and.right"
            case .countries: return "globe"
            case .api:       return "key"
            case .content:   return "slider.horizontal.3"
            case .about:     return "info.circle"
            }
        }
    }

    @State private var selectedTab: PrefTab = .playback

    var body: some View {
        TabView(selection: $selectedTab) {
            PlaybackPrefsPane()
                .tabItem { Label(PrefTab.playback.rawValue, systemImage: PrefTab.playback.icon) }
                .tag(PrefTab.playback)

            EQPrefsPane(eq: player.eq)
                .tabItem { Label(PrefTab.equalizer.rawValue, systemImage: PrefTab.equalizer.icon) }
                .tag(PrefTab.equalizer)

            LibraryPrefsPane()
                .tabItem { Label(PrefTab.library.rawValue, systemImage: PrefTab.library.icon) }
                .tag(PrefTab.library)

            CountriesPrefsPane()
                .tabItem { Label(PrefTab.countries.rawValue, systemImage: PrefTab.countries.icon) }
                .tag(PrefTab.countries)

            APIPrefsPane()
                .tabItem { Label(PrefTab.api.rawValue, systemImage: PrefTab.api.icon) }
                .tag(PrefTab.api)

            ContentPrefsPane()
                .tabItem { Label(PrefTab.content.rawValue, systemImage: PrefTab.content.icon) }
                .tag(PrefTab.content)

            AboutPane()
                .tabItem { Label(PrefTab.about.rawValue, systemImage: PrefTab.about.icon) }
                .tag(PrefTab.about)
        }
        .frame(width: 700, height: 580)
    }
}

// MARK: - Playback

private struct PlaybackPrefsPane: View {
    @AppStorage("GlassRadio.DefaultVolume") private var defaultVolume: Double = 0.8
    @AppStorage("GlassRadio.PreferLowBitrate") private var preferLowBitrate: Bool = false
    @AppStorage("GlassRadio.ConnectTimeoutSeconds") private var connectTimeout: Double = 12

    var body: some View {
        Form {
            Section("Volume") {
                HStack {
                    Image(systemName: "speaker.fill")
                        .foregroundStyle(.secondary)
                        .frame(width: 20)
                    Slider(value: $defaultVolume, in: 0...1)
                    Image(systemName: "speaker.wave.3.fill")
                        .foregroundStyle(.secondary)
                        .frame(width: 20)
                    Text("\(Int(defaultVolume * 100))%")
                        .monospacedDigit()
                        .frame(width: 38, alignment: .trailing)
                }
            }

            Section("Stream Quality") {
                Toggle("Prefer lower bitrate on slow connections", isOn: $preferLowBitrate)
                    .help("When enabled, Glass Radio chooses a lower-bitrate stream when your network is constrained or expensive.")

                HStack {
                    Text("Connection timeout")
                    Spacer()
                    Slider(value: $connectTimeout, in: 6...30, step: 1)
                        .frame(width: 140)
                    Text("\(Int(connectTimeout))s")
                        .monospacedDigit()
                        .frame(width: 28, alignment: .trailing)
                }
            }
        }
        .formStyle(.grouped)
        .padding(.vertical, 8)
    }
}

// MARK: - Library

private struct LibraryPrefsPane: View {
    @AppStorage("GlassRadio.SelectedCountryID") private var defaultCountryID: String = "uk"
    @EnvironmentObject var viewModel: AppViewModel
    @State private var showClearConfirm = false

    var body: some View {
        Form {
            Section("Default Country") {
                Picker("Open Glass Radio to:", selection: $defaultCountryID) {
                    ForEach(CountryPreset.all) { country in
                        Text(country.displayName).tag(country.id)
                    }
                }
                .pickerStyle(.menu)
            }

            Section("Station Library") {
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Cached Countries")
                        Text("\(viewModel.cachedCountryCount) of \(CountryPreset.all.count) countries have station data cached.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    if viewModel.isBackgroundRefreshing {
                        ProgressView().scaleEffect(0.75)
                    }
                    Button("Refresh All") {
                        viewModel.forceRefreshAllCountries()
                    }
                    .disabled(viewModel.isBackgroundRefreshing)
                }
            }

            Section("Cache") {
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Station Logo Cache")
                        Text("Logos are stored locally to reduce network requests.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button(role: .destructive) {
                        showClearConfirm = true
                    } label: {
                        Text("Clear Cache")
                    }
                }

                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Reset Station Library")
                        Text("Clears all cached station data. Restarts onboarding on next launch.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button(role: .destructive) {
                        UserDefaults.standard.removeObject(forKey: "GlassRadio.InitialCacheComplete")
                        UserDefaults.standard.removeObject(forKey: "GlassRadio.FailedInitialCacheCounts")
                        UserDefaults.standard.removeObject(forKey: "GlassRadio.SelectedCountryID")
                        clearStationSnapshots()
                    } label: {
                        Text("Reset Library")
                    }
                }
            }
        }
        .formStyle(.grouped)
        .padding(.vertical, 8)
        .confirmationDialog(
            "Clear all cached station logos?",
            isPresented: $showClearConfirm,
            titleVisibility: .visible
        ) {
            Button("Clear Cache", role: .destructive) {
                clearLogoCache()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Logos will be re-downloaded as you browse.")
        }
    }

    private func clearLogoCache() {
        let logosDir = AppAssetStore.stationLogosDirectory
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: logosDir, includingPropertiesForKeys: nil) else { return }
        for file in files {
            try? fm.removeItem(at: file)
        }
    }

    private func clearStationSnapshots() {
        let snapshotsDir = AppAssetStore.stationSnapshotsDirectory
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: snapshotsDir, includingPropertiesForKeys: nil) else { return }
        for file in files {
            try? fm.removeItem(at: file)
        }
    }
}

// MARK: - Equalizer

private struct EQPrefsPane: View {
    @ObservedObject var eq: AudioEQProcessor

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text("8-Band Equalizer")
                        .font(.callout.weight(.semibold))
                    Text("Shape the audio character of your stream in real time.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Toggle("Enable EQ", isOn: $eq.isEnabled)
                    .toggleStyle(.switch)
                    .controlSize(.small)
            }
            .padding(.horizontal, 24)
            .padding(.top, 20)
            .padding(.bottom, 16)

            Divider()

            EQView(eq: eq)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 8)

            Spacer(minLength: 0)
        }
    }
}

// MARK: - Countries

private func flagEmoji(for code: String) -> String {
    let base: UInt32 = 0x1F1E6
    let scalars = code.uppercased().unicodeScalars.compactMap { scalar -> UnicodeScalar? in
        let value = scalar.value
        guard value >= 65 && value <= 90 else { return nil }
        return UnicodeScalar(base + (value - 65))
    }
    return String(String.UnicodeScalarView(scalars))
}

private struct CountriesPrefsPane: View {
    @EnvironmentObject var viewModel: AppViewModel
    @State private var searchText = ""

    private var filteredCountries: [CountryPreset] {
        guard !searchText.isEmpty else { return CountryPreset.all }
        return CountryPreset.all.filter { $0.displayName.localizedCaseInsensitiveContains(searchText) }
    }

    var body: some View {
        VStack(spacing: 0) {
            // Summary header
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Country Cache")
                        .font(.callout.weight(.semibold))
                    Text("\(viewModel.cachedCountryCount) of \(CountryPreset.all.count) countries cached")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                if viewModel.isBackgroundRefreshing {
                    HStack(spacing: 6) {
                        ProgressView().scaleEffect(0.7)
                        Text(viewModel.backgroundRefreshMessage)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }
                Button("Refresh All") {
                    viewModel.forceRefreshAllCountries()
                }
                .disabled(viewModel.isBackgroundRefreshing)
            }
            .padding(.horizontal, 20)
            .padding(.top, 16)
            .padding(.bottom, 10)

            // Search bar
            HStack(spacing: 7) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("Search countries…", text: $searchText).textFieldStyle(.plain)
                if !searchText.isEmpty {
                    Button { searchText = "" } label: {
                        Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(8)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
            .padding(.horizontal, 20)
            .padding(.bottom, 8)

            Divider()

            ScrollView {
                LazyVStack(spacing: 0) {
                    ForEach(filteredCountries) { country in
                        CountryCacheRow(country: country)
                        if country.id != filteredCountries.last?.id {
                            Divider().padding(.leading, 56)
                        }
                    }
                }
                .padding(.vertical, 4)
            }
        }
    }
}

private struct CountryCacheRow: View {
    @EnvironmentObject var viewModel: AppViewModel
    let country: CountryPreset

    private var isCached: Bool { AppAssetStore.hasStationSnapshot(countryID: country.id) }
    private var isInProgress: Bool { viewModel.fullPullInProgress.contains(country.id) }

    var body: some View {
        HStack(spacing: 10) {
            Text(flagEmoji(for: country.countryCode))
                .font(.title3)
                .frame(width: 28)
            Text(country.displayName)
                .font(.callout)
                .foregroundStyle(.primary)
            Spacer()
            if isInProgress {
                ProgressView().scaleEffect(0.7)
                Text("Fetching…")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                Circle()
                    .fill(isCached ? Color.green : Color.secondary.opacity(0.4))
                    .frame(width: 8, height: 8)
                Text(isCached ? "Cached" : "Not cached")
                    .font(.caption.weight(.medium))
                    .foregroundStyle(isCached ? Color.green : .secondary)
                    .frame(width: 72, alignment: .leading)
            }
            Button(isCached ? "Refresh" : "Cache Now") {
                Task { await viewModel.triggerFullPull(for: country) }
            }
            .buttonStyle(.bordered)
            .controlSize(.small)
            .disabled(isInProgress || viewModel.isBackgroundRefreshing)
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 8)
        .contentShape(Rectangle())
    }
}

// MARK: - API

private struct APIPrefsPane: View {
    @AppStorage(GlassRadioPreferenceKey.apiEnabled) private var apiEnabled = true
    @AppStorage(GlassRadioPreferenceKey.apiSmartPortEnabled) private var smartPortEnabled = true
    @AppStorage(GlassRadioPreferenceKey.apiPreferredPort) private var preferredPort = Int(LocalAPIConfiguration.defaultPort)
    @AppStorage(GlassRadioPreferenceKey.apiActivePort) private var activePort = Int(LocalAPIConfiguration.defaultPort)
    @ObservedObject private var keyStore = LocalAPIKeyStore.shared
    @State private var newKeyName = ""

    private var isRunning: Bool { apiEnabled && activePort > 0 }
    private var activeURL: String {
        guard isRunning else { return "Not running" }
        return "http://127.0.0.1:\(activePort)"
    }

    var body: some View {
        Form {
            Section("Server") {
                HStack(spacing: 8) {
                    Circle()
                        .fill(isRunning ? Color.green : Color.secondary.opacity(0.4))
                        .frame(width: 8, height: 8)
                    Text(isRunning ? "Running on port \(activePort)" : "Stopped")
                        .font(.callout.weight(.medium))
                        .foregroundStyle(isRunning ? .primary : .secondary)
                }

                Toggle("Enable local API server", isOn: $apiEnabled)
                    .help("Allows trusted local tools to control Glass Radio with an API key.")

                Toggle("Choose an available port automatically", isOn: $smartPortEnabled)
                    .help("Glass Radio tries your preferred port first, then nearby available ports.")
                    .disabled(!apiEnabled)

                HStack {
                    Text(smartPortEnabled ? "Preferred port" : "Port")
                    Spacer()
                    Stepper(value: $preferredPort, in: 1024...65_535) {
                        Text("\(preferredPort)")
                            .monospacedDigit()
                            .frame(width: 58, alignment: .trailing)
                    }
                    .disabled(!apiEnabled)
                }

                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Active URL")
                        Text(activeURL)
                            .font(.caption.monospaced())
                            .foregroundStyle(isRunning ? .secondary : .tertiary)
                    }
                    Spacer()
                    Button("Copy") {
                        copyToPasteboard(activeURL)
                    }
                    .disabled(!isRunning)
                }
            }

            Section {
                Text("Requests require an API key. All communication stays on your local network.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Section("API Keys") {
                HStack {
                    TextField("Name (e.g. Home Dashboard)", text: $newKeyName)
                    Button("Generate Key") {
                        let key = keyStore.createKey(named: newKeyName)
                        newKeyName = ""
                        copyToPasteboard(key.secret)
                    }
                    .disabled(newKeyName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !apiEnabled)
                }
                .help("The generated key is automatically copied to your clipboard.")

                if keyStore.keys.isEmpty {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("No keys configured")
                            .font(.callout.weight(.medium))
                            .foregroundStyle(.secondary)
                        Text("Create a key for each dashboard, shortcut, or external app that should control Glass Radio. Send it in the X-Glass-Radio-Key header.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                } else {
                    ForEach(keyStore.keys) { key in
                        APIKeyRow(key: key, keyStore: keyStore)
                    }
                }
            }

            Section("Endpoints") {
                EndpointRow(label: "Metrics", endpoint: "GET /metrics")
                EndpointRow(label: "Stations", endpoint: "GET /stations")
                EndpointRow(label: "Preferences", endpoint: "GET/POST /preferences")
            }
        }
        .formStyle(.grouped)
        .padding(.vertical, 8)
    }

    private func copyToPasteboard(_ value: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(value, forType: .string)
    }
}

private struct APIKeyRow: View {
    let key: LocalAPIKey
    @ObservedObject var keyStore: LocalAPIKeyStore

    var body: some View {
        HStack(alignment: .center, spacing: 10) {
            Toggle("", isOn: Binding(
                get: { key.isEnabled },
                set: { keyStore.setEnabled($0, for: key.id) }
            ))
            .labelsHidden()

            VStack(alignment: .leading, spacing: 2) {
                Text(key.name)
                HStack(spacing: 8) {
                    Text(key.shortSecret)
                        .font(.caption.monospaced())
                    Text("Used \(key.useCount)")
                        .font(.caption)
                    if let lastUsed = key.lastUsedAt {
                        Text(lastUsed, style: .relative)
                            .font(.caption)
                    }
                }
                .foregroundStyle(.secondary)
            }

            Spacer()

            Button("Copy") {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(key.secret, forType: .string)
            }

            Button(role: .destructive) {
                keyStore.deleteKey(id: key.id)
            } label: {
                Image(systemName: "trash")
            }
            .buttonStyle(.borderless)
            .help("Delete API key")
        }
    }
}

private struct EndpointRow: View {
    let label: String
    let endpoint: String

    var body: some View {
        HStack {
            Text(label)
                .foregroundStyle(.secondary)
            Spacer()
            Text(endpoint)
                .font(.caption.monospaced())
                .foregroundStyle(.secondary)
        }
    }
}

// MARK: - Content

private struct ContentPrefsPane: View {
    @AppStorage(GlassRadioPreferenceKey.showReligiousContent) private var showReligiousContent = true
    @AppStorage(GlassRadioPreferenceKey.showSportsContent) private var showSportsContent = true
    @AppStorage(GlassRadioPreferenceKey.showNewsContent) private var showNewsContent = true

    var body: some View {
        Form {
            Section("Station Categories") {
                ContentToggleRow(
                    title: "Religion",
                    detail: "Allow religious stations in browse and search.",
                    systemImage: "building.columns",
                    isOn: $showReligiousContent
                )
                .help("Top Stations never promotes religious stations. This controls the broader station catalog and search results.")

                ContentToggleRow(
                    title: "Football and Sports",
                    detail: "Allow sport, football, match-day, and sports talk stations.",
                    systemImage: "sportscourt",
                    isOn: $showSportsContent
                )
                .help("Turn this off to hide sport, football, match-day, and talkSPORT-style stations from browse and search.")

                ContentToggleRow(
                    title: "News",
                    detail: "Allow news-led stations in browse and search.",
                    systemImage: "newspaper",
                    isOn: $showNewsContent
                )
                .help("Turn this off to hide news-focused stations from browse and search.")
            }

            Section("Top Stations") {
                LabeledContent {
                    Text("Always hidden")
                        .foregroundStyle(.secondary)
                } label: {
                    Label("Religious promotion", systemImage: "star.slash")
                }
                Text("Top Stations remains curated and family-neutral. The larger catalog below it follows these category choices.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .formStyle(.grouped)
        .padding(.vertical, 8)
    }
}

private struct ContentToggleRow: View {
    let title: String
    let detail: String
    let systemImage: String
    @Binding var isOn: Bool

    var body: some View {
        Toggle(isOn: $isOn) {
            Label {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                    Text(detail)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            } icon: {
                Image(systemName: systemImage)
                    .foregroundStyle(.secondary)
                    .frame(width: 20)
            }
        }
    }
}

// MARK: - About

private struct AboutPane: View {
    var body: some View {
        VStack(spacing: 20) {
            Image(nsImage: NSApplication.shared.applicationIconImage)
                .resizable()
                .scaledToFit()
                .frame(width: 72, height: 72)
                .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                .shadow(color: .black.opacity(0.2), radius: 8, x: 0, y: 4)

            VStack(spacing: 4) {
                Text("Glass Radio")
                    .font(.title2.weight(.semibold))
                Text("Version \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "2.0")")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Text("Global radio, curated and refined.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Divider()

            VStack(spacing: 4) {
                Text("Station data provided by radio-browser.info")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Text("Built with SwiftUI · Universal Binary · macOS 13+")
                    .font(.caption2)
                    .foregroundStyle(Color.secondary.opacity(0.7))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(24)
    }
}
