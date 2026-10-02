param([ValidateSet('framework-dependent', 'self-contained', 'all')][string]$Mode = 'all')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & "$projectRoot\scripts\compile-shader.ps1"
    $projectPath = Join-Path $projectRoot 'src\CXLauncher\CXLauncher.csproj'
    [xml]$projectDefinition = Get-Content -LiteralPath $projectPath -Raw
    $releaseVersion = [string]$projectDefinition.Project.PropertyGroup.Version
    if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw '项目版本必须为三段数字。' }
    $deliveryDirectory = Join-Path $projectRoot 'dist'
    New-Item -ItemType Directory -Path $deliveryDirectory -Force | Out-Null
    $modes = if ($Mode -eq 'all') { @('framework-dependent', 'self-contained') } else { @($Mode) }
    foreach ($distribution in $modes) {
        $destination = Join-Path $projectRoot ".artifacts\publish\$distribution"
        $selfContained = if ($distribution -eq 'self-contained') { 'true' } else { 'false' }
        & "$projectRoot\scripts\dotnet.ps1" restore src\CXLauncher\CXLauncher.csproj -r win-x64 "--property:SelfContained=$selfContained" --configfile "$projectRoot\NuGet.Config" --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "$distribution 依赖还原失败。" }
        & "$projectRoot\scripts\dotnet.ps1" publish src\CXLauncher\CXLauncher.csproj -c Release -r win-x64 --self-contained $selfContained '--property:PublishTrimmed=false' '--property:DebugType=None' '--property:DebugSymbols=false' --output $destination --no-restore
        if ($LASTEXITCODE -ne 0) { throw "$distribution 发布失败。" }
        if ($distribution -eq 'framework-dependent') {
            Copy-Item -LiteralPath "$PSScriptRoot\start-framework-dependent.cmd" -Destination (Join-Path $destination '启动 CX Launcher.cmd')
        }
        Copy-Item -LiteralPath "$PSScriptRoot\portable-readme.txt" -Destination (Join-Path $destination '使用说明.txt')
        $archive = Join-Path $projectRoot ".artifacts\CXLauncher-$releaseVersion-win-x64-$distribution.zip"
        Compress-Archive -Path "$destination\*" -DestinationPath $archive -Force
        $deliveryArchive = Join-Path $deliveryDirectory (Split-Path -Leaf $archive)
        Copy-Item -LiteralPath $archive -Destination $deliveryArchive -Force
        $checksum = (Get-FileHash -LiteralPath $deliveryArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        "$checksum  $(Split-Path -Leaf $deliveryArchive)" | Set-Content -LiteralPath "$deliveryArchive.sha256" -Encoding ascii
        $uncompressed = (Get-ChildItem -LiteralPath $destination -File -Recurse | Measure-Object -Property Length -Sum).Sum
        Write-Output "$distribution ZIP=$([math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 2)) MiB; 解压=$([math]::Round($uncompressed / 1MB, 2)) MiB"
        Write-Output "交付包：$deliveryArchive"
    }
}
finally { Pop-Location }
