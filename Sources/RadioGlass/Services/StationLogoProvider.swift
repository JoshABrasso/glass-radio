import Foundation

actor StationLogoProvider {
    static let shared = StationLogoProvider()

    private let fileManager = FileManager.default
    private let cacheDirectory: URL
    private let maxCacheBytes: Int64 = 2_500_000_000

    init() {
        AppAssetStore.ensureDirectories()
        cacheDirectory = AppAssetStore.stationLogosDirectory
    }

    func logoData(for station: RadioStation) async -> Data? {
        let key = station.stationuuid
        let diskURL = cacheDirectory.appendingPathComponent("\(key).img")

        if let data = try? Data(contentsOf: diskURL), !data.isEmpty {
            return data
        }

        for url in candidateURLs(for: station) {
            do {
                var request = URLRequest(url: url)
                request.timeoutInterval = 8
                request.setValue("RadioGlass/2.0", forHTTPHeaderField: "User-Agent")

                let (data, response) = try await URLSession.shared.data(for: request)
                guard let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode) else {
                    continue
                }

                // Basic validation to avoid caching HTML/error responses as images.
                if let mime = http.value(forHTTPHeaderField: "Content-Type")?.lowercased(),
                   !(mime.contains("image")) {
                    continue
                }
                guard data.count > 256 else { continue }

                try? data.write(to: diskURL, options: .atomic)
                await enforceCacheLimitIfNeeded()
                return data
            } catch {
                continue
            }
        }

        return nil
    }

    private func candidateURLs(for station: RadioStation) -> [URL] {
        var urls: [URL] = []

        if let favicon = station.favicon,
           let url = URL(string: favicon),
           let scheme = url.scheme?.lowercased(),
           scheme == "http" || scheme == "https" {
            urls.append(url)
            if scheme == "http",
               let httpsURL = URL(string: favicon.replacingOccurrences(of: "http://", with: "https://")) {
                urls.append(httpsURL)
            }
        }

        let hostCandidates = [station.homepage, station.urlResolved, station.url]
            .compactMap { $0 }
            .compactMap { URL(string: $0)?.host }
            .map { $0.lowercased() }

        for host in Set(hostCandidates) {
            if let duck = URL(string: "https://icons.duckduckgo.com/ip3/\(host).ico") {
                urls.append(duck)
            }
            if let google = URL(string: "https://www.google.com/s2/favicons?domain=\(host)&sz=512") {
                urls.append(google)
            }
            if let iconHorse = URL(string: "https://icon.horse/icon/\(host)") {
                urls.append(iconHorse)
            }
            if let clearbit = URL(string: "https://logo.clearbit.com/\(host)?size=512") {
                urls.append(clearbit)
            }
        }

        return urls
    }

    private func enforceCacheLimitIfNeeded() async {
        let fm = fileManager
        let cacheDir = cacheDirectory
        let maxBytes = maxCacheBytes

        let entries = await Task.detached(priority: .utility) {
            guard let files = try? fm.contentsOfDirectory(at: cacheDir, includingPropertiesForKeys: [.contentModificationDateKey, .fileSizeKey], options: [.skipsHiddenFiles]) else {
                return (Int64(0), [(url: URL, size: Int64, date: Date)]())
            }

            var total: Int64 = 0
            var items: [(url: URL, size: Int64, date: Date)] = []
            for fileURL in files {
                guard let values = try? fileURL.resourceValues(forKeys: [.contentModificationDateKey, .fileSizeKey]),
                      let size = values.fileSize,
                      let date = values.contentModificationDate else { continue }
                let size64 = Int64(size)
                total += size64
                items.append((fileURL, size64, date))
            }
            return (total, items)
        }.value

        var remaining = entries.0
        guard remaining > maxBytes else { return }

        let sorted = entries.1.sorted { $0.date < $1.date }
        for entry in sorted {
            try? fm.removeItem(at: entry.url)
            remaining -= entry.size
            if remaining <= maxBytes { break }
        }
    }
}
