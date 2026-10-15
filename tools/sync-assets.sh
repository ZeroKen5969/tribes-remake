#!/usr/bin/env bash
# Copies the game's art, music and fonts from the content repository (data/assets) to
# client/assets, where Godot reads them as res://assets. client/assets is a copy, never edited:
# change the files in data/assets. Files only copied when newer; run before Godot starts.
#
#   bash tools/sync-assets.sh
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$ROOT/data/assets"
TARGET="$ROOT/client/assets"
[ -d "$SOURCE" ] || { echo "error: missing $SOURCE (clone the content repository into data/)" >&2; exit 1; }
mkdir -p "$TARGET"
cp -ru "$SOURCE/." "$TARGET/"
