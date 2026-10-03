#!/bin/sh
# Fails when a variable referenced in a Compose file is not documented in .env.example.
set -eu
cd "$(dirname "$0")/.."

missing=0
# ${VAR...} references, skipping $${VAR} (escaped: resolved inside the container, not by Compose).
for var in $(grep -ohE '(^|[^$])\$\{[A-Za-z_][A-Za-z0-9_]*' compose*.yaml | sed 's/.*{//' | sort -u); do
    if ! grep -qE "^#? ?${var}=" .env.example; then
        echo "missing from .env.example: ${var}" >&2
        missing=1
    fi
done

if [ "$missing" -eq 0 ]; then
    echo "All Compose variables are documented in .env.example."
fi
exit "$missing"
