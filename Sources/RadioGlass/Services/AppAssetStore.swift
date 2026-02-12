import Foundation

enum AppAssetStore {
    static let appSupportDirectory: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSTemporaryDirectory())
        return base.appendingPathComponent("Glass Radio", isDirectory: true)
    }()

    static let flagsDirectory: URL = {
        appSupportDirectory.appendingPathComponent("Flags", isDirectory: true)
    }()

    static let stationLogosDirectory: URL = {
        appSupportDirectory.appendingPathComponent("StationLogos", isDirectory: true)
    }()

    static func ensureDirectories() {
        let fm = FileManager.default
        try? fm.createDirectory(at: appSupportDirectory, withIntermediateDirectories: true)
        try? fm.createDirectory(at: flagsDirectory, withIntermediateDirectories: true)
        try? fm.createDirectory(at: stationLogosDirectory, withIntermediateDirectories: true)
    }

    static func flagURL(code: String) -> URL? {
        let lowercased = code.lowercased()
        guard lowercased.count == 2 else { return nil }
        let url = flagsDirectory.appendingPathComponent("\(lowercased).png")
        return FileManager.default.fileExists(atPath: url.path) ? url : nil
    }

    static func copyBundledFlagsIfNeeded() {
        ensureDirectories()
        let fm = FileManager.default

#if SWIFT_PACKAGE
        guard let bundleURL = Bundle.module.url(forResource: "Flags", withExtension: nil) else { return }
#else
        guard let bundleURL = Bundle.main.url(forResource: "Flags", withExtension: nil) else { return }
#endif

        guard let items = try? fm.contentsOfDirectory(at: bundleURL, includingPropertiesForKeys: nil) else { return }
        for item in items where item.pathExtension.lowercased() == "png" {
            let dest = flagsDirectory.appendingPathComponent(item.lastPathComponent)
            if fm.fileExists(atPath: dest.path) { continue }
            try? fm.copyItem(at: item, to: dest)
        }
    }
}
