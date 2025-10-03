#!/usr/bin/env bash
set -euo pipefail

# Ensure the CUE4Parse submodule respects skip guard before build (idempotent)
"$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/ensure-cue4parse-target.sh" || true

export CUE4PARSE_SKIP_NATIVE=1
exec dotnet build "$@"
