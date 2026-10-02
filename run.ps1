$ErrorActionPreference = 'Stop'
$application = Join-Path $PSScriptRoot 'src\CXLauncher\bin\Release\net10.0-windows\CXLauncher.dll'
if (-not (Test-Path -LiteralPath $application)) { throw '请先运行 scripts\build.ps1。' }
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.tools\dotnet'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') $application
