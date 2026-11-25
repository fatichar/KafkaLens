#!/usr/bin/env bash
# Regenerates website screenshots and the demo video into docs/assets.
# Requires: .NET 10 SDK, python3 + Pillow + numpy + edge-tts, ffmpeg, Fonts: Inter, JetBrains Mono (Linux).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
work="${1:-$(mktemp -d)}"
logo="$repo/UI/KafkaLens.UI/Assets/KafkaLens_logo_cyan_512.png"
shots="$repo/docs/assets/images/screenshots"
video="$repo/docs/assets/video"
export FONTCONFIG_FILE="$here/fonts.conf"

dotnet build "$here/DocsMedia.csproj" -c Release -nologo -v q
app="$here/bin/Release/net10.0/DocsMedia.dll"

rm -rf "$work/raw" "$work/final" "$work/video"
dotnet "$app" "$work/raw" screens Light
dotnet "$app" "$work/raw" screens Dark
dotnet "$app" "$work/video" video Light

python3 "$here/postprocess.py" "$work/raw" "$work/final" "$logo"
mkdir -p "$shots" "$video"
cp "$work"/final/*.webp "$shots/"
cp "$work/final/main.png" "$shots/main.png"   # og:image / social cards need PNG

python3 "$here/make_video.py" "$work/video/frames" "$logo" "$video/kafkalens-demo.mp4"
ffmpeg -loglevel error -y -ss 2.5 -i "$video/kafkalens-demo.mp4" -frames:v 1 -vf scale=1280:-1 -q:v 80 "$video/kafkalens-demo-poster.webp"
echo "Media written to $shots and $video (work dir: $work)"
