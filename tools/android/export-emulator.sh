#!/usr/bin/env bash
# Exports the debug APK for the x86_64 emulator ("Android Emulator" preset, gradle build, signed
# with the editor's debug key) to build/client/android/PrehistoricTribes-emulator.apk. Being a
# debug build, it accepts the platform check argument (--platform-check, see docs/BUILD.md). Install
# and start it with tools/android/smoke.sh.
#
# usage: tools/android/export-emulator.sh [OUTPUT_APK]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/../build/common.sh"

output="${1:-$ROOT/build/client/android/PrehistoricTribes-emulator.apk}"
lock_exports
sync_version
bash "$ROOT/tools/android/install-build-template.sh"
godot_export debug "Android Emulator" "$output"
verify_apk "$output"
echo "Built $output"
