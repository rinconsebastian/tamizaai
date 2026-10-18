#!/bin/sh
# Fails when the UI's source can write markup from a string: innerHTML, or the Leaflet APIs that write it (a tooltip, a popup, a
# control, attribution strings). The map shows every provider and indicator string through Angular text bindings
# (openspec/changes/improve-map-widget, design D2). Spec files are not read: they may mention these on purpose.
# The unit-test builder has no file system, so this runs from the shell: `npm run check:safety` in tamiza-ui/.
set -eu
cd "$(dirname "$0")/../tamiza-ui"

found=0
for pattern in 'innerHTML' 'bindTooltip\(' 'bindPopup\(' 'setContent\(' 'L\.control'; do
    hits=$(grep -rnE --include='*.ts' "$pattern" src/app | grep -vE '^[^:]*\.spec\.ts:' || true)
    if [ -n "$hits" ]; then
        printf 'forbidden (%s):\n%s\n' "$pattern" "$hits" >&2
        found=1
    fi
done

if [ "$found" -eq 0 ]; then
    echo "No markup sinks in src/app."
fi
exit "$found"
