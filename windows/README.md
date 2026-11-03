# Glass Radio (Windows)

A native WinUI 3 port of Glass Radio: .NET 9, C# 13, Windows App SDK 1.8,
CommunityToolkit.Mvvm. Same liquid-glass visual style and global live radio discovery as
the [macOS app](../macos/README.md), built from scratch for Windows.

## Project layout

- `src/GlassRadio` — WinUI 3 app (views, view models, assets)
- `src/GlassRadio.Application` — app-layer services and contracts
- `src/GlassRadio.Domain` — platform-agnostic domain model
- `src/GlassRadio.Infrastructure.Windows` — Windows-specific playback, storage, artwork,
  and local API implementations
- `tests/` — unit tests for each of the above layers
- `installer/` — WiX Toolset v7 project that produces the `.msi`/`Glass-Radio-Setup.exe`
- `packaging/` — shared packaging assets

## Build and run

Requires Visual Studio 2022 (or the .NET 9 SDK + Windows App SDK workload) on Windows 11.

```
dotnet build GlassRadio.Windows.sln
```

Run `src/GlassRadio` from Visual Studio, or `dotnet run --project src/GlassRadio`.

## Tests

```
dotnet test GlassRadio.Windows.sln
```

## Building the installer

Requires [WiX Toolset v7](https://wixtoolset.org/) installed. From `installer/`:

```
./build-installer.ps1
```

This publishes a self-contained win-x64 build and produces `Glass-Radio-Setup.exe`.

## Notes

- This uses the public Radio Browser API endpoint: `https://de1.api.radio-browser.info/json`
- Build output goes to an external build root, not inside the workspace — see
  `Directory.Build.props` (`BuildRoot`, default `C:\GlassRadio-Build`, override with an
  MSBuild property if you'd rather build elsewhere).
