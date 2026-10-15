#!/usr/bin/env bash
# Builds the Android release APK (arm64-v8a, gradle build) signed with the release
# key, as build/release/PrehistoricTribes-<version>-android.apk, the file the release tool
# publishes (tools/Tribes.Release publish --apk ...).
#
# The key comes from secrets/android-signing.json and secrets/android-release.keystore
# (tools/android/create-release-keystore.sh); it reaches Godot only through the environment
# variables GODOT_ANDROID_KEYSTORE_RELEASE_PATH/USER/PASSWORD, never through export_presets.cfg.
#
# usage: tools/build/package-android.sh
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

signing="$ROOT/secrets/android-signing.json"
[ -f "$signing" ] || die "missing $signing (create it with tools/android/create-release-keystore.sh)"
keystore="$ROOT/secrets/$(json_field "$signing" keystore)"
[ -f "$keystore" ] || die "missing keystore $keystore"
alias_name="$(json_field "$signing" alias)"
[ -n "$alias_name" ] || die "$signing has no alias"

lock_exports
sync_version
bash "$ROOT/tools/android/install-build-template.sh"
output="$RELEASE_DIR/PrehistoricTribes-$APP_VERSION-android.apk"

GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$(cygpath -m "$keystore")"
GODOT_ANDROID_KEYSTORE_RELEASE_USER="$alias_name"
GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD="$(json_field "$signing" password)"
[ -n "$GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD" ] || die "$signing has no password"
export GODOT_ANDROID_KEYSTORE_RELEASE_PATH GODOT_ANDROID_KEYSTORE_RELEASE_USER GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD

godot_export release "Android" "$output"
unset GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD

verify_apk "$output"
echo "Built $output ($(stat -c %s "$output") bytes)"
