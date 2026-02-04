#! /bin/bash -e

# Source version configuration
source "$(dirname "$0")/publish_params.sh"

rm -rf "$OUT_DIR"/*

dotnet publish AvaloniaApp.Desktop.csproj \
  -r osx-arm64 \
  -c Release \
  --sc \
  -p:PublishTrimmed=false \
  -o "$OUT_DIR"_mac

if [ $? -ne 0 ]; then
  echo "Error: dotnet publish failed"
  exit 1
fi