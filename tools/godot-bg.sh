#!/usr/bin/env bash
# Runs Godot for development tests without its windows showing on screen or taking the keyboard
# focus (tools/Tribes.GodotBackground). Same arguments, output and exit code as Godot itself;
# rendering and screenshots still work. Every test or script that starts Godot with a window uses this.
#
#   bash tools/godot-bg.sh [godot args...]
#
# GODOT picks another Godot executable (default: the editor console executable in work/).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
godot="${GODOT:-$root/work/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}"
out="$root/work/bin/godot-bg"
launcher="$out/Tribes.GodotBackground.exe"
source_dir="$root/tools/Tribes.GodotBackground"

if [ ! -f "$launcher" ] || [ "$source_dir/Program.cs" -nt "$launcher" ]; then
  lock="$root/work/.godot-bg-build"
  until mkdir "$lock" 2> /dev/null; do sleep 1; done
  trap 'rmdir "$lock" 2> /dev/null || true' EXIT
  if [ ! -f "$launcher" ] || [ "$source_dir/Program.cs" -nt "$launcher" ]; then
    dotnet build "$source_dir" -c Release -o "$out" > "$root/work/godot-bg-build.log" 2>&1 \
      || { echo "godot-bg: build failed, see work/godot-bg-build.log" >&2; exit 1; }
  fi
  rmdir "$lock" 2> /dev/null || true
  trap - EXIT
fi
# Godot keeps user:// under %APPDATA%, the same folder the installed game uses on this PC, so test
# runs get their own one and never touch the player's saves, progress or settings.
# TRIBES_TEST_APPDATA=DIR uses another test folder (a test that changes settings keeps them to itself);
# TRIBES_REAL_USER_DATA=1 keeps the normal folder.
if [ -z "${TRIBES_REAL_USER_DATA:-}" ]; then
  test_data="${TRIBES_TEST_APPDATA:-$root/work/godot-test-appdata}"
  mkdir -p "$test_data"
  APPDATA="$(cygpath -w "$test_data")"
  export APPDATA
fi
# Godot reads the art from client/assets, a copy of the content repository's data/assets.
bash "$root/tools/sync-assets.sh"
"$launcher" "$godot" "$@"
