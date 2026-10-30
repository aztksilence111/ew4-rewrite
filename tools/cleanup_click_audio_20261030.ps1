param([int]$TestProcessId)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$before = @(git -C $root status --short)
if ($TestProcessId) {
    $hostProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$TestProcessId"
    if ($hostProcess) {
        if ($hostProcess.Name -ne 'dotnet.exe' -or -not $hostProcess.CommandLine.Contains('click-audio-check-20261030/profile') -or -not $hostProcess.CommandLine.Contains('56153')) { throw 'Not this task test host' }
        Stop-Process -Id $TestProcessId
        Wait-Process -Id $TestProcessId -ErrorAction SilentlyContinue
    }
}
$count = 0
$bytes = [long]0
foreach ($relative in @('build/click-audio-check-20261030', 'build/click-audio-preview-20261030')) {
    $target = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $target" }
    if (-not (Test-Path -LiteralPath $target)) { continue }
    if ((Resolve-Path -LiteralPath $target).Path -ne $target) { throw "Resolved path differs: $target" }
    $items = @((Get-Item -LiteralPath $target -Force)) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $target" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $target" }
    $consumers = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($target) })
    if ($consumers.Count) { throw "Active consumer: $target" }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $count += $files.Count
    $bytes += [long](($files | Measure-Object Length -Sum).Sum)
    Remove-Item -LiteralPath $target -Recurse -Force
}
$receipt = [ordered]@{date='2026-10-30';files=$count;bytes=$bytes;remaining=@('build/click-audio-check-20261030','build/click-audio-preview-20261030').Where({Test-Path -LiteralPath (Join-Path $root $_)}).Count;errors=0;
    gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $root status --short)));preserved='Source checks, shared compatible frontend, clean preview, releases, original inputs and player profiles'}
$directory = Join-Path $root 'build/maintenance-20261030'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory 'click-audio-cleanup.json') -Encoding utf8
$receipt | ConvertTo-Json -Compress
