#!/usr/bin/env bash
# Installs Godot's Android gradle build template into client/android/build the way the editor's
# "Project > Install Android Build Template" does (client/android/.build_version holds the Godot
# version, build/.gdignore keeps the editor from importing it), then adds the game's own Android
# code from client/android/platform/:
#   java/ -> build/src/main/java/        (UpdateInstaller, UpdateFileProvider, MulticastLockHelper)
#   res/  -> build/res/                  (FileProvider paths)
#   AndroidManifest.application.xml      inserted into <application> of build/src/main/AndroidManifest.xml
# and makes Gradle run without a resident daemon and with a smaller heap.
#
# usage: tools/android/install-build-template.sh [--force]
#   An installed template of the same Godot version is kept (its Gradle caches stay) and only the
#   game's files are refreshed; --force, a missing template or another Godot version reinstalls it.
# env:   GODOT_HOME (folder of the Godot editor), GODOT_VERSION (default 4.7.2.stable.mono)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
GODOT_HOME="${GODOT_HOME:-$ROOT/work/godot-4.7.2/Godot_v4.7.2-stable_mono_win64}"
GODOT_VERSION="${GODOT_VERSION:-4.7.2.stable.mono}"
SOURCE_ZIP="$GODOT_HOME/editor_data/export_templates/$GODOT_VERSION/android_source.zip"
ANDROID_DIR="$ROOT/client/android"
BUILD_DIR="$ANDROID_DIR/build"
PLATFORM_DIR="$ANDROID_DIR/platform"
GRADLE_HEAP="${GRADLE_HEAP:-2048m}"

force=false
if [ "${1:-}" = "--force" ]; then
  force=true
elif [ -n "${1:-}" ]; then
  echo "usage: $0 [--force]" >&2
  exit 2
fi

[ -f "$SOURCE_ZIP" ] || { echo "error: Android build template not found: $SOURCE_ZIP" >&2; exit 1; }
SNIPPET="$PLATFORM_DIR/AndroidManifest.application.xml"
for required in "$PLATFORM_DIR/java" "$PLATFORM_DIR/res" "$SNIPPET"; do
  [ -e "$required" ] || { echo "error: missing $required" >&2; exit 1; }
done

installed=""
if [ -f "$ANDROID_DIR/.build_version" ]; then
  installed="$(tr -d '\r\n' < "$ANDROID_DIR/.build_version")"
fi

if $force || [ ! -d "$BUILD_DIR" ] || [ "$installed" != "$GODOT_VERSION" ]; then
  echo "Installing the Android build template $GODOT_VERSION into $BUILD_DIR"
  # The version file is written last, so an interrupted install is redone on the next run.
  rm -f "$ANDROID_DIR/.build_version"
  rm -rf "$BUILD_DIR"
  mkdir -p "$BUILD_DIR"
  printf '\n' > "$BUILD_DIR/.gdignore"
  unzip -q "$SOURCE_ZIP" -d "$BUILD_DIR"
  printf '%s\n' "$GODOT_VERSION" > "$ANDROID_DIR/.build_version"
else
  echo "Android build template $GODOT_VERSION already installed; refreshing the game's files"
fi

# The game's Java code (copied fresh, so classes removed from platform/ disappear) and resources.
mkdir -p "$BUILD_DIR/src/main/java" "$BUILD_DIR/res"
rm -rf "$BUILD_DIR/src/main/java/com/prehistoric"
cp -R "$PLATFORM_DIR/java/." "$BUILD_DIR/src/main/java/"
cp -R "$PLATFORM_DIR/res/." "$BUILD_DIR/res/"

# The manifest: the template's own, plus the game's <application> entries.
manifest="$BUILD_DIR/src/main/AndroidManifest.xml"
unzip -p "$SOURCE_ZIP" src/main/AndroidManifest.xml \
  | awk -v snippet="$SNIPPET" '
      /<\/application>/ && !inserted {
        while ((getline line < snippet) > 0) print line
        inserted = 1
      }
      { print }
      END { if (!inserted) exit 1 }' > "$manifest.tmp"
mv "$manifest.tmp" "$manifest"
grep -q "UpdateFileProvider" "$manifest" || { echo "error: the FileProvider is missing from $manifest" >&2; exit 1; }

# Gradle settings: the template's, with a smaller heap, no resident daemon and in-process Kotlin,
# so an export leaves no Java processes behind.
unzip -p "$SOURCE_ZIP" gradle.properties \
  | sed -e "s/^org\.gradle\.jvmargs=.*/org.gradle.jvmargs=-Xmx$GRADLE_HEAP -Dfile.encoding=UTF-8/" > "$BUILD_DIR/gradle.properties"
cat >> "$BUILD_DIR/gradle.properties" <<'EOF'

# Added by tools/android/install-build-template.sh.
org.gradle.daemon=false
kotlin.compiler.execution.strategy=in-process
EOF

echo "Done: $(tr -d '\r\n' < "$ANDROID_DIR/.build_version") in $BUILD_DIR"
