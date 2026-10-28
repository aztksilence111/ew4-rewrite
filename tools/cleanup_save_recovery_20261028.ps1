param([switch]$Execute)
$ErrorActionPreference='Stop'
$recoveryRoot=(Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$recoveryTargets=@(
    'build/defeat-settlement-20261028',
    'rewrite/reports/victory_settlement_20261021',
    'rewrite/reports/victory-mod-20261021',
    'rewrite/reports/client-slot-test/run-b42db069a2ef4770a7201a2d68faf6d4',
    'rewrite/reports/equipment-fix-20261021/saves/7f9fc15e3c7d474ab8a17b148b533522',
    'rewrite/reports/equipment-fix-20261021/saves/8d4935bc8efd4a3a950b8ef0e8419d9e',
    'rewrite/reports/equipment-fix-20261021/saves/9baf19054cbb4a78b0e3bae30b812f68',
    'rewrite/reports/equipment-fix-20261021/saves/c676a18f184d46a0970d6c98f9fd98a5'
)
$recoveryGit=@(git -C $recoveryRoot status --porcelain)
$recoveryProcesses=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('dotnet.exe','Ew4.Web.exe','Ew4.Verify.exe','node.exe')})
$recoveryItems=@()
foreach($relative in $recoveryTargets) {
    $absolute=[IO.Path]::GetFullPath((Join-Path $recoveryRoot $relative))
    if(-not $absolute.StartsWith($recoveryRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Out of workspace'}
    if(-not (Test-Path -LiteralPath $absolute)){continue}
    if((Resolve-Path -LiteralPath $absolute).Path -ne $absolute){throw 'Resolution mismatch'}
    $children=@(Get-Item -LiteralPath $absolute -Force)+@(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
    if($children | Where-Object {$_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)}){throw "Linked target: $relative"}
    if(@(git -C $recoveryRoot ls-files -- $relative).Count){throw "Tracked target: $relative"}
    if($recoveryProcesses | Where-Object {$_.CommandLine -and ($_.CommandLine.Replace('/','\').Contains($absolute))}){throw "Active consumer: $relative"}
    $files=@($children | Where-Object {-not $_.PSIsContainer})
    $recoveryItems+=@{path=$relative;files=$files.Count;bytes=($files | Measure-Object Length -Sum).Sum}
}
if($Execute) {
    foreach($item in $recoveryItems){Remove-Item -LiteralPath (Join-Path $recoveryRoot $item.path) -Recurse -Force}
    $remaining=@($recoveryItems | Where-Object {Test-Path -LiteralPath (Join-Path $recoveryRoot $_.path)})
    if(Compare-Object $recoveryGit @(git -C $recoveryRoot status --porcelain)){throw 'Git status changed during cleanup'}
    $receipt=@{targets=$recoveryItems.Count;files=($recoveryItems | Measure-Object files -Sum).Sum;bytes=($recoveryItems | Measure-Object bytes -Sum).Sum;remaining=$remaining.Count;errors=0}
    $receiptFolder=Join-Path $recoveryRoot 'build/maintenance-20261028'
    New-Item -ItemType Directory -Path $receiptFolder -Force | Out-Null
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $receiptFolder 'save-recovery-cleanup.json') -Encoding utf8
    $receipt | ConvertTo-Json -Compress
} else {
    @{targets=$recoveryItems.Count;files=($recoveryItems | Measure-Object files -Sum).Sum;bytes=($recoveryItems | Measure-Object bytes -Sum).Sum} | ConvertTo-Json -Compress
}
