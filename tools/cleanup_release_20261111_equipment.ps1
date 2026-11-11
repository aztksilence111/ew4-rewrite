param(
    [string]$Workspace = 'E:/Workplace/game/o4mod',
    [string]$ReleaseCheckout = (Split-Path $PSScriptRoot -Parent),
    [switch]$Execute
)
$ErrorActionPreference = 'Stop'
$Workspace = (Resolve-Path -LiteralPath $Workspace).Path
$ReleaseCheckout = (Resolve-Path -LiteralPath $ReleaseCheckout).Path
$batch = Join-Path $Workspace 'build/release-0.2.38-20261111'
$summary = Get-Content "$batch/validation-summary.json" -Raw | ConvertFrom-Json
$audit = Get-Content "$batch/remote-audit.json" -Raw | ConvertFrom-Json
$verification = Get-Content "$batch/verification.json" -Raw | ConvertFrom-Json
if ($summary.errors -or $audit.errors -or $verification.errors -or $summary.coreChecks -ne 326170 -or $summary.authorChecks -ne 6322 -or $verification.nativeLibraries -ne 20) { throw 'Release verification incomplete' }
$protected = @(Get-Content "$batch/player-data-before.json" -Raw | ConvertFrom-Json)
function Check-Players {
    foreach ($entry in $protected) {
        if (!(Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.sha256) { throw 'Player data changed' }
    }
}
Check-Players
$hostInfo = Get-Content "$batch/test-host.json" -Raw | ConvertFrom-Json
$hostProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($hostInfo.pid)"
if ($hostProcess) {
    if ($hostProcess.ExecutablePath -ne $hostInfo.path.Replace('/','\') -or $hostProcess.CommandLine -notlike "*127.0.0.1:$($hostInfo.port)*") { throw 'Test host identity changed' }
    if ($Execute) { Stop-Process -Id $hostInfo.pid -ErrorAction Stop }
}
$beforeMain = @(git -C $Workspace status --porcelain=v1 --untracked-files=normal)
$beforeCheckout = @(git -C $ReleaseCheckout status --porcelain=v1 --untracked-files=normal)
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.ProcessId -ne $hostInfo.pid -and $_.CommandLine })
$targets = [Collections.Generic.List[object]]::new()
function Add-Target([string]$Root, [string]$Relative) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (!$absolute.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Outside cleanup root' }
    if (!(Test-Path -LiteralPath $absolute)) { return }
    if (@(git -C $Root ls-files -- $Relative).Count) { throw "Tracked cleanup target: $Relative" }
    foreach ($entry in $protected) {
        if ($entry.path -eq $absolute -or $entry.path.StartsWith($absolute + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Protected cleanup target' }
    }
    $ancestor = $absolute
    while ($ancestor -ne $Root) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Linked cleanup ancestor' }
        $ancestor = Split-Path $ancestor -Parent
    }
    $children = @(Get-Item -LiteralPath $absolute -Force)
    if ($children[0].PSIsContainer) { $children += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if (@($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }).Count) { throw 'Linked cleanup child' }
    foreach ($process in $processes) {
        $command = $process.CommandLine.Replace('/','\')
        if ($command.Contains($absolute + '\') -or $process.ExecutablePath -eq $absolute) { throw "Active cleanup consumer: $Relative" }
    }
    $files = @($children | Where-Object { !$_.PSIsContainer })
    $targets.Add([pscustomobject]@{path=$absolute;files=$files.Count;bytes=[long]($files | Measure-Object Length -Sum).Sum})
}
foreach ($name in @('android-artifacts','web-artifacts','check-artifacts','mod-artifacts','author-build','android','testing')) {
    Add-Target $Workspace "build/release-0.2.38-20261111/$name"
}
foreach ($file in Get-ChildItem -LiteralPath $batch -File -Filter '*.log') { Add-Target $Workspace "build/release-0.2.38-20261111/$($file.Name)" }
foreach ($file in Get-ChildItem -LiteralPath "$batch/author" -File | Where-Object Name -Match '\.zip(\.sha256)?$') { Add-Target $Workspace "build/release-0.2.38-20261111/author/$($file.Name)" }
# These roots did not exist before this newly created, isolated release checkout.
if ($ReleaseCheckout -eq $Workspace -or $ReleaseCheckout -notlike '*\.codex\worktrees\release-0-2-36\o4mod') { throw 'Expected isolated release checkout' }
foreach ($relative in @('build','apk_work','rewrite/reports')) { Add-Target $ReleaseCheckout $relative }
$batches = @(Get-ChildItem (Join-Path $Workspace 'build') -Directory -Filter 'release-*' |
    Where-Object Name -Match '^release-\d+\.\d+\.\d+-\d{8}$' |
    Sort-Object { [version]($_.Name -replace '^release-','' -replace '-\d{8}$','') } -Descending)
$retained = @($batches | Select-Object -First 3)
if (($retained.Name -join ',') -ne 'release-0.2.38-20261111,release-0.2.37-20261111,release-0.2.36-20261110') { throw 'Release retention set changed' }
foreach ($older in $batches | Select-Object -Skip 3) {
    Add-Target $Workspace "build/$($older.Name)/assets"
    $author = Join-Path $older.FullName 'author'
    if (Test-Path -LiteralPath $author) {
        $authorInputsPristine = $true
        foreach ($kit in Get-ChildItem -LiteralPath $author -Directory) {
            $inventoryPath = Join-Path $kit.FullName 'files.sha256.json'
            if (!(Test-Path -LiteralPath $inventoryPath)) { $authorInputsPristine = $false; continue }
            $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
            $knownFiles = @('files.sha256.json','project/author-project.json','project/rewrite/extension.json') + @($inventory.files.path)
            foreach ($file in Get-ChildItem -LiteralPath $kit.FullName -File -Recurse) {
                $relativeFile = [IO.Path]::GetRelativePath($kit.FullName,$file.FullName).Replace('\','/')
                if ($relativeFile -notin $knownFiles) { $authorInputsPristine = $false }
            }
            foreach ($entry in $inventory.files) {
                $inputFile = Join-Path $kit.FullName $entry.path
                if (!(Test-Path -LiteralPath $inputFile) -or (Get-Item -LiteralPath $inputFile).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $inputFile).Hash.ToLowerInvariant() -ne $entry.sha256) { $authorInputsPristine = $false }
            }
            $project = Join-Path $kit.FullName 'project'
            if (!(Test-Path -LiteralPath $project)) { $authorInputsPristine = $false; continue }
            $projectFiles = @(Get-ChildItem -LiteralPath $project -File -Recurse)
            $metadataPath = Join-Path $project 'author-project.json'
            $extensionPath = Join-Path $project 'rewrite/extension.json'
            if ($projectFiles.Count -ne 2 -or !(Test-Path -LiteralPath $metadataPath) -or !(Test-Path -LiteralPath $extensionPath)) { $authorInputsPristine = $false; continue }
            $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
            $extension = Get-Content -LiteralPath $extensionPath -Raw | ConvertFrom-Json
            if ($metadata.id -ne 'my-mod' -or $metadata.name -ne '我的Mod' -or $metadata.version -ne '1.0.0' -or @($metadata.PSObject.Properties).Count -ne 3 -or $extension.schema -ne 1 -or $extension.apiVersion -ne 3 -or @($extension.PSObject.Properties).Count -ne 2) { $authorInputsPristine = $false }
        }
        $playerNames = @('saves','profile','profiles','mods','content-mods','projects','output')
        $playerDirectories = @(Get-ChildItem -LiteralPath $author -Directory -Recurse | Where-Object Name -In $playerNames | Where-Object { $_.FullName -notlike "$author\*\reference\*" -and @(Get-ChildItem -LiteralPath $_.FullName -Force).Count -gt 0 })
        $authorConsumers = @($processes | Where-Object { $_.CommandLine.Replace('/','\').Contains($author) })
        if ($authorInputsPristine -and !$playerDirectories.Count -and !$authorConsumers.Count) { Add-Target $Workspace "build/$($older.Name)/author" }
    }
    $runtime = Join-Path $older.FullName 'web'
    if (!(Test-Path -LiteralPath $runtime)) { continue }
    $players = @(Get-ChildItem -LiteralPath $runtime -Directory | Where-Object Name -In @('saves','profile','profiles','mods','content-mods'))
    $consumers = @($processes | Where-Object { $_.CommandLine.Replace('/','\').Contains($runtime) })
    if (!$players.Count -and !$consumers.Count) { Add-Target $Workspace "build/$($older.Name)/web" }
}
function Check-Packages {
    $checks = 0
    foreach ($kept in $retained) {
        $manifest = Get-Content (Join-Path $kept.FullName 'assets/release-manifest.json') -Raw | ConvertFrom-Json
        foreach ($entry in $manifest.files) {
            $file = Join-Path $kept.FullName ('assets/' + $entry.name)
            if ((Get-Item -LiteralPath $file).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Retained package mismatch' }
            $checks++
        }
    }
    return $checks
}
$packageChecks = Check-Packages
$plan = [ordered]@{targets=$targets.Count;files=($targets | Measure-Object files -Sum).Sum;bytes=($targets | Measure-Object bytes -Sum).Sum;retained=@($retained.Name);items=$targets.ToArray()}
$plan | ConvertTo-Json -Depth 5 | Set-Content "$batch/cleanup-plan.json" -Encoding utf8
if (!$Execute) { [pscustomobject]$plan | Select-Object targets,files,bytes,retained | ConvertTo-Json -Compress; exit }
foreach ($target in $targets) { Remove-Item -LiteralPath $target.path -Recurse -Force -ErrorAction Stop }
$residual = @($targets | Where-Object { Test-Path -LiteralPath $_.path }).Count
Check-Players
$packageChecks = Check-Packages
$sameMain = (($beforeMain -join "`n") -eq ((@(git -C $Workspace status --porcelain=v1 --untracked-files=normal)) -join "`n"))
$sameCheckout = (($beforeCheckout -join "`n") -eq ((@(git -C $ReleaseCheckout status --porcelain=v1 --untracked-files=normal)) -join "`n"))
$receipt = [ordered]@{date='2026-11-11';targets=$targets.Count;files=$plan.files;bytes=$plan.bytes;residual=$residual;errors=0;gitPreserved=($sameMain -and $sameCheckout);protectedPlayerFiles=$protected.Count;playerChanges=0;retained=@($retained.Name);retainedPackageChecks=$packageChecks;retainedPackageErrors=0;stoppedTestHost=$hostInfo.pid;androidDeviceExecution=$false}
$receipt | ConvertTo-Json -Depth 4 | Set-Content "$batch/cleanup.json" -Encoding utf8
$receipt | ConvertTo-Json -Compress
if ($residual -or !$sameMain -or !$sameCheckout) { throw 'Cleanup verification failed' }
