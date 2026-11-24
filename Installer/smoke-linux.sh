#!/usr/bin/env bash
set -euo pipefail

archive="$(realpath "${1:?Usage: smoke-linux.sh ARCHIVE.zip}")"
app_name="$(basename "$archive" .zip)"
if [[ ! "$app_name" =~ ^KafkaLens-([0-9]+\.[0-9]+\.[0-9]+)-linux-x64$ ]]; then
    echo "Unexpected Linux artifact name: $app_name" >&2
    exit 1
fi
window_title="KafkaLens ${BASH_REMATCH[1]}"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT
unzip -q "$archive" -d "$work_dir"
app_dir="$work_dir/$app_name"
for executable in KafkaLens KafkaLens.Updater; do
    if [[ ! -x "$app_dir/$executable" ]]; then
        echo "Missing or non-executable payload: $executable" >&2
        exit 1
    fi
done
for file in libhostfxr.so libcoreclr.so appsettings.json; do
    if [[ ! -s "$app_dir/$file" ]]; then
        echo "Missing bundled runtime or application file: $file" >&2
        exit 1
    fi
done

export DO_NOT_TRACK=1 LC_ALL=C
export XDG_DATA_HOME="$work_dir/user-data"
export XDG_CONFIG_HOME="$work_dir/user-config"
export XDG_CACHE_HOME="$work_dir/user-cache"
export XDG_STATE_HOME="$work_dir/user-state"
mkdir -p "$XDG_DATA_HOME" "$XDG_CONFIG_HOME" "$XDG_CACHE_HOME" "$XDG_STATE_HOME"
cd "$work_dir"

xvfb-run -a bash -s -- "$app_dir/KafkaLens" "$window_title" <<'DISPLAY_TEST'
set -euo pipefail

"$1" > launch.log 2>&1 &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true; wait "$app_pid" 2>/dev/null || true' EXIT
for ((attempt = 0; attempt < 30; attempt++)); do
    if ! kill -0 "$app_pid" 2>/dev/null; then
        break
    fi
    if xwininfo -name "$2" 2>/dev/null | grep -F 'Map State: IsViewable' >/dev/null; then
        echo "KafkaLens displayed its main window from the extracted release ZIP."
        exit 0
    fi
    sleep 1
done

cat launch.log >&2
echo "KafkaLens did not display its main window." >&2
exit 1
DISPLAY_TEST
