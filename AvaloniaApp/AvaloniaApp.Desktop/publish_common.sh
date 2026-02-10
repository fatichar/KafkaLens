#!/bin/bash
# shared_install.sh - Shared configuration and functions for KafkaLens publish scripts
#
# This file is sourced by platform-specific publish scripts (linux.sh, macos.sh, windows.sh)
# to provide common variables and helper functions.

set -e

OUT_DIR="../../Releases/KafkaLens"

# Resolve paths
SCRIPT_DIR="$(dirname "${BASH_SOURCE[0]}")"
REPO_ROOT="$SCRIPT_DIR/../.."
RELEASES_DIR="$REPO_ROOT/Releases"

# Extract version from Directory.Build.props
VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$REPO_ROOT/Directory.Build.props")

# Publish the application for a given runtime
# Usage: publish_app <runtime> <output_dir>
publish_app() {
  local runtime="$1"
  local out_dir="$2"

  rm -rf "$out_dir"

  dotnet publish AvaloniaApp.Desktop.csproj \
    -r "$runtime" \
    -c Release \
    --sc \
    -p:PublishTrimmed=false \
    -o "$out_dir"

  if [ $? -ne 0 ]; then
    echo "Error: dotnet publish failed"
    exit 1
  fi
}

# Create a zip archive of the output directory
# Usage: create_zip <output_dir> <zip_file>
create_zip() {
  local out_dir="$1"
  local zip_file="$2"

  rm -f "$zip_file"
  powershell.exe -Command "Compress-Archive -Path '$out_dir' -DestinationPath '$zip_file'"
  echo "Created: $zip_file"
}
