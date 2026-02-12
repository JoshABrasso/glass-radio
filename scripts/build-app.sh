#!/bin/zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_NAME="RadioGlass"
DISPLAY_NAME="Glass Radio"
DIST_DIR="$ROOT_DIR/dist"
APP_DIR="$DIST_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
ASSETS_DIR="$ROOT_DIR/App/Assets.xcassets"
APP_ICON_NAME="glass_radio"
ICON_DIR="$ROOT_DIR/radio_icons"
ICON_PNG="$ICON_DIR/Icon-macOS-Default-1024x1024@1x.png"
ICONSET_DIR="$ROOT_DIR/.build/appicon.iconset"
ICON_ICNS="$RESOURCES_DIR/AppIcon.icns"
BUILT_WITH_XCODE=0

cd "$ROOT_DIR"

echo "Building release app..."
mkdir -p "$DIST_DIR"
rm -rf "$APP_DIR"

if xcrun xcodebuild -version >/dev/null 2>&1; then
  DERIVED_DATA="$ROOT_DIR/.build/xcode"
  xcrun xcodebuild \
    -project "$ROOT_DIR/RadioGlass.xcodeproj" \
    -scheme "$APP_NAME" \
    -configuration Release \
    -derivedDataPath "$DERIVED_DATA" \
    build

  BUILT_APP="$DERIVED_DATA/Build/Products/Release/$APP_NAME.app"
  if [[ -d "$BUILT_APP" ]]; then
    cp -R "$BUILT_APP" "$APP_DIR"
    BUILT_WITH_XCODE=1
  fi
fi

if [[ "$BUILT_WITH_XCODE" -eq 0 ]]; then
  echo "Falling back to SwiftPM binary build..."
  swift build -c release

  BIN_PATH="$ROOT_DIR/.build/release/$APP_NAME"
  if [[ ! -x "$BIN_PATH" ]]; then
    ALT_BIN="$ROOT_DIR/.build/arm64-apple-macosx/release/$APP_NAME"
    if [[ -x "$ALT_BIN" ]]; then
      BIN_PATH="$ALT_BIN"
    else
      echo "Could not find built binary at expected paths."
      exit 1
    fi
  fi

  mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
  cp "$BIN_PATH" "$MACOS_DIR/$APP_NAME"
  chmod +x "$MACOS_DIR/$APP_NAME"
fi

# Fallback: build an .icns if Assets.car wasn't generated.
if [[ ! -f "$RESOURCES_DIR/Assets.car" && -f "$ICON_PNG" ]]; then
  rm -rf "$ICONSET_DIR"
  mkdir -p "$ICONSET_DIR"

  sips -z 16 16 "$ICON_PNG" --out "$ICONSET_DIR/icon_16x16.png" >/dev/null
  sips -z 32 32 "$ICON_PNG" --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$ICON_PNG" --out "$ICONSET_DIR/icon_32x32.png" >/dev/null
  sips -z 64 64 "$ICON_PNG" --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$ICON_PNG" --out "$ICONSET_DIR/icon_128x128.png" >/dev/null
  sips -z 256 256 "$ICON_PNG" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$ICON_PNG" --out "$ICONSET_DIR/icon_256x256.png" >/dev/null
  sips -z 512 512 "$ICON_PNG" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$ICON_PNG" --out "$ICONSET_DIR/icon_512x512.png" >/dev/null
  cp "$ICON_PNG" "$ICONSET_DIR/icon_512x512@2x.png"

  iconutil -c icns "$ICONSET_DIR" -o "$ICON_ICNS"
fi

if [[ "$BUILT_WITH_XCODE" -eq 0 ]]; then
cat > "$CONTENTS_DIR/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>com.radioglass.app</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>$DISPLAY_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$DISPLAY_NAME</string>
  <key>CFBundleIconName</key>
  <string>$APP_ICON_NAME</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.music</string>
  <key>LSMinimumSystemVersion</key>
  <string>14.0</string>
  <key>NSAppTransportSecurity</key>
  <dict>
    <key>NSAllowsArbitraryLoads</key>
    <true/>
  </dict>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
PLIST
fi

echo "Built app bundle: $APP_DIR"
echo "Open with: open \"$APP_DIR\""
