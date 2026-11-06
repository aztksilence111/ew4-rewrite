$ErrorActionPreference='Stop'
$airRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$airHome=Join-Path $airRepo '颓垣饿鸠/art/airfield-v1'
$airProfile=Join-Path $airRepo '颓垣饿鸠/portraits/game-preview/profiles/opening'
if(Get-NetTCPConnection -LocalPort 56180 -State Listen -ErrorAction SilentlyContinue){throw 'Opening preview is running; inspect it before replacing runtime'}
$airProtected=@(Get-ChildItem -LiteralPath $airProfile -Recurse -File | Where-Object {
    $_.Name -notin @('active.json','server.json','server.log','server-error.log')
} | ForEach-Object {[pscustomobject]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
@{files=$airProtected;previousActive=(Get-Content -LiteralPath (Join-Path $airProfile 'mods/active.json') -Raw)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $airHome 'profile-preservation-20261106.json') -Encoding utf8
$airRuntime=Join-Path $airRepo '颓垣饿鸠/runtime/airfield-20261106'
New-Item -ItemType Directory -Path $airRuntime -Force | Out-Null
$airPublished=Join-Path $airRepo 'build/airfield-fix-20261106/publish-final'
Get-ChildItem -LiteralPath $airPublished -File | Where-Object {$_.Extension -in @('.dll','.exe','.json')} |
    ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $airRuntime $_.Name)}
& (Join-Path $airRepo '颓垣饿鸠/portraits/game-preview/Start.ps1') -Variant opening -Port 56180
