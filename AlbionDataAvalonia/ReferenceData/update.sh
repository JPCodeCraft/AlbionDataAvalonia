#!/usr/bin/env bash
# Refreshes the base reference data copies embedded in the app from the AFM CDN.
set -euo pipefail
cd "$(dirname "$0")"
trap 'rm -f -- *.gz.tmp' EXIT
base=https://cdn.albionfreemarket.com
for path in \
    ao-bin-dumps/formatted/items.json \
    ao-bin-dumps/formatted/world.json \
    ao-bin-dumps/achievements.xml \
    ao-bin-dumps/legendaryitems.json \
    ao-bin-dumps/gamedata.json \
    AlbionLocalization/processed_mobs.json \
    AlbionLocalization/processed_localization/en-US.json \
    AlbionLocalization/processed_items.json \
    AlbionLocalization/processed_spells.json; do
    file="$(basename "$path").gz"
    curl -fsSL "$base/$path" | gzip -9 -n > "$file.tmp"
    mv "$file.tmp" "$file"
done
