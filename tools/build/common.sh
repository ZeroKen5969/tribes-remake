#!/usr/bin/env bash
# Shared parts of the build scripts (tools/build/package-*.sh, tools/android/export-emulator.sh):
# paths, the Godot editor, the version from client/version.json and the headless export.
# Source this file; it does not do anything on its own.
#
# Environment overrides: GODOT_HOME (Godot editor folder), GODOT (editor executable),
# ANDROID_SDK_ROOT, JAVA_HOME_17.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLIENT_DIR="$ROOT/client"
RELEASE_DIR="$ROOT/build/release"
VERSION_FILE="$CLIENT_DIR/version.json"
GODOT_HOME="${GODOT_HOME:-$ROOT/work/godot-4.7.2/Godot_v4.7.2-stable_mono_win64}"
GODOT="${GODOT:-$GODOT_HOME/Godot_v4.7.2-stable_mono_win64_console.exe}"
ANDROID_SDK_ROOT="$(cygpath -u "${ANDROID_SDK_ROOT:-${LOCALAPPDATA:-C:/missing}/Android/Sdk}")"
JAVA_HOME_17="${JAVA_HOME_17:-$ROOT/tools/jdk17_unz/jdk-17.0.20.1+1}"

die() {
  echo "error: $*" >&2
  exit 1
}

# json_field FILE NAME: the string or number value of NAME in a flat JSON file of this repository.
json_field() {
  sed -n -E "s/.*\"$2\"[[:space:]]*:[[:space:]]*(\"([^\"]*)\"|([0-9]+)).*/\2\3/p" "$1" | head -n 1
}

# Sets APP_VERSION, APP_VERSION_CODE and APP_CONTENT_REVISION from client/version.json.
read_version() {
  [ -f "$VERSION_FILE" ] || die "missing $VERSION_FILE"
  APP_VERSION="$(json_field "$VERSION_FILE" version)"
  APP_VERSION_CODE="$(json_field "$VERSION_FILE" versionCode)"
  APP_CONTENT_REVISION="$(json_field "$VERSION_FILE" contentRevision)"
  [[ "$APP_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || die "version in $VERSION_FILE must be X.Y.Z or X.Y.Z-label, got '$APP_VERSION'"
  [[ "$APP_VERSION_CODE" =~ ^[1-9][0-9]{0,8}$ ]] || die "versionCode in $VERSION_FILE must be a positive whole number, got '$APP_VERSION_CODE'"
  [[ "$APP_CONTENT_REVISION" =~ ^[0-9]{1,8}$ ]] || die "contentRevision in $VERSION_FILE must be 0 to 99999999, got '$APP_CONTENT_REVISION'"
}

# replace_line FILE REGEX LINE: replaces every line matching REGEX with LINE, touching the file
# only when something differs.
replace_line() {
  grep -Eq "$2" "$1" || die "$1 has no line matching $2"
  if grep -E "$2" "$1" | grep -qvxF "$3"; then
    sed -i -E "s|$2|$3|" "$1"
  fi
}

# Copies the version into project.godot (config/version, read by the game) and
# export_presets.cfg (Android version name and code, Windows file and product version).
sync_version() {
  read_version
  local windows_version="${APP_VERSION%%-*}.0"
  replace_line "$CLIENT_DIR/project.godot" '^config/version=.*' "config/version=\"$APP_VERSION\""
  replace_line "$CLIENT_DIR/export_presets.cfg" '^version/code=.*' "version/code=$APP_VERSION_CODE"
  replace_line "$CLIENT_DIR/export_presets.cfg" '^version/name=.*' "version/name=\"$APP_VERSION\""
  replace_line "$CLIENT_DIR/export_presets.cfg" '^application/product_version=.*' "application/product_version=\"$windows_version\""
  replace_line "$CLIENT_DIR/export_presets.cfg" '^application/file_version=.*' "application/file_version=\"$windows_version\""
  echo "Version $APP_VERSION (code $APP_VERSION_CODE, content revision $APP_CONTENT_REVISION)"
}

# forget_password_in_log LOG: deletes LOG if it contains the release signing password (the
# password reaches grep through stdin, never as an argument).
forget_password_in_log() {
  if [ -n "${GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD:-}" ] && [ -f "$1" ] \
    && printf '%s\n' "$GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD" | grep -qFf - "$1"; then
    rm -f "$1"
    echo "warning: deleted $1 because it contained the signing password" >&2
  fi
}

# godot_export release|debug PRESET OUTPUT: exports the preset headless to OUTPUT. Godot's output
# goes to build/logs/export-<preset>-<time>.log. Godot still writes a package when the C# build fails
# (it only reports "completed with warnings"), so any export error or warning counts as a failure
# and the package is deleted.
godot_export() {
  local mode=$1 preset=$2 output=$3
  local prefix="$ROOT/build/logs/export-${preset// /-}"
  local log
  log="$prefix-$(date +%Y%m%d-%H%M%S).log"
  [ -f "$GODOT" ] || die "Godot editor not found: $GODOT"
  # Godot imports the art before it builds the C# project, so the copy from data/assets comes first.
  bash "$ROOT/tools/sync-assets.sh" || die "could not copy data/assets to client/assets"
  mkdir -p "$(dirname "$output")" "$(dirname "$log")"
  # Keep the newest logs of this preset only.
  ls -t "$prefix"-*.log 2> /dev/null | tail -n +"$EXPORT_LOGS_KEPT" | xargs -r rm -f || true
  rm -f "$output"
  echo "Exporting \"$preset\" ($mode) to $output (log: $log)"
  local status=0
  # MSBuild worker processes kept for reuse, and the C# compiler server (VBCSCompiler) a build starts
  # when none runs yet, inherit Godot's output and keep it open, so the export would not return until
  # they time out (about ten minutes); the build runs without them.
  MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false "$GODOT" --headless --path "$(cygpath -m "$CLIENT_DIR")" "--export-$mode" "$preset" "$(cygpath -m "$output")" > "$log" 2>&1 || status=$?
  forget_password_in_log "$log"
  if [ "$status" -ne 0 ]; then
    rm -f "$output"
    grep -E "ERROR|error CS" "$log" | head -n 20 >&2 || true
    die "Godot export of \"$preset\" failed; see $log"
  fi
  if grep -Eq "ERROR: Export|Failed to build project|completed with (warnings|errors)" "$log"; then
    rm -f "$output"
    grep -E "ERROR: Export|Failed to build|completed with" "$log" | head -n 20 >&2 || true
    die "Godot export of \"$preset\" reported problems (for C# errors run: dotnet build client/Tribes.Client.csproj); see $log"
  fi
  [ -f "$output" ] || die "Godot export of \"$preset\" wrote no $output; see $log"
}

# Stops Gradle daemons left by an Android export (the build template turns the daemon off,
# this catches daemons from other settings).
stop_gradle() {
  local gradlew="$CLIENT_DIR/android/build/gradlew.bat"
  if [ -f "$gradlew" ]; then
    JAVA_HOME="$(cygpath -w "$JAVA_HOME_17")" "$gradlew" --stop -q > /dev/null 2>&1 || true
  fi
}

# Exports share client/.godot, client/android/build and build/logs, so only one runs at a time:
# lock_exports waits for build/.export-lock, takes it, and gives it back (and stops Gradle) when
# the script exits. A lock whose owner process is gone is taken over.
EXPORT_LOCK="$ROOT/build/.export-lock"
EXPORT_LOCK_WAIT_SECONDS=3600
EXPORT_LOGS_KEPT=20

# The lock records the Windows process id: Git Bash process ids are not visible across separate
# Git Bash sessions, Windows ones are (ps -W lists every process, WINPID is its fourth column).
lock_owner_alive() {
  ps -W 2> /dev/null | awk -v pid="$1" 'NR > 1 && $4 == pid { found = 1 } END { exit !found }'
}

lock_exports() {
  local waited=0 owner me
  me="$(cat "/proc/$$/winpid")"
  mkdir -p "$ROOT/build"
  until mkdir "$EXPORT_LOCK" 2> /dev/null; do
    owner="$(cat "$EXPORT_LOCK/winpid" 2> /dev/null || true)"
    if [ -n "$owner" ] && ! lock_owner_alive "$owner"; then
      rm -rf "$EXPORT_LOCK"
      continue
    fi
    [ "$waited" -eq 0 ] && echo "Waiting for another export to finish ($EXPORT_LOCK)"
    sleep 10
    waited=$((waited + 10))
    [ "$waited" -lt "$EXPORT_LOCK_WAIT_SECONDS" ] || die "another export has held $EXPORT_LOCK for an hour"
  done
  echo "$me" > "$EXPORT_LOCK/winpid"
  EXPORT_LOCK_OWNER="$me"
  trap unlock_exports EXIT
}

unlock_exports() {
  stop_gradle
  if [ "$(cat "$EXPORT_LOCK/winpid" 2> /dev/null || true)" = "${EXPORT_LOCK_OWNER:-}" ]; then
    rm -rf "$EXPORT_LOCK"
  fi
}

# apksigner from the newest installed build tools.
apksigner_path() {
  local tools
  tools="$(ls -d "$ANDROID_SDK_ROOT"/build-tools/*/ 2> /dev/null | sort -V | tail -n 1)"
  [ -n "$tools" ] && [ -f "${tools}apksigner.bat" ] || die "apksigner not found under $ANDROID_SDK_ROOT/build-tools"
  echo "${tools}apksigner.bat"
}

# verify_apk APK: checks the signature and prints the signer (never any secret).
verify_apk() {
  JAVA_HOME="$(cygpath -w "$JAVA_HOME_17")" "$(apksigner_path)" verify --print-certs "$(cygpath -w "$1")" \
    | grep -E "^Signer #1 certificate (DN|SHA-256)" || die "APK signature check failed for $1"
}
