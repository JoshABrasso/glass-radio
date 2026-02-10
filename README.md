# Glass Radio

![macOS](https://img.shields.io/badge/platform-macOS-111111?style=flat-square)
![SwiftUI](https://img.shields.io/badge/SwiftUI-06B6D4?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-16a34a?style=flat-square)

A macOS radio app inspired by Apple Music with a liquid-glass aesthetic, curated global stations, and a fast, dynamic browsing experience.

![Glass Radio Screenshot](docs/screenshot-2026-02-10.png)

## Highlights

- Apple Music‑style three‑pane layout with a persistent player bar
- Curated top stations per country + full catalog browsing and search
- Genre filtering with responsive multi‑column layouts on large screens
- Favorites/presets with local persistence
- Streaming playback via AVPlayer with AirPlay routing
- Dynamic macOS icon support (Icon Composer `.icon` asset)

## Requirements

- macOS 14+
- Xcode 26+ for dynamic icon compilation via `actool`

## Run (Xcode)

1. Open `RadioGlass.xcodeproj` in Xcode.
2. Select the `RadioGlass` scheme.
3. Set your Team in Signing & Capabilities for `com.radioglass.app`.
4. Run on `My Mac`.

## Run (no Xcode)

1. Build the app bundle:
   `./scripts/build-app.sh`
2. Launch:
   `open dist/RadioGlass.app`

## Project Layout

- `Package.swift`
- `Sources/RadioGlass/RadioGlassApp.swift`
- `Sources/RadioGlass/Views/` UI
- `Sources/RadioGlass/ViewModels/` app state
- `Sources/RadioGlass/Services/` API, playback, persistence
- `Sources/RadioGlass/Theme/` liquid‑glass styling helpers
- `App/Assets.xcassets/` dynamic app icon assets

## Notes

- Uses the public Radio Browser API endpoints.
- Dynamic icon compilation happens during `./scripts/build-app.sh` when Xcode is available.

## License

MIT. See `LICENSE`.
