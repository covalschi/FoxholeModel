#!/usr/bin/env bash
set -euo pipefail

# Runs the Windows PowerShell render from WSL.
# Usage examples:
#   scripts/render-windows.sh -- render --scene output/scene_bpatgunait2.json
#   DOTNET_CONFIGURATION=Debug scripts/render-windows.sh -- --pak-dir "C:\\Path\\To\\Paks" render --scene output/scene.json --output output/renders

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$here/.." && pwd)"

if ! command -v wslpath >/dev/null 2>&1; then
  echo "wslpath not found; this script must run in WSL." >&2
  exit 1
fi

# Prefer PowerShell 7 if available, else Windows PowerShell
ps_exe="pwsh.exe"
if ! command -v "$ps_exe" >/dev/null 2>&1; then
  ps_exe="powershell.exe"
fi
if ! command -v "$ps_exe" >/dev/null 2>&1; then
  echo "Neither pwsh.exe nor powershell.exe found on PATH. Install PowerShell on Windows." >&2
  exit 1
fi

win_repo="$(wslpath -w "$repo_root")"
win_script="$win_repo\\scripts\\render.ps1"

# Forward all args to the PowerShell render script.
"$ps_exe" -NoProfile -ExecutionPolicy Bypass -File "$win_script" "$@"

