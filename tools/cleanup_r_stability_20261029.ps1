$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets = @('build/r-stability-audit-20261029',
    'rewrite/reports/defeated-faction-test/46809e21b21c477c81ec259bc5523dbb',
    'rewrite/reports/defeated-faction-test/794d1786464a410bbe2e369426cbd5c2')
$before = @(git -C $root status --short)
$processes = @(Get-CimInstance Win32_Process)
$rows = @()
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolved path differs: $absolute" }
    $items = @((Get-Item -LiteralPath $absolute -Force)) + @(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($absolute) }) {
        throw "Active consumer: $relative"
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
$remaining = @($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) })
$receipt = [ordered]@{date='2026-10-29';targets=$rows;files=($rows | Measure-Object files -Sum).Sum;
    bytes=($rows | Measure-Object bytes -Sum).Sum;remaining=$remaining.Count;errors=0;
    gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $root status --short)));
    preserved='Source checks, clean preview runtime, other previews and profiles, releases, signing material, original input and concurrent changes'}
$directory = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'r-stability-cleanup.json') -Encoding utf8
[pscustomobject]@{targets=$rows.Count;files=$receipt.files;bytes=$receipt.bytes;remaining=$remaining.Count;errors=0;gitStatusUnchanged=$receipt.gitStatusUnchanged} | ConvertTo-Json -Compress
