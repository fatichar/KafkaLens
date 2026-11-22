#!/bin/bash
# macos.sh - Build and package KafkaLens for macOS (ARM64)
#
# Creates: KafkaLens-{VERSION}-macos-arm64.zip

source "$(dirname "$0")/publish_common.sh"

PLATFORM_NAME="macos-arm64"
BASE_NAME="KafkaLens-${VERSION}-${PLATFORM_NAME}"
OUT_DIR="$RELEASES_DIR/$BASE_NAME"
ZIP_FILE="$RELEASES_DIR/${BASE_NAME}.zip"

# Build
publish_app "osx-arm64" "$OUT_DIR"

# Package
create_zip "$OUT_DIR" "$ZIP_FILE"

# Cleanup
rm -rf "$OUT_DIR"