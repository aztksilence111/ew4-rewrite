param([int[]]$TestProcessIds = @(17028,30868))
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$batch = Join-Path $workspace 'build/release-0.2.26-20261029'
$stage = Join-Path $batch 'replacement'
$manifest = Get-Content (Join-Path $stage 'assets/release-manifest.json') -Raw | ConvertFrom-Json
$audit = Get-Content (Join-Path $stage 'remote-audit.json') -Raw | ConvertFrom-Json
if ($audit.errors -ne 0 -or $audit.source -ne $manifest.source -or $audit.assets.Count -ne 4) { throw 'Verified replacement required' }
foreach ($entry in $manifest.files) {
    $file = Get-Item -LiteralPath (Join-Path $stage "assets/$($entry.name)")
    if ($file.Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $entry.sha256) { throw 'Replacement package changed' }
}
$playerFiles = Get-Content (Join-Path $stage 'player-data-before.json') -Raw | ConvertFrom-Json
function CheckPlayerFiles {
    foreach ($entry in $playerFiles) {
        if (-not (Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.sha256) { throw "Player file changed: $($entry.path)" }
    }
}
CheckPlayerFiles
$before = @(git -C $workspace status --short)
foreach ($processId in $TestProcessIds) {
    $hostProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$processId"
    if (-not $hostProcess) { continue }
    if ($hostProcess.Name -ne 'Ew4.Web.exe' -or -not $hostProcess.CommandLine.Replace('/','\').Contains($stage + '\web\Ew4.Web.exe')) { throw 'Unexpected test process' }
    Stop-Process -Id $processId
    Wait-Process -Id $processId -ErrorAction SilentlyContinue
}
$oldReports = @('android-assemblies.json','android-verification.json','cleanup.json','client-verification.json',
    'defeated-faction-verification.json','diplomacy-verification.json','player-data-before.json','post-cleanup-verification.json',
    'release-remote.json','release-upload-progress.json','remote-audit.json','save-export-verification.json','signature.txt',
    'startup-assets-verification.json','validation-summary.json','verification.json','web-verification.json')
$unexpected = @(Get-ChildItem -LiteralPath $batch -File | Where-Object { $_.Name -notin $oldReports })
if ($unexpected.Count) { throw 'Unexpected file in old release root' }
$targets = @((Join-Path $batch 'assets'),(Join-Path $batch 'web'),(Join-Path $stage 'testing'),
    (Join-Path $workspace 'build/shop-commander-check-20261029'),(Join-Path $workspace 'rewrite/Ew4.ModChecks/bin'))
$targets += @($oldReports | ForEach-Object { Join-Path $batch $_ })
$processes = @(Get-CimInstance Win32_Process)
$rows = @()
foreach ($target in $targets) {
    $absolute = [IO.Path]::GetFullPath($target)
    if (-not $absolute.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace' }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw 'Resolution mismatch' }
    $relative = [IO.Path]::GetRelativePath($workspace,$absolute).Replace('\','/')
    if (@(git -C $workspace ls-files -- $relative).Count) { throw "Tracked input: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/','\').Contains($absolute) }) { throw "Active consumer: $relative" }
    $item = Get-Item -LiteralPath $absolute -Force
    $items = @($item)
    if ($item.PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Linked target: $relative" }
    if ($items | Where-Object { $_.Name -in @('saves','profile','profiles','content-mods','account.json') }) {
        if ($absolute -notin @((Join-Path $stage 'testing'),(Join-Path $workspace 'build/shop-commander-check-20261029'))) { throw "Player content: $relative" }
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
}
foreach ($row in $rows) { Remove-Item -LiteralPath (Join-Path $workspace $row.path) -Recurse -Force }
foreach ($name in 'assets','web') {
    $source = Join-Path $stage $name
    if ((Resolve-Path -LiteralPath $source).Path -ne $source) { throw 'Promotion source mismatch' }
    Move-Item -LiteralPath $source -Destination (Join-Path $batch $name)
}
foreach ($file in Get-ChildItem -LiteralPath $stage -File) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $batch $file.Name) }
if (@(Get-ChildItem -LiteralPath $stage -Force).Count) { throw 'Unexpected replacement leftovers' }
Remove-Item -LiteralPath $stage
CheckPlayerFiles
$receipt = [ordered]@{replacement=$true;source=$manifest.source;targets=$rows;files=($rows|Measure-Object files -Sum).Sum;
    bytes=($rows|Measure-Object bytes -Sum).Sum;remaining=0;errors=0;playerFiles=$playerFiles.Count;playerHashChanges=0;
    retained=@('release-0.2.26-20261029','release-0.2.25-20261028','release-0.2.24-20261027');
    gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $workspace status --short)));
    preserved='Active previews, shared frontend, player accounts/saves/Mods, source checks, signing material and original input'}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $batch 'cleanup.json') -Encoding utf8
[pscustomobject]$receipt | Select-Object replacement,files,bytes,remaining,errors,playerFiles,playerHashChanges,retained,gitStatusUnchanged | ConvertTo-Json -Compress
