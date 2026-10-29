param([int]$TestProcessId)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets = @('build/shop-commander-check-20261029',
    'rewrite/reports/client-slot-test/run-776eb97769834ebc8fdd960b9c8c6ffa',
    'rewrite/reports/mobile-display-pan-20261022/naval-api-03bb6412529c4c55a5acb5272b363031',
    'rewrite/reports/client_host.json')
$before = @(git -C $root status --short)
if ($TestProcessId) {
    $testHost = Get-CimInstance Win32_Process -Filter "ProcessId=$TestProcessId"
    if ($testHost) {
        if ($testHost.Name -ne 'dotnet.exe' -or -not $testHost.CommandLine.Contains('shop-commander-check-20261029\runtime\Ew4.Web.dll')) {
            throw 'The specified process is not this task test host'
        }
        Stop-Process -Id $TestProcessId
        Wait-Process -Id $TestProcessId -ErrorAction SilentlyContinue
    }
}
$processes = @(Get-CimInstance Win32_Process)
$rows = @()
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolved path differs: $absolute" }
    $item = Get-Item -LiteralPath $absolute -Force
    $items = @($item)
    if ($item.PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($absolute) }) {
        throw "Active consumer: $relative"
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
$receipt = [ordered]@{date='2026-10-29';targets=$rows;files=($rows | Measure-Object files -Sum).Sum;
    bytes=($rows | Measure-Object bytes -Sum).Sum;remaining=@($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) }).Count;errors=0;
    gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $root status --short)));
    preserved='Source checks, clean preview runtime, active shared frontend, other services and player data, releases and original input'}
$directory = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'shop-commander-cleanup.json') -Encoding utf8
[pscustomobject]@{targets=$rows.Count;files=$receipt.files;bytes=$receipt.bytes;remaining=$receipt.remaining;errors=0;gitStatusUnchanged=$receipt.gitStatusUnchanged} | ConvertTo-Json -Compress
