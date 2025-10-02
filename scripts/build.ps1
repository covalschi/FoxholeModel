$ErrorActionPreference = 'Stop'
$env:CUE4PARSE_SKIP_NATIVE = '1'
dotnet build @args

