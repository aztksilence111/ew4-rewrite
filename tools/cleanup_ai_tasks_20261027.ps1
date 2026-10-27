$ErrorActionPreference = 'Stop'
$taskWorkspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$taskBuild = Join-Path $taskWorkspace 'build/ai-tasks-20261027'
$taskStarted = (Get-Item -LiteralPath $taskBuild).CreationTime
$taskTargets = [Collections.Generic.List[string]]::new()
foreach ($relative in @('build/ai-tasks-20261027','build/player-ai-20261026/verify-profile','build/army-capacity-20261026/check-profile','build/auto-jurisdictions-connected-20261025/backend.json')) {
    $candidate = Join-Path $taskWorkspace $relative
    if (Test-Path -LiteralPath $candidate) { $taskTargets.Add($candidate) }
}
foreach ($relative in @('build/hq-objectives-20261025/checks','build/map-colors-20261025')) {
    $directory = Join-Path $taskWorkspace $relative
    if (Test-Path -LiteralPath $directory) {
        Get-ChildItem -LiteralPath $directory -Directory | Where-Object { $_.CreationTime -ge $taskStarted } | ForEach-Object { $taskTargets.Add($_.FullName) }
    }
}
$reportNames = @('client-slot-test','commander-dialogues-20261021','common-skills-20261021','consumables-20261021','defeated-faction-test',
    'deployment-roster-20261022','equipment-fix-20261021','extensions-20261020','fort-construction-20261021','item-extensions-20261021',
    'jurisdiction-extension-20261020','legacy-skill-parameters-20261021','mobile-display-pan-20261022','objective_markers_20261019','save-check',
    'scenario-control-20261021','scenario-events-20261021','scenario-factions-20261021','scenario-recurrence-diplomacy-20261021','skill-descriptions-20261023',
    'ai_production_performance.json','client_host.json','core_validation.json','legacy_actions_ai.json','legacy_army_import.json','legacy_buildings.json',
    'legacy_combat.json','legacy_commerce.json','legacy_events.json','legacy_factions.json','legacy_host.json','legacy_map_import.json','legacy_startup.json',
    'legacy_technology.json','map_catalog.json','map_document_test.map.json','map_document_test.map.json.bak','roster_deployment.json','session_accounts.json')
foreach ($name in $reportNames) {
    $candidate = Join-Path $taskWorkspace "rewrite/reports/$name"
    if ((Test-Path -LiteralPath $candidate) -and (Get-Item -LiteralPath $candidate).CreationTime -ge $taskStarted) { $taskTargets.Add($candidate) }
}
$taskProcesses = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.Name -notmatch '^(pwsh|powershell)\.exe$' })
$taskReceipt = [Collections.Generic.List[object]]::new()
$taskStatus = @(git -C $taskWorkspace status --short)
foreach ($target in $taskTargets) {
    $absolute = [IO.Path]::GetFullPath($target)
    if (-not $absolute.StartsWith($taskWorkspace + '\', [StringComparison]::OrdinalIgnoreCase) -or (Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Invalid cleanup path: $absolute" }
    $relative = [IO.Path]::GetRelativePath($taskWorkspace, $absolute).Replace('\','/')
    if (@(git -C $taskWorkspace ls-files -- $relative).Count -gt 0) { throw "Tracked cleanup target: $relative" }
    $item = Get-Item -LiteralPath $absolute -Force
    $items = @($item)
    if ($item.PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force -ErrorAction Stop) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Linked cleanup target: $absolute" }
    foreach ($process in $taskProcesses) {
        if ($process.CommandLine.Replace('/','\').IndexOf($absolute,[StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Active process $($process.ProcessId) uses $absolute" }
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $taskReceipt.Add([pscustomobject]@{path=$relative;files=$files.Count;bytes=($files | Measure-Object Length -Sum).Sum})
}
$taskFailures = [Collections.Generic.List[object]]::new()
foreach ($target in $taskTargets) {
    try { Remove-Item -LiteralPath $target -Force -Recurse -ErrorAction Stop }
    catch { $taskFailures.Add([pscustomobject]@{path=$target;message=$_.Exception.Message}) }
}
$receiptDirectory = Join-Path $taskWorkspace 'build/maintenance-20261027'
New-Item -ItemType Directory -Path $receiptDirectory -Force | Out-Null
$summary = [ordered]@{
    date='2026-10-27';targets=$taskReceipt.Count;files=($taskReceipt | Measure-Object files -Sum).Sum;bytes=($taskReceipt | Measure-Object bytes -Sum).Sum
    remaining=@($taskTargets | Where-Object { Test-Path -LiteralPath $_ }).Count;errors=$taskFailures.Count
    retained=@('rewrite/local/ai-tasks-preview/host','rewrite/local/ai-tasks-preview/profile','rewrite/Ew4.Web/wwwroot/compat','formal releases','source checks and examples','unrelated terrain work')
    gitChangesBefore=$taskStatus.Count;removed=$taskReceipt;failures=$taskFailures
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'ai-tasks-cleanup.json') -Encoding utf8NoBOM
[pscustomobject]@{targets=$summary.targets;files=$summary.files;bytes=$summary.bytes;remaining=$summary.remaining;errors=$summary.errors} | ConvertTo-Json -Compress
if ($taskFailures.Count -gt 0) { throw 'Cleanup had failures; see receipt' }
