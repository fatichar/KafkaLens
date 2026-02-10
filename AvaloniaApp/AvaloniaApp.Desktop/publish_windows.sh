#!/bin/bash
# windows.sh - Build and package KafkaLens for Windows (x64)
#
# Creates: KafkaLens-{VERSION}-windows-x64.exe (installer)

source "$(dirname "$0")/publish_common.sh"

PLATFORM_NAME="win-x64"
BASE_NAME="KafkaLens-${VERSION}-${PLATFORM_NAME}"
OUT_DIR="$RELEASES_DIR/KafkaLens_win"

# Build
publish_app "$PLATFORM_NAME" "$OUT_DIR"

# Create installer
"/C/Program Files (x86)/Inno Setup 6/ISCC.exe" "$REPO_ROOT/Installer/install_windows.iss"

# Rename installer to standard format
mv "$RELEASES_DIR/KafkaLensSetup.exe" "$RELEASES_DIR/${BASE_NAME}.exe"

echo "Created: $RELEASES_DIR/${BASE_NAME}.exe"

# Cleanup
rm -rf "$OUT_DIR"