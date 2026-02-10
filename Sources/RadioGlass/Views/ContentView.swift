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
                    window.titlebarAppearsTransparent = false
                }
            )
            .searchable(text: $viewModel.searchText, placement: .toolbar, prompt: tab == .countries ? "Search to add niche stations" : "Search stations")
            .onSubmit(of: .search) {
                Task {
                    await viewModel.performSearch()
                    tab = .search
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

    private var leftSidebar: some View {
        List {
            Section {
                Text("Library")
                    .font(.caption2.weight(.semibold))
                    .foregroundStyle(.white.opacity(0.52))
            }

            Section {
                sidebarItem("Country Picks", tab: .countries, icon: "globe")
                sidebarItem("Search", tab: .search, icon: "magnifyingglass")
                sidebarItem("Discover", tab: .discover, icon: "sparkles")
            }

            Section("Preset Stations") {
                if viewModel.presets.isEmpty {
                    Text("Star stations to add presets")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(viewModel.presets) { station in
                        Button {
                            viewModel.play(station, in: viewModel.presets)
                        } label: {
                            HStack(spacing: 6) {
                                StationArtworkView(station: station, cornerRadius: 4)
                                    .frame(width: 16, height: 16)

                                Text(station.name)
                                    .font(.caption)
                                    .lineLimit(1)

                                Spacer()

                                if viewModel.isCurrentStation(station) {
                                    Image(systemName: "speaker.wave.2.fill")
                                        .foregroundStyle(.red)
                                        .font(.caption2)
                                }
                            }
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
        }
        .environment(\.defaultMinListRowHeight, 22)
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
                    .font(.caption2)
                Text(title)
                    .font(.caption)
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
                    searchResultsView
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
        case .search: return "Search Results"
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
                        if geo.size.width >= 900 {
                            let buttonWidth: CGFloat = 140
                            let columnCount = max(6, Int((geo.size.width - 32) / (buttonWidth + 8)))
                            let columns = Array(repeating: GridItem(.fixed(buttonWidth), spacing: 6), count: columnCount)
                            LazyVGrid(columns: columns, alignment: .center, spacing: 6) {
                                ForEach(viewModel.genreButtons, id: \.self) { genre in
                                    Button {
                                        viewModel.setGenre(genre)
                                    } label: {
                                        Text(genre)
                                            .font(.caption2)
                                            .foregroundStyle(.white)
                                            .lineLimit(1)
                                            .truncationMode(.tail)
                                            .minimumScaleFactor(0.8)
                                            .frame(width: buttonWidth, height: 28)
                                            .background(
                                                viewModel.selectedGenre == genre ? Color.red.opacity(0.28) : Color.white.opacity(0.08),
                                                in: Capsule()
                                            )
                                    }
                                    .buttonStyle(.plain)
                                }
                            }
                            .frame(maxWidth: 1000)
                            .frame(maxWidth: .infinity)
                            .padding(.horizontal, 16)
                        } else {
                            ScrollView(.horizontal, showsIndicators: false) {
                                HStack(spacing: 6) {
                                    ForEach(viewModel.genreButtons, id: \.self) { genre in
                                        Button {
                                            viewModel.setGenre(genre)
                                        } label: {
                                            Text(genre)
                                                .font(.caption2)
                                                .foregroundStyle(.white)
                                                .lineLimit(1)
                                                .truncationMode(.tail)
                                                .minimumScaleFactor(0.8)
                                                .frame(width: 120, height: 28)
                                                .background(
                                                    viewModel.selectedGenre == genre ? Color.red.opacity(0.28) : Color.white.opacity(0.08),
                                                    in: Capsule()
                                                )
                                        }
                                        .buttonStyle(.plain)
                                    }
                                }
                                .padding(.horizontal, 16)
                            }
                        }
                    }
                    .frame(height: 70)

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

    private var searchResultsView: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Use search to find niche stations and add them with the star")
                .font(.caption2)
                .foregroundStyle(.white.opacity(0.62))

            LazyVStack(spacing: 6) {
                ForEach(viewModel.searchResults) { station in
                    CountryStationRowView(
                        station: station,
                        isPreset: viewModel.isPreset(station),
                        onPlay: { viewModel.play(station, in: viewModel.searchResults) },
                        onTogglePreset: { viewModel.togglePreset(station) }
                    )
                }
            }
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
                    .font(.caption.weight(.semibold))
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
                                Text(country.displayName)
                                    .font(.caption)
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
                .font(.caption.weight(.semibold))
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
                                    .font(.caption2)
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

    var body: some View {
        ZStack {
            Color.black.opacity(0.55)
                .ignoresSafeArea()
                .background(.ultraThinMaterial)

            VStack(spacing: 14) {
                Text("Welcome to Glass Radio")
                    .font(.title3.weight(.semibold))
                    .foregroundStyle(.white)

                Text("Building your global radio library")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.7))

                ProgressView(value: progress)
                    .progressViewStyle(.linear)
                    .frame(width: 260)

                Text(message)
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .padding(.vertical, 24)
            .padding(.horizontal, 28)
            .background(
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(.ultraThinMaterial)
                    .overlay(
                        RoundedRectangle(cornerRadius: 18, style: .continuous)
                            .strokeBorder(Color.white.opacity(0.14), lineWidth: 1)
                    )
                    .shadow(color: Color.black.opacity(0.45), radius: 24, x: 0, y: 12)
            )
        }
        .allowsHitTesting(true)
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
