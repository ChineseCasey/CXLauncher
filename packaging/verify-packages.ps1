$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$projectDefinition = Get-Content -LiteralPath (Join-Path $projectRoot 'src\CXLauncher\CXLauncher.csproj') -Raw
$releaseVersion = [string]$projectDefinition.Project.PropertyGroup.Version
$testRoot = Join-Path $projectRoot ('.artifacts\package-smoke\' + $releaseVersion + '\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$results = @()
$savedEnvironment = @{}
foreach ($name in @('CXLAUNCHER_DATA_DIR','DOTNET_ROOT','DOTNET_ROOT_X64')) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    foreach ($distribution in @('self-contained','framework-dependent')) {
        $archive = Join-Path $projectRoot "dist\CXLauncher-$releaseVersion-win-x64-$distribution.zip"
        $checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        $expected = (Get-Content -LiteralPath "$archive.sha256" -Raw).Split(' ')[0]
        if ($checksum -ne $expected) { throw 'Archive checksum mismatch.' }
        $extracted = Join-Path $testRoot "$distribution\app"
        Expand-Archive -LiteralPath $archive -DestinationPath $extracted
        $isolatedData = Join-Path $testRoot "$distribution\data"
        [Environment]::SetEnvironmentVariable('CXLAUNCHER_DATA_DIR', $isolatedData, 'Process')
        $executable = Join-Path $extracted 'CXLauncher.exe'
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
        if ($version -ne $releaseVersion) { throw "Unexpected executable version: $version" }
        $primary = $null; $secondary = $null
        try {
            if ($distribution -eq 'self-contained') {
                [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $null, 'Process')
                [Environment]::SetEnvironmentVariable('DOTNET_ROOT_X64', $null, 'Process')
                $launchOptions = @{FilePath=$executable;WorkingDirectory=$extracted;WindowStyle='Hidden';PassThru=$true}
            } else {
                [Environment]::SetEnvironmentVariable('DOTNET_ROOT', "$projectRoot\.tools\dotnet", 'Process')
                [Environment]::SetEnvironmentVariable('DOTNET_ROOT_X64', "$projectRoot\.tools\dotnet", 'Process')
                $launchOptions = @{FilePath="$projectRoot\.tools\dotnet\dotnet.exe";ArgumentList=@(Join-Path $extracted 'CXLauncher.dll');WorkingDirectory=$extracted;WindowStyle='Hidden';PassThru=$true}
            }
            $primary = Start-Process @launchOptions
            $logPath = Join-Path $isolatedData 'logs\launcher.log'
            $ready = $false
            for ($attempt = 0; $attempt -lt 100; $attempt++) {
                if ($primary.HasExited) { throw 'Primary exited before ready.' }
                if ((Test-Path -LiteralPath $logPath) -and (Get-Content -LiteralPath $logPath -Raw).Contains('Ready:')) { $ready=$true; break }
                Start-Sleep -Milliseconds 100
            }
            if (!$ready) { throw 'Startup timed out.' }
            $secondary = Start-Process @launchOptions
            if (!$secondary.WaitForExit(5000) -or $secondary.ExitCode -ne 0) { throw 'Second instance did not exit normally.' }
            $primary.Refresh()
            if (!$primary.CloseMainWindow() -or !$primary.WaitForExit(8000) -or $primary.ExitCode -ne 0) { throw 'Normal window close failed.' }
            if (!(Test-Path -LiteralPath (Join-Path $isolatedData 'settings.json'))) { throw 'Close did not save settings.' }
            $results += [pscustomobject]@{Mode=$distribution;Version=$version;Archive=$archive;Bytes=(Get-Item -LiteralPath $archive).Length;SHA256=$checksum;Ready=$ready;SecondInstanceExit=0;PrimaryExit=0;SettingsSaved=$true;ExtractedDirectory=$extracted}
        } finally {
            foreach ($launched in @($primary,$secondary)) { if ($null -ne $launched -and !$launched.HasExited) { Stop-Process -Id $launched.Id } }
        }
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $testRoot 'verification.json')
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot 'dist\verification.json')
    $results | Format-Table Mode,Version,Ready,PrimaryExit,SecondInstanceExit,SettingsSaved
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
