#!/usr/bin/env bash
# Fails when views or view models type a color or a font size instead of using Themes/Tokens.axaml.
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

# XAML in Views and Themes; only Tokens.axaml defines colors (App.axaml holds Fluent's own palette).
xaml=$(find "$app/Views" "$app/Themes" -name '*.axaml' ! -name 'Tokens.axaml')

colors=$(grep -nE '#[0-9A-Fa-f]{3,8}\b|rgba?\(' $xaml 2>/dev/null)
[ -n "$colors" ] && report "Color literals in XAML (use a Ui*Brush token):" "$colors"

sizes=$(grep -nE 'FontSize="[0-9]' $xaml 2>/dev/null)
[ -n "$sizes" ] && report "Font sizes in XAML (use a text class or a UiFontSize token):" "$sizes"

# Code draws with UiPalette and ActivityPalette, which read the tokens.
code=$(grep -rnE 'Color\.Parse|Brush\.Parse|new SolidColorBrush|Color\.From(Rgb|Argb|UInt32)|\bColors\.|\bBrushes\.' \
    --include='*.cs' "$app/Views" "$app/ViewModels" 2>/dev/null)
[ -n "$code" ] && report "Colors built in code (use UiPalette or ActivityPalette):" "$code"

# View models expose states; XAML maps them to tokens with classes or a converter.
brushes=$(grep -rnE '\bI?Brush\b|\bIBrush\b' --include='*.cs' "$app/ViewModels" 2>/dev/null)
[ -n "$brushes" ] && report "Brushes in view models (expose a state instead):" "$brushes"

[ "$status" -eq 0 ] && echo "UI tokens check passed."
exit "$status"
