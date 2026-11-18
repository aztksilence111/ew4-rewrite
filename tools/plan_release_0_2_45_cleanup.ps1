param([string]$Root='E:/Workplace/game/o4mod',[string]$Checkout='C:/Users/WIN11/.codex/worktrees/r-release-0-2-43/o4mod')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path -LiteralPath $Root).Path
$Checkout=(Resolve-Path -LiteralPath $Checkout).Path
$batch=Join-Path $Root 'build/release-0.2.45-20261118'
$audit=Get-Content "$batch/remote-audit.json" -Raw|ConvertFrom-Json
if($audit.errors -ne 0 -or $audit.tag -ne 'v0.2.45-20261118' -or $audit.assets.Count -ne 5){throw 'Published release audit required'}
$protected=@{}
function Sha([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Protect([string]$path){if(Test-Path -LiteralPath $path -PathType Leaf){$protected[[IO.Path]::GetFullPath($path)]=Sha $path}}
foreach($record in (Get-Content "$batch/player-data-before.json" -Raw|ConvertFrom-Json)){
    if((Sha $record.path) -ne $record.sha256){throw "Player file changed: $($record.path)"}
    $protected[$record.path]=$record.sha256
}
foreach($version in @('0.2.43','0.2.44','0.2.45')){
    $assetRoot=Join-Path $Root "build/release-$version-20261118/assets"
    $manifest=Get-Content "$assetRoot/release-manifest.json" -Raw|ConvertFrom-Json
    foreach($entry in $manifest.files){$file=Join-Path $assetRoot $entry.name;if((Sha $file).ToLowerInvariant() -ne $entry.sha256){throw "Retained package changed: $file"};Protect $file}
    Protect "$assetRoot/release-manifest.json"
    Protect "$assetRoot/SHA256SUMS.txt"
}
Protect 'C:/Users/WIN11/.codex/keys/ew4-release-debug.keystore'
$mainTargets=[Collections.Generic.List[object]]::new()
$checkoutTargets=[Collections.Generic.List[object]]::new()
function Add-Target([string]$path,[bool]$testOnly=$false){
    $path=[IO.Path]::GetFullPath($path)
    if(!(Test-Path -LiteralPath $path)){return}
    if($path.StartsWith((Join-Path $batch 'web')+'\',[StringComparison]::OrdinalIgnoreCase) -or $path -eq (Join-Path $batch 'web')){throw 'Current runtime is protected'}
    if(@($protected.Keys|Where-Object {$_ -eq $path -or $_.StartsWith($path+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){throw "Protected target: $path"}
    $row=[ordered]@{path=$path;testOnly=$testOnly}
    if($path.StartsWith($Checkout+'\',[StringComparison]::OrdinalIgnoreCase)){$checkoutTargets.Add($row)}
    elseif($path.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)){$mainTargets.Add($row)}else{throw 'Target escaped project'}
}
function Prune-ImmutableKit([string]$kit){
    $listing=Join-Path $kit 'files.sha256.json'
    if(!(Test-Path -LiteralPath $listing)){return}
    $known=@{}
    foreach($entry in (Get-Content -LiteralPath $listing -Raw|ConvertFrom-Json).files){$known[$entry.path]=$entry.sha256}
    foreach($name in @('project','exports','_work','signing')){
        $path=Join-Path $kit $name
        if(Test-Path -LiteralPath $path){foreach($file in Get-ChildItem -LiteralPath $path -File -Recurse){Protect $file.FullName}}
    }
    foreach($child in Get-ChildItem -LiteralPath $kit -Force){
        if($child.Name -in @('project','exports','_work','signing')){continue}
        $files=if($child.PSIsContainer){@(Get-ChildItem -LiteralPath $child.FullName -File -Recurse)}else{@($child)}
        $safe=$true
        foreach($file in $files){
            $relative=[IO.Path]::GetRelativePath($kit,$file.FullName).Replace('\','/')
            if($relative -eq 'files.sha256.json'){continue}
            if(!$known.ContainsKey($relative) -or (Sha $file.FullName).ToLowerInvariant() -ne $known[$relative]){$safe=$false;break}
        }
        # These are hash-verified generated package copies. Reference examples may
        # contain a directory literally named profiles; writable author inputs above are excluded.
        if($safe){Add-Target $child.FullName $true}
    }
}
foreach($version in @('0.2.42','0.2.43','0.2.45')){
    $date=if($version -eq '0.2.42'){'20261116'}else{'20261118'}
    Prune-ImmutableKit (Join-Path $Root "build/release-$version-$date/author/EW4-Mod-Author-$version-win-x64")
}
Prune-ImmutableKit (Join-Path $Root 'build/author-kit-0.2.44-1.1.10-20261118/EW4-Mod-Author-0.2.44-kit1.1.10-win-x64')
Prune-ImmutableKit (Join-Path $Root 'build/author-kit-0.2.34-20261109/EW4-Mod-Author-0.2.34-win-x64')
foreach($relative in @('build/release-0.2.45-20261118/qa','build/release-0.2.45-20261118/android',
    'build/release-0.2.42-20261116/web','build/release-0.2.43-20261118/web','build/release-0.2.44-20261118/web',
    'build/item-gallery-20261118/runtime','build/hq-objectives-20261025/checks','build/startup-assets-20261026/checks',
    'build/turn-country-preview-20261113/artifacts','build/turn-country-preview-20261113/web-artifacts')){Add-Target (Join-Path $Root $relative) $true}
$feedback=Join-Path $Root 'build/feedback-undo-fix-20261022/client'
if(Test-Path -LiteralPath $feedback){
    $children=@(Get-ChildItem -LiteralPath $feedback -Force)
    if($children.Count -and !@($children|Where-Object {!$_.PSIsContainer -or $_.Name -notmatch '^[0-9a-f]{32}$'}).Count){Add-Target $feedback $true}
}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $Root 'build/release-0.2.42-20261116/assets') -File|Where-Object {$_.Extension -in @('.apk','.zip')}){Add-Target $file.FullName}
$newKit=Join-Path $batch 'author/EW4-Mod-Author-0.2.45-win-x64.zip'
if((Sha $newKit) -ne (Sha (Join-Path $batch 'assets/EW4-Mod-Author-0.2.45-win-x64.zip'))){throw 'Duplicate author ZIP differs'}
Add-Target $newKit
Add-Target ($newKit+'.sha256')
$oldKit=Join-Path $Root 'build/author-kit-0.2.44-1.1.10-20261118/EW4-Mod-Author-0.2.44-kit1.1.10-win-x64.zip'
if(Test-Path -LiteralPath $oldKit){
    $receipt=Get-Content (Join-Path $Root 'build/author-signing-maintenance-20261118/completion.json') -Raw|ConvertFrom-Json
    if((Sha $oldKit).ToLowerInvariant() -ne $receipt.zipSha256){throw 'Preserve modified author archive'}
    Add-Target $oldKit
    Add-Target ($oldKit+'.sha256')
}
foreach($workspace in @($Root,$Checkout)){
    foreach($project in Get-ChildItem -LiteralPath (Join-Path $workspace 'rewrite') -Directory -Filter 'Ew4.*'){
        foreach($name in @('bin','obj')){Add-Target (Join-Path $project.FullName $name) $true}
    }
}
Add-Target (Join-Path $Checkout 'rewrite/Ew4.Web/wwwroot/compat') $true
Add-Target (Join-Path $Checkout 'rewrite/Ew4.Web/wwwroot/build-info.json') $true
$records=@($protected.GetEnumerator()|ForEach-Object {[ordered]@{path=$_.Key;sha256=$_.Value}})
$records|ConvertTo-Json -Depth 4|Set-Content "$batch/cleanup-protected.json" -Encoding utf8
$links=@(foreach($relative in @('tools/frontend/node_modules','build/release-0.2.45-20261118')){
    $item=Get-Item -LiteralPath (Join-Path $Checkout $relative)
    if($item.LinkType -ne 'Junction'){throw 'Expected dependency junction'}
    [ordered]@{path=$item.FullName;target=$item.Target}
})
[ordered]@{schema=1;workspace=$Root;receipt="$batch/cleanup-main.json";targets=@($mainTargets);protectedFiles=$records;links=@()}|ConvertTo-Json -Depth 7|Set-Content "$batch/cleanup-main-manifest.json" -Encoding utf8
[ordered]@{schema=1;workspace=$Checkout;receipt="$batch/cleanup-checkout.json";targets=@($checkoutTargets);protectedFiles=$records;links=$links}|ConvertTo-Json -Depth 7|Set-Content "$batch/cleanup-checkout-manifest.json" -Encoding utf8
[ordered]@{mainTargets=$mainTargets.Count;checkoutTargets=$checkoutTargets.Count;protectedFiles=$records.Count;links=$links.Count}|ConvertTo-Json -Compress
