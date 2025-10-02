#!/usr/bin/env bash
set -euo pipefail
export CUE4PARSE_SKIP_NATIVE=1
exec dotnet build "$@"

