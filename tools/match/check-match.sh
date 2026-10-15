#!/usr/bin/env bash
# Plays the match screen's interface scripts (client/dev/scripts) in the editor build and reports
# each one's SCRIPT_DONE / SCRIPT_FAIL line, then runs the in-engine construction site check.
# Captures go to OUT_DIR (default work/screenshots/match). Build the client first
# (dotnet build client/Tribes.Client.csproj).
# usage: tools/match/check-match.sh [OUT_DIR]
set -u
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${1:-$ROOT/work/screenshots/match}"
mkdir -p "$OUT"
godot_bg="$ROOT/tools/godot-bg.sh"   # Godot without a visible window (GODOT picks another executable)
win() { cygpath -m "$1" 2>/dev/null || echo "$1"; }
status=0
for script in "$ROOT"/client/dev/scripts/*.txt; do
  # The first line says how to start the game for the script.
  args=$(head -1 "$script" | sed -n 's/^# Run[^:]*: *//p')
  line=$(timeout 900 bash "$godot_bg" --path "$ROOT/client" --resolution 1280x720 -- $args \
    --match-script="$(win "$script")" --match-script-out="$(win "$OUT")" 2>&1 | grep -E "SCRIPT_(DONE|FAIL)" | tail -1)
  echo "$(basename "$script"): ${line:-no result}"
  case "$line" in SCRIPT_DONE*) ;; *) status=1 ;; esac
done
line=$(timeout 900 bash "$godot_bg" --path "$ROOT/client" --resolution 1280x720 -- --ui-screen=../match/match --match-mission=7 \
  --match-skip-scenes --session-seed=1 --site-check 2>&1 | grep -E "SITE_CHECK_DONE" | tail -1)
echo "site check: ${line:-no result}"
case "$line" in *failures=0*) ;; *) status=1 ;; esac
exit $status
