param([string]$DataDirectory, [int]$Iterations = 11, [int]$IdleSeconds = 60)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $DataDirectory) { throw '请指定测试数据目录，避免操作实际游戏库。' }
$dataRoot = [IO.Path]::GetFullPath($DataDirectory)
if (-not $dataRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $projectRoot '.artifacts')) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '性能测试数据必须位于项目 .artifacts 内。' }
$application = Join-Path $projectRoot '.artifacts\publish\self-contained\CXLauncher.exe'
$previousData = $env:CXLAUNCHER_DATA_DIR
$process = $null
$times = [System.Collections.Generic.List[double]]::new()
try {
    $env:CXLAUNCHER_DATA_DIR = $dataRoot
    for ($iteration = 0; $iteration -lt $Iterations; $iteration++) {
        $logFile = Join-Path $dataRoot 'logs\launcher.log'
        $previousLines = if (Test-Path -LiteralPath $logFile) { @(Get-Content -LiteralPath $logFile).Count } else { 0 }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $application -WorkingDirectory (Split-Path -Parent $application) -WindowStyle Hidden -PassThru
        $ready = $false
        $readyLine = $null
        while ($watch.Elapsed.TotalSeconds -lt 15) {
            if ($process.HasExited) { throw "测试启动器退出：$($process.ExitCode)" }
            if (Test-Path -LiteralPath $logFile) {
                $readyLines = @(Get-Content -LiteralPath $logFile | Select-Object -Skip $previousLines | Where-Object { $_ -match ' Ready:' })
                if ($readyLines.Count -gt 0) { $readyLine = $readyLines[-1]; $ready = $true; break }
            }
            Start-Sleep -Milliseconds 20
        }
        if (-not $ready) { throw '未收到可操作界面标记。' }
        $watch.Stop()
        $readyTimestamp = [DateTimeOffset]::Parse($readyLine.Split(' ')[0])
        $elapsed = ($readyTimestamp - [DateTimeOffset]$process.StartTime).TotalMilliseconds
        $times.Add($elapsed)
        Write-Output "启动 $($iteration + 1): $([math]::Round($elapsed)) ms（外部等待 $([math]::Round($watch.Elapsed.TotalMilliseconds)) ms）"
        if ($iteration -eq $Iterations - 1) { break }
        $process.Refresh()
        if (-not $process.CloseMainWindow()) { throw '测试窗口未能正常关闭。' }
        if (-not $process.WaitForExit(10000)) { throw '等待测试窗口关闭超时。' }
    }
    $duplicate = Start-Process -FilePath $application -WorkingDirectory (Split-Path -Parent $application) -WindowStyle Hidden -PassThru
    if (-not $duplicate.WaitForExit(10000) -or $duplicate.ExitCode -ne 0) { throw '单实例重复启动未正常退出。' }
    Write-Output '单实例验证通过：第二个进程已正常退出，首个窗口仍在运行。'
    Start-Sleep -Seconds 30
    $process.Refresh()
    $memory = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$($process.Id)"
    if ($null -eq $memory) { throw '未能获得进程私有工作集计数器。' }
    $cpuStart = $process.TotalProcessorTime.TotalMilliseconds
    $cpuWatch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $IdleSeconds
    $process.Refresh(); $cpuWatch.Stop()
    $cpuPercent = ($process.TotalProcessorTime.TotalMilliseconds - $cpuStart) / $cpuWatch.Elapsed.TotalMilliseconds / [Environment]::ProcessorCount * 100
    $hot = @($times | Select-Object -Skip 1 | Sort-Object)
    $middle = [int][math]::Floor($hot.Count / 2)
    $median = if ($hot.Count % 2 -eq 0) { ($hot[$middle - 1] + $hot[$middle]) / 2 } else { $hot[$middle] }
    $report = [pscustomobject]@{
        Timestamp = [DateTimeOffset]::Now.ToString('O')
        OS = [Environment]::OSVersion.Version.ToString()
        Cpu = (Get-CimInstance Win32_Processor).Name.Trim()
        LogicalProcessors = [Environment]::ProcessorCount
        MemoryGiB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
        GameCount = (Get-Content -LiteralPath (Join-Path $dataRoot 'library.json') -Raw | ConvertFrom-Json).games.Count
        StartupMilliseconds = @($times | ForEach-Object { [math]::Round($_, 1) })
        FirstRunMilliseconds = [math]::Round($times[0], 1)
        HotMedianMilliseconds = [math]::Round($median, 1)
        HotMaxMilliseconds = [math]::Round(($hot | Measure-Object -Maximum).Maximum, 1)
        PrivateWorkingSetMiB = [math]::Round($memory.WorkingSetPrivate / 1MB, 1)
        PrivateBytesMiB = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        WorkingSetMiB = [math]::Round($process.WorkingSet64 / 1MB, 1)
        IdleCpuNormalizedPercent = [math]::Round($cpuPercent, 4)
        IdleSampleSeconds = $IdleSeconds
        SingleInstancePassed = $true
        StartupDefinition = 'OS process StartTime to UTC Ready timestamp after metadata loaded and application-idle callback. Excludes PowerShell launch-return/polling overhead. First run is not a reboot cold start.'
    }
    $reportName = if ($IdleSeconds -ge 60) { 'performance.json' } else { 'startup-audit.json' }
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dataRoot $reportName) -Encoding utf8
    $report | ConvertTo-Json -Depth 4
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.Refresh()
        if ($process.CloseMainWindow()) { if (-not $process.WaitForExit(10000)) { Stop-Process -Id $process.Id } }
        else { Stop-Process -Id $process.Id }
    }
    $env:CXLAUNCHER_DATA_DIR = $previousData
}
