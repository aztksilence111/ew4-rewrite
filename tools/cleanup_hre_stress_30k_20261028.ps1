$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets = @(
    'build/hre-stress-30k-20261028',
    'tools/HreStressAudit/bin', 'tools/HreStressAudit/obj',
    'rewrite/reports/client-slot-test/run-4dc28af4f3f14f2abb06cc2e272d9fd5',
    'rewrite/reports/mobile-display-pan-20261022/naval-api-430d6200438c409ea9d38d39b05a7600',
    'rewrite/reports/client_host.json',
    'rewrite/reports/objective_markers_20261019/campaign-checks.json',
    'rewrite/reports/objective_markers_20261019/test-profile/saves/mode-1/slot-6.json'
)
# The fort suite reused a report directory containing earlier work; remove only this run's files.
$fort = Join-Path $root 'rewrite/reports/fort-construction-20261021'
if (Test-Path -LiteralPath $fort) {
    $targets += @(Get-ChildItem -LiteralPath $fort -Recurse -File | Where-Object {
        $_.LastWriteTime -ge [datetime]'2026-10-28 16:50:00'
    } | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) })
}
$processes = @(Get-CimInstance Win32_Process)
$server = $processes | Where-Object { $_.ProcessId -eq 20036 -and $_.CommandLine -match 'hre-stress-30k-20261028/host/Ew4.Web.dll' -and $_.CommandLine -match ':5601' }
if ($server) { Stop-Process -Id $server.ProcessId -Force }
$gitState = @(git -C $root status --short)
$rows = @()
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolved path differs: $absolute" }
    $items = @((Get-Item -LiteralPath $absolute -Force))
    if ($items[0].PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $relative" }
    $consumers = $processes | Where-Object {
        $_.ProcessId -ne $PID -and $_.ProcessId -ne 20036 -and $_.CommandLine -and
        $_.CommandLine.Replace('/', '\').Contains($absolute)
    }
    if ($consumers) { throw "Active consumer of $relative" }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{ path = $relative; files = $files.Count; bytes = [long](($files | Measure-Object Length -Sum).Sum) }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
$remaining = @($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) })
$receipt = [ordered]@{
    date = '2026-10-28'; targets = $rows; files = ($rows | Measure-Object files -Sum).Sum
    bytes = ($rows | Measure-Object bytes -Sum).Sum; remaining = $remaining.Count; errors = 0
    stoppedServer = @($server.ProcessId); preserved = 'Original map, player services/accounts/saves/mods, earlier fort reports, shared builds and concurrent edits'
    gitStatusUnchanged = [bool](-not (Compare-Object $gitState @(git -C $root status --short)))
}
$receiptDirectory = Join-Path $root 'build/maintenance-20261028'
New-Item -ItemType Directory -Force $receiptDirectory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'hre-stress-30k-cleanup.json') -Encoding utf8
[pscustomobject]@{targets=$rows.Count;files=$receipt.files;bytes=$receipt.bytes;remaining=$remaining.Count;errors=0;gitStatusUnchanged=$receipt.gitStatusUnchanged} | ConvertTo-Json -Compress
