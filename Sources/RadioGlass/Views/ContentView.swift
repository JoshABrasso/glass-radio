import AppKit
import SwiftUI

struct ContentView: View {
    enum Tab: String, CaseIterable, Identifiable {
        case countries = "Country Picks"
        case search = "Search"
        case discover = "Discover"
        var id: String { rawValue }
    }

    @EnvironmentObject var viewModel: AppViewModel
    @State private var tab: Tab = .countries
    @State private var isEditingPresets = false

    var body: some View {
        ZStack(alignment: .bottomLeading) {
            NavigationSplitView {
                leftSidebar
                    .navigationSplitViewColumnWidth(min: 180, ideal: 220, max: 320)
            } content: {
                centerContent
                    .navigationSplitViewColumnWidth(min: 560, ideal: 980, max: 2600)
            } detail: {
                rightCountryPanel
                    .navigationSplitViewColumnWidth(min: 220, ideal: 260, max: 380)
            }
            .navigationSplitViewStyle(.balanced)
            .disabled(viewModel.isInitialLoading)
            .background(
                WindowConfigurator { window in
                    window.isMovableByWindowBackground = true
                    window.isMovable = true
                    window.styleMask = [.titled, .resizable, .miniaturizable, .closable]
                    window.titleVisibility = .visible
                    window.titlebarAppearsTransparent = true
                }
            )
            .searchable(text: $viewModel.quickSearchText, placement: .toolbar, prompt: tab == .countries ? "Quick search stations" : "Quick search") {
                ForEach(viewModel.quickSearchResults.prefix(8)) { station in
                    SearchSuggestionRow(station: station) {
                        viewModel.play(station, in: viewModel.quickSearchResults)
                    }
                }
            }
            .onChange(of: viewModel.quickSearchText) { _ in
                viewModel.scheduleQuickSearch()
            }
            .toolbarBackground(.clear, for: .windowToolbar)
            .onSubmit(of: .search) {
                Task {
                    await viewModel.commitQuickSearch()
                }
            }
            .safeAreaInset(edge: .bottom) {
                GeometryReader { geo in
                    HStack {
                        Spacer(minLength: 0)
                        PlayerBarView(player: viewModel.player)
                            .frame(maxWidth: min(920, geo.size.width - 120))
                        Spacer(minLength: 0)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.horizontal, 24)
                    .padding(.bottom, 8)
                }
                .frame(height: 58)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .ignoresSafeArea(.container, edges: .horizontal)
            .appBackground()

            if viewModel.isBackgroundRefreshing {
                BackgroundRefreshBadge(
                    message: viewModel.backgroundRefreshMessage,
                    progress: viewModel.backgroundRefreshProgress
                )
                .padding(.leading, 22)
                .padding(.bottom, 72)
            }

            if viewModel.isInitialLoading {
                InitialLoadingView(
                    message: viewModel.initialLoadingMessage,
                    progress: viewModel.initialLoadingProgress
                )
                .transition(.opacity)
                .zIndex(5)
            }
        }
    }

    private var presetsEditButton: some View {
        Button(isEditingPresets ? "Done" : "Reorder") {
            isEditingPresets.toggle()
        }
        .buttonStyle(.plain)
        .font(.callout.weight(.semibold))
        .foregroundStyle(.white.opacity(0.72))
    }

    private var leftSidebar: some View {
        List {
            Section {
                Text("Library")
                    .font(.callout.weight(.semibold))
                    .foregroundStyle(.white.opacity(0.62))
            }

            Section {
                sidebarItem("Country Picks", tab: .countries, icon: "globe")
                sidebarItem("Search", tab: .search, icon: "magnifyingglass")
                sidebarItem("Discover", tab: .discover, icon: "sparkles")
            }

            Section {
                HStack {
                    Text("Preset Stations")
                        .font(.callout.weight(.semibold))
                        .foregroundStyle(.white.opacity(0.82))
                    Spacer()
                    presetsEditButton
                }
            }

            Section {
                if viewModel.presets.isEmpty {
                    Text("Star stations to add presets")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(viewModel.presets) { station in
                        Button {
                            viewModel.play(station, in: viewModel.presets)
                        } label: {
                            HStack(spacing: 6) {
                                StationArtworkView(station: station, cornerRadius: 4)
                                    .frame(width: 18, height: 18)

                                Text(station.name)
                                    .font(.callout.weight(.semibold))
                                    .lineLimit(1)

                                Spacer()

                                if viewModel.isCurrentStation(station) {
                                    Image(systemName: "speaker.wave.2.fill")
                                        .foregroundStyle(.red)
                                        .font(.callout)
                                }
                            }
                        }
                        .buttonStyle(.plain)
                    }
                    .onMove { indices, newOffset in
                        viewModel.movePresets(from: indices, to: newOffset)
                    }
                    .moveDisabled(!isEditingPresets)
                }
            }
        }
        .environment(\.defaultMinListRowHeight, 26)
        .listStyle(.sidebar)
        .scrollContentBackground(.hidden)
        .background(Color.black.opacity(0.30))
    }

    private func sidebarItem(_ title: String, tab: Tab, icon: String) -> some View {
        Button {
            self.tab = tab
        } label: {
            HStack(spacing: 7) {
                Image(systemName: icon)
                    .font(.callout)
                Text(title)
                    .font(.callout.weight(.semibold))
                Spacer()
            }
            .foregroundStyle(self.tab == tab ? Color.red : .white.opacity(0.86))
        }
        .buttonStyle(.plain)
    }

    private var centerContent: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(tabTitle)
                .font(.headline)
                .foregroundStyle(.white.opacity(0.88))
                .padding(.top, 8)
                .padding(.horizontal, 16)

            switch tab {
            case .countries:
                countriesMainView
            case .search:
                ScrollView {
                    searchPageView
                        .padding(.horizontal, 16)
                        .padding(.bottom, 18)
                }
            case .discover:
                ScrollView {
                    discoverView
                        .padding(.horizontal, 16)
                        .padding(.bottom, 18)
                }
            }
        }
        .background(Color.black.opacity(0.12))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var tabTitle: String {
        switch tab {
        case .countries: return "\(viewModel.selectedCountry.displayName) Stations"
        case .search: return "Search"
        case .discover: return "Discover"
        }
    }

    private var countriesMainView: some View {
        GeometryReader { geo in
            let topHeight = max(240, geo.size.height * 0.48)

            VStack(alignment: .leading, spacing: 12) {
                if let error = viewModel.errorMessage {
                    Text(error)
                        .font(.caption2)
                        .foregroundStyle(.red)
                        .padding(.horizontal, 16)
                }

                if let info = viewModel.countryInfoMessage {
                    Text(info)
                        .font(.caption2)
                        .foregroundStyle(.white.opacity(0.62))
                        .padding(.horizontal, 16)
                }

                VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Text("Top Stations In \(viewModel.selectedCountry.displayName)")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.white.opacity(0.88))
                
                Text("\(viewModel.countryStationCount) stations")
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.58))

                        Spacer()

                        Picker("Filter", selection: $viewModel.selectedFilter) {
                            ForEach(AppViewModel.StationFilter.allCases) { filter in
                                Text(filter.rawValue).tag(filter)
                            }
                        }
                        .labelsHidden()
                        .pickerStyle(.menu)

                        Picker("Sort", selection: $viewModel.selectedSort) {
                            ForEach(AppViewModel.StationSort.allCases) { sort in
                                Text(sort.rawValue).tag(sort)
                            }
                        }
                        .labelsHidden()
                        .pickerStyle(.menu)
                    }
                    .padding(.horizontal, 16)

                    ScrollView {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 168), spacing: 14)], spacing: 16) {
                ForEach(viewModel.filteredCountryStations) { station in
                    FeaturedStationCardView(
                        station: station,
                        onPlay: { viewModel.play(station, in: viewModel.filteredCountryStations) },
                        onTogglePreset: { viewModel.togglePreset(station) },
                        isPreset: viewModel.isPreset(station)
                                )
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.bottom, 8)
                    }
                }
                .frame(height: topHeight)

                VStack(alignment: .leading, spacing: 8) {
                    Text("Stations by Genre")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.white.opacity(0.88))
                        .padding(.horizontal, 16)

                    GeometryReader { geo in
                        let buttons = viewModel.genreButtons
                        let count = max(1, buttons.count)
                        let spacing: CGFloat = 8
                        let available = max(0, geo.size.width - 32)
                        let rawWidth = (available - (spacing * CGFloat(count - 1))) / CGFloat(count)
                        let buttonWidth = min(160, max(110, rawWidth))

                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack {
                                Spacer(minLength: 0)
                                HStack(spacing: spacing) {
                                    ForEach(buttons, id: \.self) { genre in
                                        Button {
                                            viewModel.setGenre(genre)
                                        } label: {
                                            Text(genre)
                                                .font(.caption)
                                                .foregroundStyle(.white)
                                                .lineLimit(1)
                                                .truncationMode(.tail)
                                                .minimumScaleFactor(0.85)
                                                .frame(width: buttonWidth, height: 28)
                                                .background(
                                                    viewModel.selectedGenre == genre ? Color.red.opacity(0.28) : Color.white.opacity(0.08),
                                                    in: Capsule()
                                                )
                                        }
                                        .buttonStyle(.plain)
                                    }
                                }
                                Spacer(minLength: 0)
                            }
                            .padding(.horizontal, 16)
                        }
                    }
                    .frame(height: 54)

                    GeometryReader { geo in
                        ScrollView {
                            let width = geo.size.width
                            let columnCount: Int = {
                                switch width {
                                case 0..<1100:
                                    return 1
                                case 1100..<1500:
                                    return 2
                                case 1500..<1900:
                                    return 3
                                default:
                                    return 4
                                }
                            }()

                            if columnCount > 1 {
                                let columns = Array(repeating: GridItem(.flexible(minimum: 320), spacing: 12), count: columnCount)
                                LazyVGrid(columns: columns, spacing: 8) {
                                    ForEach(viewModel.genreFilteredStations) { station in
                                    CountryStationRowView(
                                        station: station,
                                        isPreset: viewModel.isPreset(station),
                                        onPlay: { viewModel.play(station, in: viewModel.genreFilteredStations) },
                                        onTogglePreset: { viewModel.togglePreset(station) }
                                    )
                                }
                                }
                            } else {
                                LazyVStack(spacing: 6) {
                                    ForEach(viewModel.genreFilteredStations) { station in
                                    CountryStationRowView(
                                        station: station,
                                        isPreset: viewModel.isPreset(station),
                                        onPlay: { viewModel.play(station, in: viewModel.genreFilteredStations) },
                                        onTogglePreset: { viewModel.togglePreset(station) }
                                    )
                                }
                                }
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.bottom, 12)
                    }
                }
                .frame(maxHeight: .infinity)
            }
        }
    }

    private var searchPageView: some View {
        let brandClusters = viewModel.brandClusters(for: viewModel.searchPageQuery)
        let topByCountry = viewModel.topStationsByCountryPreview(maxCountries: 12, perCountry: 1)

        return ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                SearchGlassCard(title: "Search Stations", subtitle: "Explore by name, city, or frequency") {
                    HStack(spacing: 10) {
                        Image(systemName: "magnifyingglass")
                            .foregroundStyle(.white.opacity(0.6))
                        TextField("Search by station, city, or frequency", text: $viewModel.searchPageQuery)
                            .textFieldStyle(.plain)
                            .foregroundStyle(.white)
                            .onChange(of: viewModel.searchPageQuery) { _ in
                                viewModel.scheduleSearchPageQuery()
                            }
                        if !viewModel.searchPageQuery.isEmpty {
                            Button {
                                viewModel.searchPageQuery = ""
                                viewModel.searchPageResults = []
                            } label: {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundStyle(.white.opacity(0.6))
                            }
                            .buttonStyle(.plain)
                        }
                        Button("Search") {
                            Task { await viewModel.performSearchPageQuery() }
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(.red.opacity(0.75))
                    }
                    .padding(12)
                    .background(
                        RoundedRectangle(cornerRadius: 12, style: .continuous)
                            .fill(Color.white.opacity(0.08))
                            .overlay(
                                RoundedRectangle(cornerRadius: 12, style: .continuous)
                                    .stroke(Color.white.opacity(0.12), lineWidth: 1)
                            )
                    )

                    HStack(spacing: 8) {
                        Text("Suggestions")
                            .font(.caption2)
                            .foregroundStyle(.white.opacity(0.55))
                        ForEach(["BBC Radio", "Classic Rock", "Paris", "90s", "Lo-Fi"], id: \.self) { suggestion in
                            SearchSuggestionChip(title: suggestion) {
                                viewModel.searchPageQuery = suggestion
                                Task { await viewModel.performSearchPageQuery() }
                            }
                        }
                    }
                }

                // Primary live search results
                if !viewModel.searchPageQuery.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    if !brandClusters.isEmpty {
                        SearchGlassCard(title: "Station Channels", subtitle: "Related sub-brands and streams") {
                            ScrollView(.horizontal, showsIndicators: false) {
                                HStack(spacing: 12) {
                                    ForEach(brandClusters) { cluster in
                                        SearchBrandCard(cluster: cluster) { station in
                                            viewModel.play(station, in: cluster.stations)
                                        }
                                    }
                                }
                                .padding(.vertical, 4)
                            }
                        }
                    }

                    SearchGlassCard(title: "Search Results", subtitle: "Results for \"\(viewModel.searchPageQuery)\"") {
                        LazyVStack(spacing: 6) {
                            ForEach(viewModel.searchPageResults) { station in
                                CountryStationRowView(
                                    station: station,
                                    isPreset: viewModel.isPreset(station),
                                    onPlay: { viewModel.play(station, in: viewModel.searchPageResults) },
                                    onTogglePreset: { viewModel.togglePreset(station) }
                                )
                            }
                        }
                    }
                }

                SearchGlassCard(title: "Browse Categories", subtitle: "Tap a vibe") {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 180), spacing: 12)], spacing: 12) {
                        ForEach(Array(viewModel.genreButtons.prefix(18)).indices, id: \.self) { index in
                            let genre = viewModel.genreButtons[index]
                            SearchCategoryTile(title: genre, index: index) {
                                viewModel.setGenre(genre)
                                viewModel.searchPageQuery = genre
                                Task { await viewModel.performSearchPageQuery() }
                            }
                        }
                    }
                }

                ForEach([AppViewModel.StationFilter.majorBrands, .withArtwork, .withGenres, .presetsOnly]) { filter in
                    let stations = viewModel.stationsForFilter(filter, limit: 10)
                    if !stations.isEmpty {
                        SearchGlassCard(title: filter.rawValue, subtitle: "A focused slice of the catalog") {
                            ScrollView(.horizontal, showsIndicators: false) {
                                HStack(spacing: 12) {
                                    ForEach(stations) { station in
                                        FeaturedStationCardView(
                                            station: station,
                                            onPlay: { viewModel.play(station, in: stations) },
                                            onTogglePreset: { viewModel.togglePreset(station) },
                                            isPreset: viewModel.isPreset(station)
                                        )
                                        .frame(width: 170)
                                    }
                                }
                                .padding(.vertical, 2)
                            }
                        }
                    }
                }

                SearchGlassCard(title: "Explore Countries", subtitle: "Jump into a country’s scene") {
                    let countryColumns = [GridItem(.adaptive(minimum: 180, maximum: 180), spacing: 12, alignment: .topLeading)]
                    LazyVGrid(columns: countryColumns, spacing: 12) {
                        ForEach(CountryPreset.all) { country in
                            SearchCountryCard(
                                title: country.displayName,
                                code: country.countryCode
                            ) {
                                Task { await viewModel.loadCountry(country) }
                                tab = .countries
                            }
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }

                if !topByCountry.isEmpty {
                    SearchGlassCard(title: "Top Stations Worldwide", subtitle: "A quick tour of global favorites") {
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 12) {
                                ForEach(topByCountry, id: \.0.id) { entry in
                                    if let station = entry.1.first {
                                        SearchCountryTopCard(
                                            country: entry.0.displayName,
                                            station: station
                                        ) {
                                            viewModel.play(station, in: entry.1)
                                        }
                                    }
                                }
                            }
                            .padding(.vertical, 4)
                        }
                    }
                }

                if !viewModel.quickSearchResults.isEmpty {
                    SearchGlassCard(title: "Quick Search Results", subtitle: "From the top-right quick search") {
                        LazyVStack(spacing: 6) {
                            ForEach(viewModel.quickSearchResults) { station in
                                CountryStationRowView(
                                    station: station,
                                    isPreset: viewModel.isPreset(station),
                                    onPlay: { viewModel.play(station, in: viewModel.quickSearchResults) },
                                    onTogglePreset: { viewModel.togglePreset(station) }
                                )
                            }
                        }
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 10)
            .padding(.bottom, 240) // extra clearance so the last row stays above the player bar
        }
    }

    private var discoverView: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 172), spacing: 14)], spacing: 16) {
            ForEach(viewModel.discoverStations) { station in
                FeaturedStationCardView(
                    station: station,
                    onPlay: { viewModel.play(station, in: viewModel.discoverStations) },
                    onTogglePreset: { viewModel.togglePreset(station) },
                    isPreset: viewModel.isPreset(station)
                )
            }
        }
    }

    private var rightCountryPanel: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Countries")
                    .font(.callout.weight(.semibold))
                    .foregroundStyle(.white)
                Spacer()
            }
            .padding(.horizontal, 10)
            .padding(.top, 8)

            ScrollView {
                LazyVStack(spacing: 4) {
                    ForEach(CountryPreset.all) { country in
                        Button {
                            Task { await viewModel.loadCountry(country) }
                            tab = .countries
                        } label: {
                            HStack {
                                Text("\(flagEmoji(for: country.countryCode)) \(country.displayName)")
                                    .font(.callout.weight(.semibold))
                                    .lineLimit(1)
                                Spacer()
                            }
                            .foregroundStyle(viewModel.selectedCountry == country ? Color.red : .white.opacity(0.86))
                            .padding(.horizontal, 8)
                            .padding(.vertical, 6)
                            .background(
                                viewModel.selectedCountry == country ? Color.white.opacity(0.08) : .clear,
                                in: RoundedRectangle(cornerRadius: 6)
                            )
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(.horizontal, 8)
            }

            Divider()
                .overlay(Color.white.opacity(0.08))

            Text("Top In \(viewModel.selectedCountry.displayName)")
                .font(.callout.weight(.semibold))
                .foregroundStyle(.white)
                .padding(.horizontal, 10)

            ScrollView {
                LazyVStack(spacing: 4) {
                    ForEach(Array(viewModel.countryTopStations.prefix(20))) { station in
                        Button {
                            viewModel.play(station, in: viewModel.filteredCountryStations)
                        } label: {
                            HStack(spacing: 6) {
                                StationArtworkView(station: station, cornerRadius: 4)
                                    .frame(width: 26, height: 26)

                                Text(station.name)
                                    .font(.callout.weight(.semibold))
                                    .lineLimit(1)
                                Spacer()
                            }
                            .foregroundStyle(.white.opacity(0.88))
                            .padding(.horizontal, 8)
                            .padding(.vertical, 5)
                            .background(
                                viewModel.isCurrentStation(station) ? Color.white.opacity(0.08) : .clear,
                                in: RoundedRectangle(cornerRadius: 6)
                            )
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(.horizontal, 8)
                .padding(.bottom, 10)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(Color.black.opacity(0.22))
    }
}

private struct InitialLoadingView: View {
    let message: String
    let progress: Double
    @State private var paddleX: CGFloat = 0.5
    @State private var gameActive = false

    var body: some View {
        ZStack {
            Color.black.opacity(0.55)
                .ignoresSafeArea()
                .background(.ultraThinMaterial)

            VStack(spacing: 16) {
                InitialLoadingHeader(message: message, progress: progress)
                    .frame(maxWidth: .infinity)

                if gameActive {
                    MiniPongView(paddleX: $paddleX, isActive: gameActive)
                        .overlay(
                            Text("Use ← and → to move")
                                .font(.caption2)
                                .foregroundStyle(.white.opacity(0.7))
                                .padding(.horizontal, 10)
                                .padding(.vertical, 6)
                                .background(
                                    Capsule().fill(Color.black.opacity(0.35))
                                )
                                .padding(.bottom, 8),
                            alignment: .bottom
                        )
                } else {
                    VStack(spacing: 12) {
                        Text("Do you want to play a game while you wait?")
                            .font(.headline.weight(.semibold))
                            .foregroundStyle(.white)
                        Button("Start Game") {
                            gameActive = true
                        }
                        .buttonStyle(.borderedProminent)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .padding(.vertical, 40)
                    .background(
                        RoundedRectangle(cornerRadius: 18, style: .continuous)
                            .fill(.ultraThinMaterial)
                            .overlay(
                                RoundedRectangle(cornerRadius: 18, style: .continuous)
                                    .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                            )
                    )
                }
            }
            .padding(.horizontal, 18)
            .padding(.top, 16)
            .padding(.bottom, 20)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .background(
                KeyCatcher { keyCode in
                    guard gameActive else { return }
                    switch keyCode {
                    case 123:
                        paddleX = max(0.1, paddleX - 0.05)
                    case 124:
                        paddleX = min(0.9, paddleX + 0.05)
                    default:
                        break
                    }
                }
            )
        }
        .allowsHitTesting(true)
    }
}

private struct InitialLoadingHeader: View {
    let message: String
    let progress: Double

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 14) {
                Image(nsImage: NSApplication.shared.applicationIconImage)
                    .resizable()
                    .scaledToFit()
                    .frame(width: 40, height: 40)
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .shadow(color: Color.black.opacity(0.35), radius: 8, x: 0, y: 4)

                VStack(alignment: .leading, spacing: 4) {
                    Text("Welcome to Glass Radio")
                        .font(.headline.weight(.semibold))
                        .foregroundStyle(.white)
                    Text("Building your global radio library")
                        .font(.caption)
                        .foregroundStyle(.white.opacity(0.7))
                }

                Spacer()

                Text("\(Int((progress * 100).rounded()))%")
                    .font(.headline.weight(.semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(
                        Capsule().fill(Color.white.opacity(0.1))
                    )
            }

            Text(message)
                .font(.caption2)
                .foregroundStyle(.white.opacity(0.7))
                .frame(maxWidth: .infinity, alignment: .leading)

            ProgressBarSimple(progress: progress)
                .frame(height: 10)
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 14)
        .background(
            RoundedRectangle(cornerRadius: 18, style: .continuous)
                .fill(.ultraThinMaterial)
                .overlay(
                    RoundedRectangle(cornerRadius: 18, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                )
        )
    }
}

private struct ProgressBarSimple: View {
    let progress: Double

    var body: some View {
        GeometryReader { proxy in
            let width = max(0, min(1, progress)) * proxy.size.width
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(Color.white.opacity(0.08))

                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(
                        LinearGradient(
                            colors: [
                                Color(red: 0.96, green: 0.34, blue: 0.40),
                                Color(red: 0.96, green: 0.56, blue: 0.32)
                            ],
                            startPoint: .leading,
                            endPoint: .trailing
                        )
                    )
                    .frame(width: width)
            }
        }
    }
}

private struct MiniPongView: View {
    @Binding var paddleX: CGFloat
    let isActive: Bool
    @State private var cpuX: CGFloat = 0.5
    @State private var ball = CGPoint(x: 0.5, y: 0.3)
    @State private var velocity = CGVector(dx: 0.32, dy: 0.42)
    @State private var lastTime = Date()
    @State private var playerScore = 0
    @State private var cpuScore = 0
    @State private var cpuMissChance = 0.12

    var body: some View {
        TimelineView(.animation) { context in
            GeometryReader { proxy in
                let size = proxy.size
                let paddleWidth: CGFloat = 90
                let paddleHeight: CGFloat = 8
                let ballSize: CGFloat = 12

                ZStack {
                    RoundedRectangle(cornerRadius: 16, style: .continuous)
                        .fill(Color.white.opacity(0.04))
                        .overlay(
                            RoundedRectangle(cornerRadius: 16, style: .continuous)
                                .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                        )

                    HStack(spacing: 18) {
                        Text("You \(playerScore)")
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(.white)
                        Text("CPU \(cpuScore)")
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(.white.opacity(0.8))
                    }
                    .padding(.vertical, 6)
                    .padding(.horizontal, 12)
                    .background(
                        Capsule().fill(Color.black.opacity(0.35))
                    )
                    .frame(maxWidth: .infinity, alignment: .top)
                    .padding(.top, 10)

                    Capsule()
                        .fill(Color.white.opacity(0.55))
                        .frame(width: paddleWidth, height: paddleHeight)
                        .position(
                            x: min(max(cpuX, 0.1), 0.9) * size.width,
                            y: 20
                        )

                    Capsule()
                        .fill(Color.white.opacity(0.75))
                        .frame(width: paddleWidth, height: paddleHeight)
                        .position(
                            x: min(max(paddleX, 0.1), 0.9) * size.width,
                            y: size.height - 20
                        )

                    Circle()
                        .fill(Color.white)
                        .frame(width: ballSize, height: ballSize)
                        .position(x: ball.x * size.width, y: ball.y * size.height)
                        .shadow(color: Color.white.opacity(0.6), radius: 6, x: 0, y: 0)
                }
                .contentShape(Rectangle())
                .onAppear {
                    paddleX = 0.5
                    cpuX = 0.5
                    lastTime = context.date
                }
                .onChange(of: context.date) {
                    guard isActive else { return }
                    let delta = CGFloat(context.date.timeIntervalSince(lastTime))
                    lastTime = context.date
                    step(delta: min(delta, 0.05) * 1.2)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func step(delta: CGFloat) {
        var newBall = CGPoint(x: ball.x + velocity.dx * delta, y: ball.y + velocity.dy * delta)
        if newBall.x < 0.05 || newBall.x > 0.95 {
            velocity.dx *= -1
            newBall.x = max(0.05, min(0.95, newBall.x))
        }
        if newBall.y < 0.08 {
            let cpuCenter = cpuX
            let hitRange: ClosedRange<CGFloat> = (cpuCenter - 0.12)...(cpuCenter + 0.12)
            if hitRange.contains(newBall.x) {
                velocity.dy *= -1
                let offset = (newBall.x - cpuCenter) * 0.9
                velocity.dx = (velocity.dx + offset).clamped(to: -0.6...0.6)
                newBall.y = 0.1
            } else {
                playerScore += 1
                newBall = resetBall(towardsPlayer: false)
                ball = newBall
                updateCpuPaddle(targetX: newBall.x)
                return
            }
        }

        if newBall.y > 0.9 {
            let paddleCenter = paddleX
            let hitRange: ClosedRange<CGFloat> = (paddleCenter - 0.12)...(paddleCenter + 0.12)
            if hitRange.contains(newBall.x) {
                velocity.dy *= -1
                let offset = (newBall.x - paddleCenter) * 0.9
                velocity.dx = (velocity.dx + offset).clamped(to: -0.6...0.6)
                newBall.y = 0.88
            } else {
                cpuScore += 1
                newBall = resetBall(towardsPlayer: true)
                ball = newBall
                updateCpuPaddle(targetX: newBall.x)
                return
            }
        }

        ball = newBall
        updateCpuPaddle(targetX: newBall.x)
    }

    private func resetBall(towardsPlayer: Bool) -> CGPoint {
        let reset = CGPoint(x: 0.5, y: 0.5)
        let dx = Double.random(in: -0.35...0.35)
        let dy = towardsPlayer ? 0.4 : -0.4
        velocity = CGVector(dx: dx, dy: dy)
        return reset
    }

    private func updateCpuPaddle(targetX: CGFloat) {
        let target = targetX
        let miss = Double.random(in: 0...1) < cpuMissChance
        let jitter: CGFloat = miss ? CGFloat.random(in: -0.18...0.18) : 0
        let desired = min(max(target + jitter, 0.1), 0.9)
        cpuX = cpuX + (desired - cpuX) * 0.12
    }
}

private struct KeyCatcher: NSViewRepresentable {
    let onKeyDown: (UInt16) -> Void

    func makeNSView(context: Context) -> KeyCaptureView {
        let view = KeyCaptureView()
        view.onKeyDown = onKeyDown
        DispatchQueue.main.async {
            view.window?.makeFirstResponder(view)
        }
        return view
    }

    func updateNSView(_ nsView: KeyCaptureView, context: Context) {
        DispatchQueue.main.async {
            nsView.window?.makeFirstResponder(nsView)
        }
    }
}

private final class KeyCaptureView: NSView {
    var onKeyDown: ((UInt16) -> Void)?

    override var acceptsFirstResponder: Bool { true }

    override func keyDown(with event: NSEvent) {
        onKeyDown?(event.keyCode)
    }
}

private extension Comparable {
    func clamped(to limits: ClosedRange<Self>) -> Self {
        min(max(self, limits.lowerBound), limits.upperBound)
    }
}

private struct BackgroundRefreshBadge: View {
    let message: String
    let progress: Double

    var body: some View {
        HStack(spacing: 8) {
            ProgressView(value: progress)
                .progressViewStyle(.linear)
                .frame(width: 120)

            Text(message)
                .font(.caption2)
                .foregroundStyle(.white.opacity(0.75))
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(.ultraThinMaterial)
                .overlay(
                    RoundedRectangle(cornerRadius: 14, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                )
        )
        .shadow(color: Color.black.opacity(0.35), radius: 14, x: 0, y: 6)
    }
}

private struct SearchHeroCard: View {
    let title: String
    let subtitle: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.title3.weight(.semibold))
                .foregroundStyle(.white)
            Text(subtitle)
                .font(.caption)
                .foregroundStyle(.white.opacity(0.7))
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .fill(.ultraThinMaterial)
                .overlay(
                    RoundedRectangle(cornerRadius: 16, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                )
        )
    }
}

private struct SearchCard<Content: View>: View {
    let title: String
    let subtitle: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.white)
                Text(subtitle)
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.6))
            }
            content
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(.ultraThinMaterial)
                .overlay(
                    RoundedRectangle(cornerRadius: 14, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.1), lineWidth: 1)
                )
        )
    }
}

private struct SearchHeroLarge: View {
    var body: some View {
        ZStack(alignment: .bottomLeading) {
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [
                            Color(red: 0.36, green: 0.18, blue: 0.26),
                            Color(red: 0.18, green: 0.14, blue: 0.22),
                            Color(red: 0.10, green: 0.10, blue: 0.14)
                        ],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing
                    )
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 20, style: .continuous)
                        .stroke(Color.white.opacity(0.12), lineWidth: 1)
                )

            Circle()
                .fill(Color.red.opacity(0.25))
                .frame(width: 240, height: 240)
                .blur(radius: 40)
                .offset(x: 220, y: -80)

            Circle()
                .fill(Color.orange.opacity(0.2))
                .frame(width: 200, height: 200)
                .blur(radius: 50)
                .offset(x: -120, y: -40)

            VStack(alignment: .leading, spacing: 8) {
                Text("Search")
                    .font(.title2.weight(.semibold))
                    .foregroundStyle(.white)
                Text("Discover stations by mood, city, genre, or vibe.")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .padding(20)
        }
        .frame(height: 160)
        .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
    }
}

private struct SearchGlassCard<Content: View>: View {
    let title: String
    let subtitle: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.white)
                Text(subtitle)
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.6))
            }
            content
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .fill(.ultraThinMaterial)
                .overlay(
                    RoundedRectangle(cornerRadius: 16, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                )
        )
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
    }
}

private struct SearchCategoryTile: View {
    let title: String
    let index: Int
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack(alignment: .bottomLeading) {
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(tileGradient(index))
                    .overlay(
                        RoundedRectangle(cornerRadius: 14, style: .continuous)
                            .stroke(Color.white.opacity(0.12), lineWidth: 1)
                    )
                Text(title)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.white)
                    .padding(10)
            }
            .frame(height: 86)
        }
        .buttonStyle(.plain)
    }

    private func tileGradient(_ index: Int) -> LinearGradient {
        let palettes: [[Color]] = [
            [Color(red: 0.80, green: 0.32, blue: 0.40), Color(red: 0.42, green: 0.18, blue: 0.24)],
            [Color(red: 0.26, green: 0.55, blue: 0.80), Color(red: 0.16, green: 0.24, blue: 0.42)],
            [Color(red: 0.75, green: 0.62, blue: 0.18), Color(red: 0.38, green: 0.30, blue: 0.12)],
            [Color(red: 0.22, green: 0.70, blue: 0.52), Color(red: 0.12, green: 0.32, blue: 0.28)],
            [Color(red: 0.60, green: 0.32, blue: 0.78), Color(red: 0.28, green: 0.18, blue: 0.42)],
            [Color(red: 0.85, green: 0.45, blue: 0.18), Color(red: 0.40, green: 0.22, blue: 0.12)]
        ]
        let palette = palettes[index % palettes.count]
        return LinearGradient(colors: palette, startPoint: .topLeading, endPoint: .bottomTrailing)
    }
}

private struct SearchCountryPill: View {
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack {
                Text(title)
                    .font(.footnote)
                    .foregroundStyle(.white)
                    .lineLimit(1)
                Spacer()
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .background(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(Color.white.opacity(0.08))
                    .overlay(
                        RoundedRectangle(cornerRadius: 12, style: .continuous)
                            .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                    )
            )
        }
        .buttonStyle(.plain)
    }
}

private struct SearchSuggestionChip: View {
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.caption2)
                .foregroundStyle(.white.opacity(0.9))
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(
                    Capsule().fill(Color.white.opacity(0.10))
                )
        }
        .buttonStyle(.plain)
    }
}

private struct SearchSuggestionRow: View {
    let station: RadioStation
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                StationArtworkView(station: station, cornerRadius: 6)
                    .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 2) {
                    Text(station.name)
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.white)
                        .lineLimit(1)
                    Text(station.country)
                        .font(.caption2)
                        .foregroundStyle(.white.opacity(0.65))
                        .lineLimit(1)
                }
                Spacer()
            }
            .padding(.vertical, 4)
        }
        .buttonStyle(.plain)
    }
}

private func flagEmoji(for code: String) -> String {
    let base: UInt32 = 0x1F1E6
    let scalars = code.uppercased().unicodeScalars.compactMap { scalar -> UnicodeScalar? in
        let value = scalar.value
        guard value >= 65 && value <= 90 else { return nil }
        let offset = value - 65
        return UnicodeScalar(base + offset)
    }
    return String(String.UnicodeScalarView(scalars))
}

private func flagImageResourceURL(code: String) -> URL? {
    let lowercased = code.lowercased()
    guard lowercased.count == 2 else { return nil }
    if let fromSupport = AppAssetStore.flagURL(code: lowercased) {
        return fromSupport
    }
#if SWIFT_PACKAGE
    return Bundle.module.url(forResource: lowercased, withExtension: "png", subdirectory: "Flags")
#else
    return Bundle.main.url(forResource: lowercased, withExtension: "png", subdirectory: "Flags")
#endif
}

private func flagImageURL(code: String, width: Int) -> URL? {
    let lowercased = code.lowercased()
    guard lowercased.count == 2 else { return nil }
    return URL(string: "https://flagcdn.com/w\(width)/\(lowercased).png")
}

private struct SearchBrandCard: View {
    let cluster: AppViewModel.BrandCluster
    let onPlay: (RadioStation) -> Void

    var body: some View {
        guard let primary = cluster.stations.first else {
            return AnyView(EmptyView())
        }

        return AnyView(
        ZStack(alignment: .bottomLeading) {
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .fill(Color.white.opacity(0.06))
                .overlay(
                    RoundedRectangle(cornerRadius: 16, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                )

            if let favicon = primary.favicon, let url = URL(string: favicon) {
                AsyncImage(url: url) { phase in
                    if let image = phase.image {
                        image
                            .resizable()
                            .scaledToFill()
                            .blur(radius: 12)
                            .opacity(0.35)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .clipped()
            }

            LinearGradient(
                colors: [
                    Color.black.opacity(0.05),
                    Color.black.opacity(0.55)
                ],
                startPoint: .top,
                endPoint: .bottom
            )
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))

            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 8) {
                    StationArtworkView(station: primary, cornerRadius: 8)
                        .frame(width: 40, height: 40)

                    VStack(alignment: .leading, spacing: 2) {
                        Text(cluster.name)
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(.white)
                            .lineLimit(1)
                        Text("\(cluster.stations.count) channels")
                            .font(.caption2)
                            .foregroundStyle(.white.opacity(0.7))
                    }
                }

                VStack(alignment: .leading, spacing: 6) {
                    ForEach(Array(cluster.stations.prefix(4))) { station in
                        Button {
                            onPlay(station)
                        } label: {
                            HStack(spacing: 6) {
                                StationArtworkView(station: station, cornerRadius: 4)
                                    .frame(width: 18, height: 18)
                                Text(station.name)
                                    .font(.caption2)
                                    .foregroundStyle(.white.opacity(0.9))
                                    .lineLimit(1)
                                Spacer()
                                Image(systemName: "play.fill")
                                    .font(.caption2)
                                    .foregroundStyle(.white.opacity(0.7))
                            }
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .padding(14)
        }
        .frame(width: 280, alignment: .leading)
        )
    }
}

private struct SearchCountryTopCard: View {
    let country: String
    let station: RadioStation
    let onPlay: () -> Void

    var body: some View {
        Button(action: onPlay) {
            ZStack(alignment: .bottomLeading) {
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(
                        LinearGradient(
                            colors: [
                                Color.white.opacity(0.12),
                                Color.white.opacity(0.04)
                            ],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
                    .overlay(
                        RoundedRectangle(cornerRadius: 16, style: .continuous)
                            .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                    )

                VStack(alignment: .leading, spacing: 6) {
                    StationArtworkView(station: station, cornerRadius: 10)
                        .frame(width: 44, height: 44)
                    Text(country)
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.white)
                        .lineLimit(1)
                    Text(station.name)
                        .font(.caption2)
                        .foregroundStyle(.white.opacity(0.7))
                        .lineLimit(1)
                }
                .padding(12)
            }
            .frame(width: 200, height: 120)
        }
        .buttonStyle(.plain)
    }
}

private struct SearchCountryCard: View {
    let title: String
    let code: String
    let action: () -> Void

    private let cardWidth: CGFloat = 180
    private var cardHeight: CGFloat { cardWidth / 1.65 }

    var body: some View {
        Button(action: action) {
            ZStack {
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(Color.white.opacity(0.06))

                if let url = flagImageResourceURL(code: code) ?? flagImageURL(code: code, width: 640) {
                    AsyncImage(url: url) { phase in
                        if let image = phase.image {
                            image
                                .resizable()
                                .aspectRatio(1.65, contentMode: .fill)
                        } else {
                            Color.white.opacity(0.02)
                        }
                    }
                    .frame(width: cardWidth, height: cardHeight)
                }

                LinearGradient(
                    colors: [
                        Color.black.opacity(0.05),
                        Color.black.opacity(0.28),
                        Color.black.opacity(0.5)
                    ],
                    startPoint: .top,
                    endPoint: .bottom
                )
            }
            .frame(width: cardWidth, height: cardHeight)
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
            )
            .overlay(alignment: .bottomLeading) {
                HStack {
                    Text(title)
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(.white)
                        .lineLimit(1)
                        .minimumScaleFactor(0.85)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .frame(height: 30)
                .padding(.horizontal, 10)
                .background(.ultraThinMaterial)
                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay(
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                )
                .padding(.horizontal, 10)
                .padding(.bottom, 8)
            }
        }
        .buttonStyle(.plain)
    }
}
