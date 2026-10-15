#!/usr/bin/env bash
# Installs an APK on an emulator or device, starts the game and saves a screenshot.
# usage: smoke.sh SERIAL APK OUT_PNG [WAIT_MARKER]
set -u
SERIAL=$1; APK=$2; OUT=$3; MARKER=${4:-TRIBES_BOOT_OK}
ADB="${ADB:-$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe}"
PKG=com.prehistoric.tribes.remake
for i in $(seq 1 120); do
  [ "$("$ADB" -s "$SERIAL" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ] && break
  sleep 5
done
"$ADB" -s "$SERIAL" shell getprop sys.boot_completed | grep -q 1 || { echo "not booted"; exit 1; }
"$ADB" -s "$SERIAL" install -r "$APK" 2>&1 | tail -1
"$ADB" -s "$SERIAL" logcat -c
"$ADB" -s "$SERIAL" shell am start -n "$PKG/com.godot.game.GodotAppLauncher" 2>&1 | tail -1
for i in $(seq 1 60); do
  "$ADB" -s "$SERIAL" logcat -d 2>/dev/null | grep -q "$MARKER\|FATAL EXCEPTION" && break
  sleep 2
done
"$ADB" -s "$SERIAL" logcat -d 2>/dev/null | grep -E "$MARKER|FATAL EXCEPTION|godot.*ERROR" | head -10
sleep 3
MSYS_NO_PATHCONV=1 "$ADB" -s "$SERIAL" exec-out screencap -p > "$OUT"
ls -la "$OUT"
