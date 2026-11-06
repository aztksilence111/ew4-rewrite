param([ValidateSet('20261105','20261106')][string]$RevisionDate='20261105')
$ErrorActionPreference='Stop'
$airRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$airHome=Join-Path $airRepo '颓垣饿鸠/art/airfield-v1'
$airSuffix=if($RevisionDate -eq '20261106'){'-20261106'}else{''}
$airBuildRelative=if($RevisionDate -eq '20261106'){'build/airfield-fix-20261106'}else{'build/airfield-20261105'}
$airQaExecutable=if($RevisionDate -eq '20261106'){'build/airfield-fix-20261106/publish/Ew4.Web.exe'}else{'build/airfield-20261105/publish-final/Ew4.Web.exe'}
$airBefore=Get-Content -LiteralPath (Join-Path $airHome ('profile-preservation'+$airSuffix+'.json')) -Raw | ConvertFrom-Json
if(!$airBefore.files){throw 'Detailed pre-deployment hashes are absent; this completed cleanup cannot be replayed'}
$airChanged=@($airBefore.files | Where-Object {
    !(Test-Path -LiteralPath $_.path -PathType Leaf) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256
})
if($airChanged.Count){throw "Protected original profile changed: $($airChanged.Count)"}
$airPackage=Get-Content -LiteralPath (Join-Path $airHome 'package.json') -Raw | ConvertFrom-Json
if((Get-FileHash -LiteralPath $airPackage.package -Algorithm SHA256).Hash.ToLowerInvariant() -ne $airPackage.sha256){throw 'Final package hash differs'}
$airQa=Get-Content -LiteralPath (Join-Path $airHome 'qa/server.json') -Raw | ConvertFrom-Json
$airProcess=Get-CimInstance Win32_Process -Filter "ProcessId=$($airQa.pid)"
if($airProcess){
    if($airProcess.Name -ne 'Ew4.Web.exe' -or !$airProcess.CommandLine.Replace('\','/').Contains($airQaExecutable) -or !$airProcess.CommandLine.Contains('57946')){throw 'QA host identity differs'}
    Stop-Process -Id $airQa.pid
    Wait-Process -Id $airQa.pid -Timeout 10 -ErrorAction SilentlyContinue
}
$airTargets=@($airBuildRelative,'颓垣饿鸠/art/airfield-v1/stage','颓垣饿鸠/art/airfield-v1/qa',
    '颓垣饿鸠/art/airfield-v1/__pycache__','颓垣饿鸠/art/airfield-v1/Package/bin','颓垣饿鸠/art/airfield-v1/Package/obj')
foreach($airProject in @('Ew4.Core','Ew4.Client','Ew4.Legacy','Ew4.Mods','Ew4.Persistence','Ew4.Verify','Ew4.Web','Ew4.Android')){
    $airTargets+="rewrite/$airProject/bin";$airTargets+="rewrite/$airProject/obj"
}
if($RevisionDate -eq '20261106'){
    foreach($airRole in @('interceptor','fighter','bomber','attack_air','recon_air','transport_air','fuel_transport_air')){
        foreach($airMotion in @('reload','finish','support','die')){$airTargets+="颓垣饿鸠/art/airfield-v1/review/$airRole/$airMotion.gif"}
    }
}
$airGitBefore=(& git -C $airRepo status --porcelain=v1 -uno) -join "`n"
$airConsumers=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -in @('Ew4.Web.exe','dotnet.exe','node.exe','python.exe','Ew4.Verify.exe')})
$airPlan=@()
foreach($airRelative in $airTargets){
    $airTarget=[IO.Path]::GetFullPath((Join-Path $airRepo $airRelative))
    if(!$airTarget.StartsWith($airRepo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup target escaped workspace'}
    if(!(Test-Path -LiteralPath $airTarget)){continue}
    $airItem=Get-Item -LiteralPath $airTarget
    $airItems=@($airItem)
    if($airItem.PSIsContainer){$airItems+=@(Get-ChildItem -LiteralPath $airTarget -Recurse -Force)}
    if(@($airItems | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw "Linked cleanup target: $airRelative"}
    if(@(& git -C $airRepo ls-files -- $airRelative).Count){throw "Tracked cleanup target: $airRelative"}
    $airNeedle=$airTarget.Replace('\','/')+'/'
    if(@($airConsumers | Where-Object {$_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($airNeedle)}).Count){throw "Active cleanup consumer: $airRelative"}
    $airFiles=@($airItems | Where-Object {!$_.PSIsContainer})
    $airBytes=($airFiles | Measure-Object Length -Sum).Sum
    $airPlan+=[pscustomobject]@{path=$airTarget;files=$airFiles.Count;bytes=[long]$airBytes}
}
$airPlan | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $airHome ('cleanup-plan'+$airSuffix+'.json')) -Encoding utf8
foreach($airEntry in $airPlan){Remove-Item -LiteralPath $airEntry.path -Recurse -Force}
$airRemaining=@($airPlan | Where-Object {Test-Path -LiteralPath $_.path}).Count
$airGitAfter=(& git -C $airRepo status --porcelain=v1 -uno) -join "`n"
$airCatalog=Invoke-RestMethod 'http://127.0.0.1:56180/api/catalog'
$airMods=Invoke-RestMethod 'http://127.0.0.1:56180/api/mods'
$airReceipt=[ordered]@{targets=$airPlan.Count;files=($airPlan|Measure-Object files -Sum).Sum;bytes=($airPlan|Measure-Object bytes -Sum).Sum;
    remaining=$airRemaining;errors=0;trackedGitUnchanged=($airGitBefore -eq $airGitAfter);protectedOriginalFiles=$airBefore.files.Count;protectedChanges=$airChanged.Count;
    preview='http://127.0.0.1:56180';active=$airMods.active;hasSave=$airCatalog.data.hasSave;qaStopped=$airQa.pid;responsePreviewChanged=$false}
if($airRemaining -or !$airReceipt.trackedGitUnchanged -or $airReceipt.hasSave){throw 'Cleanup or clean preview verification failed'}
$airReceipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $airHome ('cleanup'+$airSuffix+'.json')) -Encoding utf8
$airReceipt | ConvertTo-Json -Compress
