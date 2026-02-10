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

cd "$ROOT_DIR"

echo "Building release binary..."
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

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

cp "$BIN_PATH" "$MACOS_DIR/$APP_NAME"
chmod +x "$MACOS_DIR/$APP_NAME"

if [[ -d "$ASSETS_DIR" ]]; then
  if xcrun actool --version >/dev/null 2>&1; then
    ASSET_INFO_PLIST="$RESOURCES_DIR/asset-info.plist"
    xcrun actool \
      --output-format human-readable-text \
      --notices --warnings \
      --platform macosx \
      --minimum-deployment-target 14.0 \
      --app-icon "$APP_ICON_NAME" \
      --output-partial-info-plist "$ASSET_INFO_PLIST" \
      --compile "$RESOURCES_DIR" \
      "$ASSETS_DIR"
  else
    echo "Warning: actool not available. Install Xcode and run 'sudo xcode-select -switch /Applications/Xcode.app/Contents/Developer' to build dynamic app icons."
  fi
fi

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

echo "Built app bundle: $APP_DIR"
echo "Open with: open \"$APP_DIR\""
