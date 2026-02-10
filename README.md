# RadioGlass

RadioGlass is a macOS SwiftUI app inspired by Apple Music, with a liquid-glass visual style and global live radio discovery.

## Implemented foundation

- Apple Music-like split layout (Sidebar + content + persistent player bar)
- Liquid glass cards and atmospheric gradient backdrop
- Featured station cards and hero section for premium first-load experience
- Global trending stations from Radio Browser
- Curated top countries list (by station count)
- Country-level popular station view
- Global station search
- Favorites add/remove with persistence in `UserDefaults`
- Recently played station rail
- In-app streaming using `AVPlayer`
- AirPlay route picker (`AVRoutePickerView`)
- Now Playing metadata integration (`MPNowPlayingInfoCenter`)
- Media key / Control Center command support (`MPRemoteCommandCenter`)
- App-level playback command menu and handoff activity metadata

## Project layout

- `Package.swift`
- `Sources/RadioGlass/RadioGlassApp.swift`
- `Sources/RadioGlass/Views/` UI
- `Sources/RadioGlass/ViewModels/` app state
- `Sources/RadioGlass/Services/` API, playback, persistence
- `Sources/RadioGlass/Theme/` liquid-glass styling helpers

## Open and run

1. Open `RadioGlass.xcodeproj` in Xcode.
2. Select the `RadioGlass` scheme.
3. In Signing & Capabilities, set your Team for `com.radioglass.app`.
4. Run on `My Mac`.

## Run without Xcode

1. Build a standalone app bundle:
   `./scripts/build-app.sh`
2. Open the app:
   `open dist/RadioGlass.app`

## Xcode project details

- Project: `RadioGlass.xcodeproj`
- Shared scheme: `RadioGlass.xcodeproj/xcshareddata/xcschemes/RadioGlass.xcscheme`
- Info plist: `App/Info.plist`
- Entitlements: `App/RadioGlass.entitlements`
- Enabled capability baseline:
  - App Sandbox
  - Outgoing Network Connections (`com.apple.security.network.client`)

## Notes

- This uses the public Radio Browser API endpoint:
  `https://de1.api.radio-browser.info/json`
- Production hardening still needed: retries, better artwork fallback, stream health checks, and richer queue/session handling.
