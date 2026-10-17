#!/usr/bin/env bash
# Checks that every button can be pressed with a finger on phone-sized screens: the match screen's
# panels (client/dev/scripts/reach.txt, reach-endless.txt) and every menu screen, each in landscape
# and portrait, at the largest interface size, and with rounded corners plus the most room kept
# from the screen edges. A button counts when enough of it is on the screen inside that room (or
# its list scrolls it there) and a press on it is not taken by something over it (UI_REACH lines,
# client/src/Ui/Dev/UiReach.cs). Prints one line per run and exits non-zero when any fails.
# Build the client first (dotnet build client/Tribes.Client.csproj).
# usage: tools/match/check-reach.sh [OUT_DIR]
set -u
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${1:-$ROOT/work/screenshots/reach}"
mkdir -p "$OUT"
godot_bg="$ROOT/tools/godot-bg.sh"
win() { cygpath -m "$1" 2>/dev/null || echo "$1"; }
status=0

# NAME WINDOW INTERFACE_SCALE EDGE_MARGIN CORNER_RADIUS: each setup gets its own settings.
SETUPS=(
  "landscape 1560x720 1 0 0"
  "portrait 720x1560 1 0 0"
  "small 1280x720 1 0 0"
  "large 1560x720 3 0 0"
  "large-portrait 720x1560 3 0 0"
  "rounded 1560x720 1 24 90"
)
MENUS=("main_menu" "settings" "campaign" "campaign --ui-mission=1" "custom_game" "multiplayer --ui-tab=lan"
  "multiplayer --ui-tab=internet" "endless" "endless --ui-tab=continue" "load_game" "update" "credits"
  "lobby --lan-host --lan-quit-after=60")

for setup in "${SETUPS[@]}"; do
  read -r name window scale edge radius <<< "$setup"
  data="$OUT/appdata-$name"
  settings_dir="$data/Godot/app_userdata/史前部落 Prehistoric Tribes"
  mkdir -p "$settings_dir"
  printf '{ "InterfaceScale": %s, "EdgeMargin": %s }\n' "$scale" "$edge" > "$settings_dir/settings.json"
  corners=()
  [ "$radius" -gt 0 ] && corners=(--screen-corners="$radius")
  for script in reach reach-endless; do
    # The script's own start, at this setup's window.
    args=$(head -1 "$ROOT/client/dev/scripts/$script.txt" | sed -n 's/^# Run[^:]*: *//p' | sed "s/--ui-window=[0-9x]*/--ui-window=$window/")
    line=$(TRIBES_TEST_APPDATA="$data" timeout 900 bash "$godot_bg" --path "$ROOT/client" --resolution "$window" -- $args "${corners[@]}" \
      --match-script="$(win "$ROOT/client/dev/scripts/$script.txt")" --match-script-out="$(win "$OUT/$name")" 2>&1 \
      | grep -E "UI_REACH_PROBLEM|SCRIPT_(DONE|FAIL)" | tee "$OUT/$name-$script.log" | grep -E "SCRIPT_(DONE|FAIL)" | tail -1)
    echo "$name $script: ${line:-no result}"
    case "$line" in SCRIPT_DONE*) ;; *) status=1 ;; esac
  done
  : > "$OUT/$name-menus.log"
  for spec in "${MENUS[@]}"; do
    set -- $spec
    screen=$1
    shift
    tag="$(echo "$spec" | tr ' =' '__')"
    TRIBES_TEST_APPDATA="$data" timeout 150 bash "$godot_bg" --path "$ROOT/client" --resolution "$window" -- --ui-window="$window" \
      --ui-screen="$screen" "$@" "${corners[@]}" --ui-capture="$(win "$OUT/$name-$tag.png")" --ui-capture-delay=30 --ui-reach 2>&1 \
      | grep -E "UI_REACH" | sed "s|^|[$tag] |" >> "$OUT/$name-menus.log"
  done
  problems=$(grep -c "UI_REACH_PROBLEM" "$OUT/$name-menus.log")
  screens=$(grep -c "UI_REACH " "$OUT/$name-menus.log")
  echo "$name menus: $screens of ${#MENUS[@]} screens checked, $problems buttons out of reach"
  if [ "$problems" -ne 0 ] || [ "$screens" -ne "${#MENUS[@]}" ]; then
    status=1
  fi
done
exit $status
