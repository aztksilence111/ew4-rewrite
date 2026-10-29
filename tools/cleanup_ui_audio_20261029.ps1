param([int]$TestProcessId)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$relative = 'build/ui-audio-check-20261029'
$target = [IO.Path]::GetFullPath((Join-Path $root $relative))
if (-not $target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $target" }
$before = @(git -C $root status --short)
if ($TestProcessId) {
    $hostProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$TestProcessId"
    if ($hostProcess) {
        if ($hostProcess.Name -ne 'dotnet.exe' -or -not $hostProcess.CommandLine.Contains('ui-audio-preview/runtime/Ew4.Web.dll') -or -not $hostProcess.CommandLine.Contains('ui-audio-check-20261029/profile')) { throw 'Not this task test host' }
        Stop-Process -Id $TestProcessId
        Wait-Process -Id $TestProcessId -ErrorAction SilentlyContinue
    }
}
$files = @()
if (Test-Path -LiteralPath $target) {
    if ((Resolve-Path -LiteralPath $target).Path -ne $target) { throw "Resolved path differs: $target" }
    $items = @((Get-Item -LiteralPath $target -Force)) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $target" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $target" }
    $consumers = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($target) })
    if ($consumers.Count) { throw "Active consumer: $target" }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    Remove-Item -LiteralPath $target -Recurse -Force
}
$receipt = [ordered]@{date='2026-10-29';target=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum);
    remaining=[int](Test-Path -LiteralPath $target);errors=0;gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $root status --short)));
    preserved='Source checks, minimal clean preview runtime, shared frontend, other services and player data, original input and releases'}
$directory = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory 'ui-audio-cleanup.json') -Encoding utf8
$receipt | ConvertTo-Json -Compress
