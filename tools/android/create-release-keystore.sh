#!/usr/bin/env bash
# Creates the Android release signing key: secrets/android-release.keystore (PKCS12, RSA 4096)
# and secrets/android-signing.json with its alias and a random password (store and key share
# it, as Godot's gradle build expects). Both files are readable only by the current Windows
# user. The password is never printed and never passed on a command line.
#
# Run once. It refuses to overwrite: every update APK must be signed with the same key, so a lost
# or replaced key means players have to uninstall and reinstall the game. Back both files up
# offline.
#
# usage: tools/android/create-release-keystore.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
JAVA_HOME_17="${JAVA_HOME_17:-$ROOT/tools/jdk17_unz/jdk-17.0.20.1+1}"
KEYTOOL="$JAVA_HOME_17/bin/keytool.exe"
SECRETS="$ROOT/secrets"
KEYSTORE="$SECRETS/android-release.keystore"
SIGNING="$SECRETS/android-signing.json"
ALIAS="prehistoric-tribes"
PASSWORD_LENGTH=40
VALIDITY_DAYS=10000

[ -f "$KEYTOOL" ] || { echo "error: keytool not found: $KEYTOOL" >&2; exit 1; }
if [ -e "$KEYSTORE" ] || [ -e "$SIGNING" ]; then
  echo "error: $KEYSTORE or $SIGNING already exists; the release key is never replaced" >&2
  exit 1
fi
mkdir -p "$SECRETS"

# tr ends with SIGPIPE once head has enough characters; that is expected.
password="$(LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom 2> /dev/null | head -c "$PASSWORD_LENGTH" || true)"
[ "${#password}" -eq "$PASSWORD_LENGTH" ] || { echo "error: could not generate a password" >&2; exit 1; }

# Only the current user may read the files, like secrets/update-signing-key.pem.
restrict() {
  MSYS_NO_PATHCONV=1 icacls "$(cygpath -w "$1")" /inheritance:r /grant:r "$USERDOMAIN\\$USERNAME:F" > /dev/null
}

# If keytool fails, remove the half-made pair so that the script can run again.
cleanup_on_failure() {
  if [ ! -f "$KEYSTORE" ]; then
    rm -f "$SIGNING"
  fi
}
trap cleanup_on_failure EXIT

umask 077
printf '{\n  "keystore": "android-release.keystore",\n  "alias": "%s",\n  "password": "%s"\n}\n' "$ALIAS" "$password" > "$SIGNING"
restrict "$SIGNING"

TRIBES_KEYSTORE_PASSWORD="$password" "$KEYTOOL" -genkeypair -noprompt \
  -keystore "$(cygpath -w "$KEYSTORE")" -storetype PKCS12 \
  -alias "$ALIAS" -keyalg RSA -keysize 4096 -validity "$VALIDITY_DAYS" \
  -dname "CN=Prehistoric Tribes, O=Prehistoric Tribes Remake" \
  -storepass:env TRIBES_KEYSTORE_PASSWORD -keypass:env TRIBES_KEYSTORE_PASSWORD
restrict "$KEYSTORE"
unset password

echo "Created $KEYSTORE and $SIGNING (alias $ALIAS). Back both up offline."
