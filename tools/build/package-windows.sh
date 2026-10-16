#!/usr/bin/env bash
# Builds the Windows release: exports the "Windows" preset into
# build/release/windows/PrehistoricTribes/ (PrehistoricTribes.exe with the game data embedded, and
# the .NET assemblies in data_Tribes.Client_windows_x86_64/), zips the folder's contents as
# build/release/PrehistoricTribes-<version>-windows.zip (the layout the in-game updater swaps into
# the install folder, see docs/UPDATES.md), then unpacks the zip elsewhere and starts the game
# once with --screenshot to prove the package runs.
#
# usage: tools/build/package-windows.sh [--no-run]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

RUN_TIMEOUT_SECONDS=180
run_check=true
if [ "${1:-}" = "--no-run" ]; then
  run_check=false
elif [ -n "${1:-}" ]; then
  die "usage: $0 [--no-run]"
fi

lock_exports
sync_version
stage="$RELEASE_DIR/windows/PrehistoricTribes"
rm -rf "$stage"
godot_export release "Windows" "$stage/PrehistoricTribes.exe"

# The list of the game's files (the list itself included): the in-game updater replaces and
# removes only the files on it, so a game unpacked into a shared folder keeps the user's files.
list=install-files.txt
( cd "$stage" && { find . -type f ! -path "./$list" -printf '%P\n'; echo "$list"; } | LC_ALL=C sort ) > "$RELEASE_DIR/windows/$list"
mv "$RELEASE_DIR/windows/$list" "$stage/$list"
echo "Listed $(wc -l < "$stage/$list") files in $list"

zip="$RELEASE_DIR/PrehistoricTribes-$APP_VERSION-windows.zip"
rm -f "$zip"
"$JAVA_HOME_17/bin/jar.exe" --create --no-manifest --file "$(cygpath -w "$zip")" -C "$(cygpath -w "$stage")" .
echo "Packed $zip ($(stat -c %s "$zip") bytes)"

if $run_check; then
  check_dir="$RELEASE_DIR/windows/check"
  shot="$RELEASE_DIR/windows/check.png"
  rm -rf "$check_dir" "$shot"
  mkdir -p "$check_dir"
  unzip -q "$zip" -d "$check_dir"
  echo "Starting the unpacked game once"
  GODOT="$check_dir/PrehistoricTribes.exe" timeout "$RUN_TIMEOUT_SECONDS" bash "$ROOT/tools/godot-bg.sh" \
    -- "--screenshot=$(cygpath -m "$shot")" \
    || die "the packaged game did not exit cleanly"
  [ -f "$shot" ] || die "the packaged game started but saved no screenshot"
  echo "The packaged game ran and saved $shot"
fi
