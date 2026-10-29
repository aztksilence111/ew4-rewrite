$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $root 'build/hre30-15000-20261029'
if (-not (Test-Path -LiteralPath (Join-Path $scratch 'full-round/results.json'))) { throw 'Full-round result missing' }
$result = Get-Content -LiteralPath (Join-Path $scratch 'full-round/results.json') -Raw | ConvertFrom-Json
if (-not $result.complete) { throw 'Full AI round did not complete' }
$cutoff = [datetime]'2026-10-29 15:30:00'
$finished = [datetime]'2026-10-29 15:32:09'
$targets = @(
    'build/hre30-15000-20261029',
    'rewrite/reports/client-slot-test/run-e21ed55eec4b45feb36adc17b3db0445',
    'rewrite/reports/mobile-display-pan-20261022/naval-api-c85e6615ccc24ce8a546c7e33250fd24',
    'rewrite/reports/defeated-faction-test/bda0f7d87cdf473ab431b54cd5df2f40',
    'rewrite/reports/ai_production_performance.json'
)
$fort = Join-Path $root 'rewrite/reports/fort-construction-20261021'
if (Test-Path -LiteralPath $fort) {
    $targets += @(Get-ChildItem -LiteralPath $fort -File | Where-Object { $_.LastWriteTime -ge $cutoff -and $_.LastWriteTime -le $finished } |
        ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) })
    $clients = Join-Path $fort 'clients'
    $targets += @(Get-ChildItem -LiteralPath $clients -Directory | Where-Object { $_.CreationTime -ge $cutoff -and $_.CreationTime -le $finished } |
        ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) })
}
$processes = @(Get-CimInstance Win32_Process)
$gitBefore = @(git -C $root status --short)
$rows = @()
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Unexpected real path: $absolute" }
    $items = @((Get-Item -LiteralPath $absolute -Force))
    if ($items[0].PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked file: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($absolute) }) {
        throw "Active consumer: $absolute"
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{ path = $relative; files = $files.Count; bytes = [long](($files | Measure-Object Length -Sum).Sum) }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
$remaining = @($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) })
$gitAfter = @(git -C $root status --short)
$gitUnchanged = ($gitBefore -join "`n") -eq ($gitAfter -join "`n")
$receipt = [ordered]@{ date = '2026-10-29'; targets = $rows; files = ($rows | Measure-Object files -Sum).Sum;
    bytes = ($rows | Measure-Object bytes -Sum).Sum; remaining = $remaining.Count; errors = 0; gitStatusUnchanged = $gitUnchanged }
$destination = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'hre30-15000-cleanup.json') -Encoding utf8
[pscustomobject]@{ files = $receipt.files; bytes = $receipt.bytes; remaining = $receipt.remaining; errors = 0 } | ConvertTo-Json -Compress
if ($remaining.Count) { throw 'Cleanup incomplete' }
