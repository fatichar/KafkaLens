#!/usr/bin/env bash

set -e

PROPS_FILE="Directory.Build.props"
INSTALLER_FILE="Installer/install_windows.iss"

echo "INFO: Starting release workflow"

# 1. Ensure we're on master
echo "INFO: Checking current branch..."
CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
echo "INFO: Current branch is $CURRENT_BRANCH"
if [ "$CURRENT_BRANCH" != "master" ]; then
  echo "ERROR: You must be on master branch."
  exit 1
fi
echo "INFO: Branch check passed."

# 2. Ensure working tree is clean
echo "INFO: Checking working tree status..."
if [ -n "$(git status --porcelain)" ]; then
  echo "ERROR: Working directory not clean. Commit or stash changes."
  exit 1
fi
echo "INFO: Working tree is clean."

# 3. Extract version from Directory.Build.props
echo "INFO: Reading version from $PROPS_FILE..."
VERSION=$(sed -nE 's/.*<Version>([^<]+)<\/Version>.*/\1/p' "$PROPS_FILE" | head -1)
if [ -z "$VERSION" ]; then
  echo "ERROR: Could not find <Version> in $PROPS_FILE"
  exit 1
fi
echo "INFO: Parsed app version: $VERSION"

TAG="v$VERSION"
echo "INFO: Release tag will be $TAG"

# 4. Verify installer version matches
echo "INFO: Reading installer version from $INSTALLER_FILE..."
INSTALLER_VERSION=$(sed -nE 's/^[[:space:]]*AppVersion=([^[:space:]]+).*/\1/p' "$INSTALLER_FILE" | head -1)
if [ -z "$INSTALLER_VERSION" ]; then
  echo "ERROR: Could not find AppVersion in $INSTALLER_FILE"
  exit 1
fi
echo "INFO: Parsed installer version: $INSTALLER_VERSION"

if [ "$INSTALLER_VERSION" != "$VERSION" ]; then
  echo "ERROR: Version mismatch: Directory.Build.props has $VERSION but $INSTALLER_FILE has $INSTALLER_VERSION"
  exit 1
fi
echo "INFO: Version consistency check passed."

# 5. Verify download.html version matches
DOWNLOAD_HTML="docs/download.html"
echo "INFO: Reading version from $DOWNLOAD_HTML..."
HTML_VERSION=$(sed -nE 's/.*Latest Stable Release \(v([^)]+)\).*/\1/p' "$DOWNLOAD_HTML" | head -1)
if [ -z "$HTML_VERSION" ]; then
  echo "ERROR: Could not find 'Latest Stable Release (vX.Y)' in $DOWNLOAD_HTML"
  exit 1
fi
echo "INFO: Parsed download.html version: $HTML_VERSION"

if [ "$HTML_VERSION" != "$VERSION" ]; then
  echo "ERROR: Version mismatch: Directory.Build.props has $VERSION but $DOWNLOAD_HTML has $HTML_VERSION"
  exit 1
fi
echo "INFO: download.html version check passed."

# 6. Check if tag already exists
echo "INFO: Checking whether tag $TAG already exists..."
if git rev-parse "$TAG" >/dev/null 2>&1; then
  echo "ERROR: Tag $TAG already exists."
  exit 1
fi
echo "INFO: Tag $TAG does not exist yet."

# 7. Create annotated tag (opens editor for release notes)
echo "INFO: Creating annotated tag $TAG..."
git tag -a "$TAG"
echo "INFO: Tag $TAG created."

# 8. Push tag
echo "INFO: Pushing tag $TAG to origin..."
git push
git push origin "$TAG"

echo "OK: Release $TAG created and pushed."