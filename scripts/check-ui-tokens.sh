#!/usr/bin/env bash
# Fails when view XAML types a color or a font size instead of using Themes/Tokens.axaml.
# See "UI Conventions" in AGENTS.md.
set -u
cd "$(dirname "$0")/.."
app=AlbionDataAvalonia
status=0

report() {
    echo "$1"
    echo "$2" | sed 's/^/  /'
    status=1
}

colors=$(grep -nE '#[0-9A-Fa-f]{3,8}\b|rgba?\(' "$app"/Views/*.axaml 2>/dev/null)
[ -n "$colors" ] && report "Color literals in view XAML (use a Ui*Brush token):" "$colors"

sizes=$(grep -nE 'FontSize="[0-9]' "$app"/Views/*.axaml 2>/dev/null)
[ -n "$sizes" ] && report "Font sizes in view XAML (use a text class or a UiFontSize token):" "$sizes"

[ "$status" -eq 0 ] && echo "UI tokens check passed."
exit "$status"
