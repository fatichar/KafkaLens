#! /bin/bash -e

# Source version configuration
source "$(dirname "$0")/publish_params.sh"

rm -rf "$OUT_DIR"/*

dotnet publish AvaloniaApp.Desktop.csproj \
  -r win-x64 \
  -c Release \
  --sc \
  -p:PublishTrimmed=false \
  -o "$OUT_DIR"_win

if [ $? -ne 0 ]; then
  echo "Error: dotnet publish failed"
  exit 1
fi

"/C/Program Files (x86)/Inno Setup 6/ISCC.exe" ../../Installer/install_windows.iss