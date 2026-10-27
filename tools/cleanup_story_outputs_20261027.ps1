param([switch]$Execute)
$ErrorActionPreference='Stop'
$storyRoot=(Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$storyTargets=@('build/story-browser-20261027','build/story-flow-test-20261027')
foreach($storyProject in @('Android','Client','Core','Legacy','Mods','ModTool','Persistence','Verify','Web')) {
    foreach($storyOutput in @('bin','obj')) { $storyTargets+="rewrite/Ew4.$storyProject/$storyOutput" }
}
foreach($storyReport in @('scenario-events-20261021','scenario-control-20261021','scenario-factions-20261021','scenario-recurrence-diplomacy-20261021','victory_settlement_20261021','victory-mod-20261021','extensions-20261020','common-skills-20261021','commander-dialogues-20261021','deployment-roster-20261022')) {
    $storyTargets+="rewrite/reports/$storyReport"
}
$storyProcesses=@(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.Name -in @('dotnet.exe','Ew4.Web.exe','node.exe') })
$storyTracked=@(git -C $storyRoot ls-files)
$storyItems=@()
foreach($storyRelative in $storyTargets) {
    $storyAbsolute=[IO.Path]::GetFullPath((Join-Path $storyRoot $storyRelative))
    if(-not $storyAbsolute.StartsWith($storyRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Out of workspace' }
    if(-not (Test-Path -LiteralPath $storyAbsolute)) { continue }
    if((Resolve-Path -LiteralPath $storyAbsolute).Path -ne $storyAbsolute) { throw 'Resolution mismatch' }
    $storyChildren=@(Get-Item -LiteralPath $storyAbsolute -Force)+@(Get-ChildItem -LiteralPath $storyAbsolute -Recurse -Force)
    if($storyChildren | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Linked target: $storyAbsolute" }
    if($storyTracked | Where-Object { $_ -eq $storyRelative -or $_.StartsWith($storyRelative+'/',[StringComparison]::OrdinalIgnoreCase) }) { throw "Tracked target: $storyRelative" }
    if($storyProcesses | Where-Object { $_.CommandLine.Replace('/','\').Contains($storyAbsolute) }) { throw "Active target: $storyAbsolute" }
    $storyFiles=@($storyChildren | Where-Object { -not $_.PSIsContainer })
    $storyItems+= [pscustomobject]@{Path=$storyAbsolute;Files=$storyFiles.Count;Bytes=($storyFiles|Measure-Object Length -Sum).Sum}
}
if(-not $Execute) { $storyItems | Format-Table -AutoSize; exit }
foreach($storyItem in $storyItems) { Remove-Item -LiteralPath $storyItem.Path -Recurse -Force }
$storyReceipt=[pscustomobject]@{Targets=$storyItems.Count;Files=($storyItems|Measure-Object Files -Sum).Sum;Bytes=($storyItems|Measure-Object Bytes -Sum).Sum;Remaining=@($storyItems|Where-Object {Test-Path -LiteralPath $_.Path}).Count;Errors=0}
$storyReceiptDirectory=Join-Path $storyRoot 'build/maintenance-20261027'
New-Item -ItemType Directory -Path $storyReceiptDirectory -Force | Out-Null
$storyReceipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $storyReceiptDirectory 'story-playback-cleanup.json') -Encoding utf8
$storyReceipt | ConvertTo-Json -Compress
