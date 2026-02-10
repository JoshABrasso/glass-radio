import Foundation

actor StationLogoProvider {
    static let shared = StationLogoProvider()

    private let fileManager = FileManager.default
    private let cacheDirectory: URL

    init() {
        let base = fileManager.urls(for: .cachesDirectory, in: .userDomainMask).first ?? URL(fileURLWithPath: NSTemporaryDirectory())
        cacheDirectory = base.appendingPathComponent("RadioGlassLogos", isDirectory: true)
        try? fileManager.createDirectory(at: cacheDirectory, withIntermediateDirectories: true)
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
                guard data.count > 256 else { continue }

                try? data.write(to: diskURL, options: .atomic)
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
            if let google = URL(string: "https://www.google.com/s2/favicons?domain=\(host)&sz=256") {
                urls.append(google)
            }
            if let iconHorse = URL(string: "https://icon.horse/icon/\(host)") {
                urls.append(iconHorse)
            }
        }

        return urls
    }
}
