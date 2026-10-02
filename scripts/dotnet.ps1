$DotnetArguments = $args
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $localDotnet)) {
    throw '项目 SDK 尚未准备，请先执行 scripts\setup-sdk.ps1。'
}
$taskEnvironment = @{
    DOTNET_ROOT = (Split-Path -Parent $localDotnet)
    DOTNET_ROOT_X64 = (Split-Path -Parent $localDotnet)
    DOTNET_CLI_HOME = (Join-Path $projectRoot '.tools\cli')
    APPDATA = (Join-Path $projectRoot '.tools\appdata')
    LOCALAPPDATA = (Join-Path $projectRoot '.tools\localappdata')
    NUGET_PACKAGES = (Join-Path $projectRoot '.tools\packages')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
}
$previousEnvironment = @{}
try {
    foreach ($key in $taskEnvironment.Keys) {
        $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $taskEnvironment[$key], 'Process')
    }
    & $localDotnet @DotnetArguments
    $toolExitCode = $LASTEXITCODE
}
finally {
    foreach ($key in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process') }
}
exit $toolExitCode
