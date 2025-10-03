#!/usr/bin/env bash
set -euo pipefail

# Ensures CUE4Parse's CUE4Parse.csproj has a skip guard on the Build-Natives target.
# If the target exists but lacks a Condition, we inject one that honors CUE4PARSE_SKIP_NATIVE.

CS_PROJ_DEFAULT="extern/CUE4Parse/CUE4Parse/CUE4Parse.csproj"
CS_PROJ="${1:-$CS_PROJ_DEFAULT}"

if [[ ! -f "$CS_PROJ" ]]; then
  echo "[ensure-cue4parse] csproj not found at: $CS_PROJ (skip)" >&2
  exit 0
fi

if grep -qE '<Target[^>]*Name="Build-Natives"[^>]*Condition=' "$CS_PROJ"; then
  echo "[ensure-cue4parse] Build-Natives already conditioned (ok)"
  exit 0
fi

echo "[ensure-cue4parse] Injecting CUE4PARSE_SKIP_NATIVE guard into Build-Natives target"

# Inject a Condition attribute on the Build-Natives opening tag. Keep other attributes intact.
perl -0777 -pe '
  my $cond = q{ Condition=\'$(CUE4PARSE_SKIP_NATIVE)\' != \'true\' and \'$(CUE4PARSE_SKIP_NATIVE)\' != \'1\' and \'$(CUE4PARSE_SKIP_NATIVE)\' != \'True\'};
  s{(<Target\s+Name="Build-Natives"[^>]*?)(>)}{$1.$cond.$2}s;
' -i "$CS_PROJ"

echo "[ensure-cue4parse] Done"

