#!/usr/bin/env bash
set -euo pipefail

version="${1:?Usage: package-macos.sh VERSION arm64|x64}"
arch="${2:?Usage: package-macos.sh VERSION arm64|x64}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Expected a three-component macOS version." >&2
    exit 1
fi
case "$arch" in
    arm64) macho_arch=arm64 ;;
    x64) macho_arch=x86_64 ;;
    *) echo "Unsupported macOS architecture: $arch" >&2; exit 1 ;;
esac

: "${MACOS_CERTIFICATE_BASE64:?Missing Developer ID certificate}"
: "${MACOS_CERTIFICATE_PASSWORD:?Missing Developer ID certificate password}"
: "${MACOS_NOTARY_KEY_BASE64:?Missing App Store Connect API key}"
: "${MACOS_NOTARY_KEY_ID:?Missing App Store Connect key ID}"
: "${MACOS_NOTARY_ISSUER_ID:?Missing App Store Connect issuer ID}"
if [[ "${MACOS_SIGNING_IDENTITY:-}" != "Developer ID Application: "?* ]]; then
    echo "MACOS_SIGNING_IDENTITY must be a Developer ID Application identity." >&2
    exit 1
fi

installer_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
releases_dir="$installer_dir/../Releases"
publish_dir="$releases_dir/KafkaLens_mac"
for executable in KafkaLens KafkaLens.Updater; do
    test -x "$publish_dir/$executable"
    lipo -verify_arch "$macho_arch" "$publish_dir/$executable"
done

original_umask="$(umask)"
umask 077
work_dir="$(mktemp -d)"
keychain="$work_dir/signing.keychain-db"
cleanup() {
    security delete-keychain "$keychain" >/dev/null 2>&1 || true
    rm -rf "$work_dir"
}
trap cleanup EXIT

printf '%s' "$MACOS_CERTIFICATE_BASE64" | base64 -D > "$work_dir/certificate.p12"
printf '%s' "$MACOS_NOTARY_KEY_BASE64" | base64 -D > "$work_dir/notary-key.p8"
keychain_password="$(openssl rand -hex 32)"
security create-keychain -p "$keychain_password" "$keychain"
security set-keychain-settings -lut 3600 "$keychain"
security unlock-keychain -p "$keychain_password" "$keychain"
security import "$work_dir/certificate.p12" -k "$keychain" -P "$MACOS_CERTIFICATE_PASSWORD" -T /usr/bin/codesign
security set-key-partition-list -S apple-tool:,apple: -s -k "$keychain_password" "$keychain"
rm "$work_dir/certificate.p12"
unset MACOS_CERTIFICATE_BASE64 MACOS_CERTIFICATE_PASSWORD MACOS_NOTARY_KEY_BASE64 keychain_password
umask "$original_umask"

app="$work_dir/KafkaLens.app"
ditto "$publish_dir" "$app/Contents/MacOS"
cp "$installer_dir/macos-info.plist" "$app/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$app/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$version" "$app/Contents/Info.plist"

sign_options=(--force --timestamp --options runtime --keychain "$keychain" --sign "$MACOS_SIGNING_IDENTITY")
entitlements="$installer_dir/macos-entitlements.plist"
find "$app/Contents/MacOS" -type f -print0 > "$work_dir/bundle-files"
while IFS= read -r -d '' path; do
    [[ "$path" == "$app/Contents/MacOS/KafkaLens" ]] && continue
    if [[ "$(file -b "$path")" != *Mach-O* ]]; then
        continue
    fi
    if [[ "$path" == "$app/Contents/MacOS/KafkaLens.Updater" ]]; then
        codesign "${sign_options[@]}" --entitlements "$entitlements" "$path"
    else
        codesign "${sign_options[@]}" "$path"
    fi
    codesign --verify --strict "$path"
done < "$work_dir/bundle-files"
codesign "${sign_options[@]}" --entitlements "$entitlements" "$app"
codesign --verify --deep --strict --verbose=2 "$app"

notary_options=(--key "$work_dir/notary-key.p8" --key-id "$MACOS_NOTARY_KEY_ID" --issuer "$MACOS_NOTARY_ISSUER_ID")
ditto -c -k --keepParent "$app" "$work_dir/submission.zip"
result="$work_dir/notary-result.plist"
submit_exit=0
xcrun notarytool submit "$work_dir/submission.zip" "${notary_options[@]}" \
    --wait --timeout 30m --output-format plist > "$result" || submit_exit=$?
submission_id="$(plutil -extract id raw "$result" 2>/dev/null)" || submission_id=
status="$(plutil -extract status raw "$result" 2>/dev/null)" || status=
if [[ "$submit_exit" -ne 0 || -z "$submission_id" || "$status" != Accepted ]]; then
    echo "Apple notarization failed (exit $submit_exit, submission ${submission_id:-unavailable}, status ${status:-unavailable})." >&2
    if [[ -n "$submission_id" ]]; then
        xcrun notarytool log "$submission_id" "${notary_options[@]}" >&2 || true
    fi
    exit 1
fi
echo "Apple notarization $submission_id: $status"
xcrun stapler staple "$app"

archive="KafkaLens-$version-macos-$arch.zip"
ditto -c -k --keepParent "$app" "$work_dir/$archive"
ditto -x -k "$work_dir/$archive" "$work_dir/verify"
verified_app="$work_dir/verify/KafkaLens.app"
codesign --verify --deep --strict --verbose=2 "$verified_app"
xcrun stapler validate "$verified_app"
syspolicy_check distribution "$verified_app"
mv "$work_dir/$archive" "$releases_dir/$archive"
