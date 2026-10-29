param([int]$TestProcessId)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets = @('build/hq-presentation-20261029',
    'rewrite/reports/headquarters-20261021/run-776641c5fa24495f99e88aa6f0a4b887',
    'rewrite/reports/client-slot-test/run-0132aac0586e4c01b2e9d2fd2034def2',
    'rewrite/reports/mobile-display-pan-20261022/naval-api-04ddcac6c0ec48938956c2c724612315')
$before = @(git -C $root status --short)
if ($TestProcessId) {
    $testHost = Get-CimInstance Win32_Process -Filter "ProcessId=$TestProcessId"
    if ($testHost) {
        if ($testHost.Name -ne 'dotnet.exe' -or -not $testHost.CommandLine.Replace('/', '\').Contains('build\hq-presentation-20261029\compile\bin\Ew4.Web')) { throw 'Not this task test host' }
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
    if (@(git -C $root ls-files --others --exclude-standard -- $relative).Count) { throw "Untracked author input: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($absolute) }) { throw "Active consumer: $relative" }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
$receipt = [ordered]@{date='2026-10-29';targets=$rows;files=($rows | Measure-Object files -Sum).Sum;
    bytes=($rows | Measure-Object bytes -Sum).Sum;remaining=@($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) }).Count;errors=0;
    gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $root status --short)));
    preserved='Author example source, clean preview runtime, shared frontend and shared reports, other services and player data, releases and original input'}
$directory = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'headquarters-presentation-cleanup.json') -Encoding utf8
[pscustomobject]@{targets=$rows.Count;files=$receipt.files;bytes=$receipt.bytes;remaining=$receipt.remaining;errors=0;gitStatusUnchanged=$receipt.gitStatusUnchanged} | ConvertTo-Json -Compress
