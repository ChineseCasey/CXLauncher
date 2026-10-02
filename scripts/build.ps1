param([switch]$UiSmoke)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & "$PSScriptRoot\compile-shader.ps1"
    & "$PSScriptRoot\dotnet.ps1" restore CXLauncher.slnx --configfile "$projectRoot\NuGet.Config" --locked-mode
    if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }
    & "$PSScriptRoot\dotnet.ps1" build CXLauncher.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
    $testArguments = @()
    if ($UiSmoke) { $testArguments += '--ui-smoke' }
    & "$PSScriptRoot\dotnet.ps1" run --project tests\CXLauncher.Tests -c Release --no-build -- @testArguments
    if ($LASTEXITCODE -ne 0) { throw '验证失败。' }
}
finally { Pop-Location }
