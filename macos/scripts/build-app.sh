#!/bin/zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
WORKSPACE_DIR="$(cd "$ROOT_DIR/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/RadioGlass.xcodeproj"
SCHEME_NAME="RadioGlass"
PRODUCT_NAME="GlassRadio"
BUILDS_DIR="$WORKSPACE_DIR/Builds"
XCODEBUILD="/Volumes/Data Extension/Applications/Devalop/Xcode.app/Contents/Developer/usr/bin/xcodebuild"
BUILD_PREFIX="${BUILD_PREFIX:-$(date +%Y%m%d-%H%M%S)}"

if [[ ! -x "$XCODEBUILD" ]]; then
  echo "Required Xcode build tool not found: $XCODEBUILD"
  exit 1
fi

mkdir -p "$BUILDS_DIR"

build_configuration() {
  local configuration="$1"
  local derived_data="$ROOT_DIR/.build/xcode-$configuration"
  local built_app="$derived_data/Build/Products/$configuration/$PRODUCT_NAME.app"
  local staged_app="$BUILDS_DIR/$BUILD_PREFIX-$configuration-$PRODUCT_NAME.app"

  echo "Building $configuration..."
  "$XCODEBUILD" \
    -project "$PROJECT_PATH" \
    -scheme "$SCHEME_NAME" \
    -configuration "$configuration" \
    -destination "platform=macOS" \
    -derivedDataPath "$derived_data" \
    build

  if [[ ! -d "$built_app" ]]; then
    echo "Expected app bundle was not produced: $built_app"
    exit 1
  fi

  if [[ -e "$staged_app" ]]; then
    echo "Build output already exists: $staged_app"
    echo "Choose a different BUILD_PREFIX or run again with a new timestamp."
    exit 1
  fi

  ditto "$built_app" "$staged_app"
  echo "Staged $configuration app: $staged_app"
}

build_configuration "Debug"
build_configuration "Release"

echo "Build artifacts:"
echo "  $BUILDS_DIR/$BUILD_PREFIX-Debug-$PRODUCT_NAME.app"
echo "  $BUILDS_DIR/$BUILD_PREFIX-Release-$PRODUCT_NAME.app"
