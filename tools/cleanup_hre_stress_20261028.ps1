$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets = @(
    'build/hre-stress-20261028',
    'tools/HreStressAudit/bin', 'tools/HreStressAudit/obj',
    'rewrite/reports/unit-display-20261021',
    'rewrite/reports/client-slot-test/run-83529f11e6284184ad3a689896d57239',
    'rewrite/reports/mobile-display-pan-20261022/naval-api-e411aac129f447bfb60458487ada20e9',
    'rewrite/reports/fort-construction-20261021',
    'rewrite/reports/extensions-20261020',
    'rewrite/reports/common-skills-20261021',
    'rewrite/reports/commander-dialogues-20261021',
    'rewrite/reports/deployment-roster-20261022',
    'rewrite/reports/defeated-faction-test',
    'rewrite/reports/client_host.json', 'rewrite/reports/ai_production_performance.json',
    'rewrite/reports/objective_markers_20261019/campaign-checks.json'
)
$processes = @(Get-CimInstance Win32_Process)
$server = $processes | Where-Object { $_.ProcessId -eq 32148 -and $_.CommandLine -match 'hre-stress-20261028/web-profile' -and $_.CommandLine -match ':5597' }
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
        $_.ProcessId -ne $PID -and $_.ProcessId -ne 32148 -and $_.CommandLine -and
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
    stoppedServer = @($server.ProcessId); preserved = 'Original map source, player services/accounts/saves/mods, shared builds and concurrent source edits'
    gitStatusUnchanged = [bool](-not (Compare-Object $gitState @(git -C $root status --short)))
}
$receiptDirectory = Join-Path $root 'build/maintenance-20261028'
New-Item -ItemType Directory -Force $receiptDirectory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'hre-stress-cleanup.json') -Encoding utf8
[pscustomobject]@{targets=$rows.Count;files=$receipt.files;bytes=$receipt.bytes;remaining=$remaining.Count;errors=0;gitStatusUnchanged=$receipt.gitStatusUnchanged} | ConvertTo-Json -Compress
