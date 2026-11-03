# Third-party notices

Glass Radio is a personal FOSS application released under the MIT License.
This file records external services and runtime components used by the Windows
build. It does not claim ownership of remote station data or artwork.

## Station catalogue

Station metadata is requested from the public Radio Browser API. The service
endpoint and its acceptable-use requirements remain external to this project;
the application does not bundle or redistribute the catalogue as project-owned
content.

## Remote station artwork

Station logos and broadcaster artwork are remote content selected from station
metadata and documented fallback providers. They are cached only in the
per-user Glass Radio data directory, are size/content validated, and are not
included in the source distribution or installer payload. Users can clear the
cache from the application settings.

## Windows-provided components

The application uses the Windows App SDK, WinUI 3, .NET, and system-provided
Segoe MDL2 Assets / Segoe Fluent Icons glyphs. Those components retain their
own Microsoft/runtime licensing and are not relicensed by this project.

## Bundled fonts

`src/GlassRadio/Assets/Fonts/FluentSystemIcons-Filled.ttf` - Fluent UI System
Icons (Filled), by Microsoft, from
https://github.com/microsoft/fluentui-system-icons, MIT License. Used for the
player pill's transport-control glyphs (play/pause/previous/next/volume).
Shipped as-is, unmodified, in the installer payload.

No other third-party font, sound, bundled station logo, flag image, or
closed-source SDK is currently shipped in the Windows source or installer
payload.
