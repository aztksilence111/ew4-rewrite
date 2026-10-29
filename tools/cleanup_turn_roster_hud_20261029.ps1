param([int]$TestProcessId, [string]$ReceiptName = 'turn-roster-hud-cleanup.json')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$relative = 'build/turn-roster-hud-check-20261029'
$target = [IO.Path]::GetFullPath((Join-Path $root $relative))
if (-not $target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $target" }
$before = @(git -C $root status --short)
if ($TestProcessId) {
    $hostProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$TestProcessId"
    if ($hostProcess) {
        $command = $hostProcess.CommandLine.Replace('/', '\')
        if ($hostProcess.Name -ne 'dotnet.exe' -or -not $command.Contains($target + '\compile\bin\Ew4.Web\release\Ew4.Web.dll') -or -not $command.Contains($target + '\profile')) { throw 'Not this task test host' }
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
    preserved='Source checks, clean preview runtime, shared frontend, other services and player data, original inputs and releases'}
$directory = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Force $directory | Out-Null
if ([IO.Path]::GetFileName($ReceiptName) -ne $ReceiptName) { throw 'Receipt must be a file name' }
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $directory $ReceiptName) -Encoding utf8
$receipt | ConvertTo-Json -Compress
