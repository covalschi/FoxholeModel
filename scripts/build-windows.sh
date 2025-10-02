#!/usr/bin/env bash
set -euo pipefail

# Runs the Windows PowerShell build from WSL, honoring repo scripts.
# Usage examples:
#   scripts/build-windows.sh -c Release
#   scripts/build-windows.sh -c Debug -p:CUE4_DIR=C:\third_party\CUE4Parse

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
win_script="$win_repo\\scripts\\build.ps1"

# Pass all args through to the PowerShell build script
"$ps_exe" -NoProfile -ExecutionPolicy Bypass -File "$win_script" "$@"

