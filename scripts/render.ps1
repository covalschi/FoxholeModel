$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Ensure native step is skipped for CUE4Parse
$env:CUE4PARSE_SKIP_NATIVE = '1'

# Choose configuration via env var (default Release)
$config = if ($env:DOTNET_CONFIGURATION) { $env:DOTNET_CONFIGURATION } else { 'Release' }

# Run from repo root (parent of scripts folder)
Set-Location -LiteralPath (Join-Path $PSScriptRoot '..')

# Forward all app args after --
dotnet run -c $config -- @args

