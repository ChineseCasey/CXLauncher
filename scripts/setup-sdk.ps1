$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdkVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$sdkRoot = Join-Path $projectRoot '.tools\dotnet'
$sdkExecutable = Join-Path $sdkRoot 'dotnet.exe'
if ((Test-Path -LiteralPath $sdkExecutable) -and ((& $sdkExecutable --list-sdks) -match [regex]::Escape($sdkVersion))) {
    Write-Output "SDK $sdkVersion 已准备。"
    exit 0
}
$metadata = Invoke-RestMethod -Uri 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
$release = $metadata.releases | Where-Object { $_.sdk.version -eq $sdkVersion } | Select-Object -First 1
$asset = $release.sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') } | Select-Object -First 1
if ($null -eq $asset) { throw "微软发布清单中没有找到 SDK $sdkVersion 的 win-x64 ZIP。" }
$toolsDirectory = Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
$archive = Join-Path $toolsDirectory 'dotnet-sdk.zip'
Write-Output "下载微软官方 SDK $sdkVersion…"
Invoke-WebRequest -Uri $asset.url -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $asset.hash) { throw 'SDK SHA-512 校验失败，未解压。' }
Expand-Archive -LiteralPath $archive -DestinationPath $sdkRoot -Force
& $sdkExecutable --info
