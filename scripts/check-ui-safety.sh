#!/bin/sh
# Fails when the UI's source can write markup from a string: innerHTML, or the Leaflet APIs that write it (a tooltip, a popup, a
# control, attribution strings). The map shows every provider and indicator string through Angular text bindings
# (openspec/changes/improve-map-widget, design D2). Spec files are not read: they may mention these on purpose.
#
# It also fails on formatting that only works in English (openspec/changes/add-spanish-ui, design D3 and D10): Angular's
# locale-data pipes and functions, which throw for a locale that was not registered, `toLocaleString('en')`, and `.toFixed(`, which
# always writes a decimal point. Numbers, dates and times go through `shared/ui/formats.ts`.
# The unit-test builder has no file system, so this runs from the shell: `npm run check:safety` in tamiza-ui/.
set -eu
cd "$(dirname "$0")/../tamiza-ui"

found=0
check() {
    pattern=$1
    exclude=${2:-}
    hits=$(grep -rnE --include='*.ts' "$pattern" src/app | grep -vE '^[^:]*\.spec\.ts:' || true)
    if [ -n "$exclude" ] && [ -n "$hits" ]; then
        hits=$(printf '%s\n' "$hits" | grep -vE "$exclude" || true)
    fi
    if [ -n "$hits" ]; then
        printf 'forbidden (%s):\n%s\n' "$pattern" "$hits" >&2
        found=1
    fi
}

for pattern in 'innerHTML' 'bindTooltip\(' 'bindPopup\(' 'setContent\(' 'L\.control'; do
    check "$pattern"
done

# Angular's pipes and functions (`| date`, `| number`, `| percent`, `| currency`, `formatDate(`, `DatePipe`, ...).
check '\|[[:space:]]*(date|percent|currency)\b'
check '\|[[:space:]]*number[[:space:]]*(\}\}|:|\)[[:space:]]*(\}\}|,|:))'
check '\b(formatDate|formatNumber|formatPercent|formatCurrency)\('
check '\b(DatePipe|DecimalPipe|PercentPipe|CurrencyPipe)\b'
check 'toLocale(Date|Time)?String\([[:space:]]*.en'
# The two rounding uses below are not text: the chart scale's tick math and the key of a map view.
check '\.toFixed\(' 'indicators/(chart-scales|map-widget)\.ts:'

# Every text CodeMirror writes itself must be translatable: each phrase the installed @codemirror packages ask for needs an entry in
# code-editor-phrases.ts (add-spanish-ui, design D7), so a package update that asks for a new phrase fails here.
phrases=$(
    {
        grep -rhoE 'phrase\((view, )?"[^"]+"' node_modules/@codemirror/*/dist/index.js | sed -E 's/^[^"]*"([^"]+)"$/\1/'
        grep -rhoE 'phrase\([a-z.]+ \? "[^"]+" : "[^"]+"\)' node_modules/@codemirror/*/dist/index.js | grep -oE '"[^"]+"' | tr -d '"'
    } | sort -u
)
missing=$(
    printf '%s\n' "$phrases" | while IFS= read -r phrase; do
        [ -n "$phrase" ] || continue
        entries=src/app/core/i18n/code-editor-phrases.ts
        grep -qF "'$phrase': '" "$entries" || grep -qF "  $phrase: '" "$entries" || printf '%s\n' "$phrase"
    done
)
if [ -n "$missing" ]; then
    printf 'CodeMirror phrases without a translation key in code-editor-phrases.ts:\n%s\n' "$missing" >&2
    found=1
fi

if [ "$found" -eq 0 ]; then
    echo "No markup sinks and no English-only formatting in src/app."
fi
exit "$found"
