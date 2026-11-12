param(
    [string]$Root = 'E:/Workplace/game/o4mod',
    [string]$Worktree = 'C:/Users/WIN11/.codex/worktrees/release-sea-installation-0-2-40/o4mod'
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath $Root).Path
$Worktree = (Resolve-Path -LiteralPath $Worktree).Path
$releaseRoot = Join-Path $Root 'build/release-0.2.40-20261112'
$oldRoot = Join-Path $Root 'build/release-0.2.37-20261111'
$utf8 = [Text.UTF8Encoding]::new($false)
function Save-Json($Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), $utf8) }
function Sha($Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Git-State($Path) { (& git -C $Path status --porcelain --untracked-files=all) -join "`n" }
$sourceStatus = Git-State $Root
$worktreeStatus = Git-State $Worktree
$audit = Get-Content -LiteralPath (Join-Path $releaseRoot 'remote-audit.json') -Raw | ConvertFrom-Json
if ($audit.errors -ne 0 -or $audit.tag -ne 'v0.2.40-20261112' -or $audit.assets.Count -ne 5) { throw 'Release must be published and verified first' }
foreach ($asset in $audit.assets) {
    $file = Join-Path $releaseRoot ('assets/' + $asset.name)
    if (!(Test-Path -LiteralPath $file) -or ('sha256:' + (Sha $file)) -ne $asset.digest) { throw 'Verified asset changed' }
}
$protected = @{}
foreach ($item in (Get-Content -LiteralPath (Join-Path $Root 'build/release-0.2.39-20261111/player-data-before.json') -Raw | ConvertFrom-Json)) {
    if (Test-Path -LiteralPath $item.path -PathType Leaf) { $protected[$item.path] = Sha $item.path }
}
foreach ($version in @('0.2.37','0.2.40')) {
    $date = if ($version -eq '0.2.40') { '20261112' } else { '20261111' }
    $kit = Join-Path $Root ("build/release-$version-$date/author/EW4-Mod-Author-$version-win-x64")
    foreach ($name in @('project','exports','_work')) {
        $path = Join-Path $kit $name
        if (Test-Path -LiteralPath $path) {
            foreach ($file in Get-ChildItem -LiteralPath $path -Recurse -File) { $protected[$file.FullName] = Sha $file.FullName }
        }
    }
}
Save-Json (Join-Path $releaseRoot 'player-data-before.json') @($protected.GetEnumerator() | ForEach-Object { @{path=$_.Key;sha256=$_.Value} })
$pidFile = Join-Path $releaseRoot 'testing/server.pid'
if (Test-Path -LiteralPath $pidFile) {
    $testPid = [int](Get-Content -LiteralPath $pidFile)
    $process = Get-CimInstance Win32_Process -Filter ("ProcessId=$testPid")
    if ($process) {
        $line = $process.CommandLine.Replace('/','\')
        if ($process.Name -ne 'Ew4.Web.exe' -or !$line.Contains($releaseRoot + '\web\Ew4.Web.exe') -or !$line.Contains('56248') -or !$line.Contains('\testing\profile')) { throw 'Test server identity changed' }
        Stop-Process -Id $testPid
        Wait-Process -Id $testPid -Timeout 15 -ErrorAction SilentlyContinue
    }
}
$targets = [Collections.Generic.List[string]]::new()
foreach ($name in @('android','android-artifacts','web-artifacts','check-artifacts','checks','mod-artifacts','mod-checks','author-build','testing')) {
    $targets.Add((Join-Path $releaseRoot $name))
}
foreach ($file in Get-ChildItem -LiteralPath $releaseRoot -File -Filter '*.log') { $targets.Add($file.FullName) }
foreach ($suffix in @('.zip','.zip.sha256')) { $targets.Add((Join-Path $releaseRoot ('author/EW4-Mod-Author-0.2.40-win-x64' + $suffix))) }
$targets.Add((Join-Path $oldRoot 'assets'))
$targets.Add((Join-Path $oldRoot 'web'))
$oldKit = Join-Path $oldRoot 'author/EW4-Mod-Author-0.2.37-win-x64'
$inventory = Get-Content -LiteralPath (Join-Path $oldKit 'files.sha256.json') -Raw | ConvertFrom-Json
$known = @{}
foreach ($entry in $inventory.files) {
    $known[$entry.path] = $true
    $file = Join-Path $oldKit $entry.path
    if ((Test-Path -LiteralPath $file) -and (Sha $file) -ne $entry.sha256) { throw ('Preserve modified old author file: ' + $entry.path) }
}
foreach ($file in Get-ChildItem -LiteralPath $oldKit -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($oldKit, $file.FullName).Replace('\','/')
    if (!$known.ContainsKey($relative) -and $relative -ne 'files.sha256.json' -and $relative -notmatch '^(project|exports|_work)/') { throw ('Preserve unknown old author input: ' + $relative) }
    if ($relative -notmatch '^(project|exports|_work)/' -and $file.Extension -in @('.ew4mod','.ew4author','.keystore','.jks')) { throw ('Preserve raw author input: ' + $relative) }
}
foreach ($item in Get-ChildItem -LiteralPath $oldKit -Force) {
    if ($item.Name -notin @('project','exports','_work')) { $targets.Add($item.FullName) }
}
foreach ($item in Get-ChildItem -LiteralPath (Join-Path $Worktree 'build') -Force) {
    if ($item.Name -ne 'release-0.2.40-20261112') { $targets.Add($item.FullName) }
}
$targets = @($targets | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { (Resolve-Path -LiteralPath $_).Path } | Select-Object -Unique)
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.Name -in @('dotnet.exe','Ew4.Web.exe','Ew4.ModTool.exe','Ew4.Verify.exe','Ew4.ModChecks.exe','python.exe','node.exe') })
foreach ($target in $targets) {
    if (!$target.StartsWith($Root + '\build\', [StringComparison]::OrdinalIgnoreCase) -and !$target.StartsWith($Worktree + '\build\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup escaped the allowed build directories' }
    $item = Get-Item -LiteralPath $target -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Cleanup target is a reparse point' }
    if ($item.PSIsContainer -and @(Get-ChildItem -LiteralPath $target -Recurse -Force -Attributes ReparsePoint).Count) { throw 'Cleanup subtree contains a reparse point' }
    foreach ($path in $protected.Keys) {
        if ($path -eq $target -or $path.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Protected input is inside cleanup target' }
    }
    foreach ($process in $processes) {
        if ($process.CommandLine -and $process.CommandLine.Replace('/','\').Contains($target)) { throw 'Cleanup target has an active consumer' }
    }
    $repo = if ($target.StartsWith($Worktree + '\')) { $Worktree } else { $Root }
    $relative = [IO.Path]::GetRelativePath($repo, $target).Replace('\','/')
    if (@(& git -C $repo ls-files -- $relative).Count) { throw 'Cleanup would delete tracked files' }
}
$oldWeb = Join-Path $oldRoot 'web'
if (Test-Path -LiteralPath $oldWeb) {
    $playerFiles = @(Get-ChildItem -LiteralPath $oldWeb -Recurse -File | Where-Object { $_.Name -eq 'account.json' -or $_.FullName -match '\\saves\\|\\mods\\installed\\' })
    if ($playerFiles.Count) { throw 'Old runtime contains player files' }
}
Save-Json (Join-Path $releaseRoot 'cleanup-plan.json') @{targets=$targets;preserved=@('0.2.40/39/38 packages and runtimes','all player data and installed Mods','old author editable projects and exports','signing materials')}
$removed = @()
foreach ($target in $targets) {
    $item = Get-Item -LiteralPath $target -Force
    $files = if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $target -Recurse -Force -File) } else { @($item) }
    $bytes = ($files | Measure-Object Length -Sum).Sum
    $count = $files.Count
    Remove-Item -LiteralPath $target -Recurse -Force
    $removed += @{path=$target;files=$count;bytes=$bytes}
}
foreach ($path in $protected.Keys) { if (!(Test-Path -LiteralPath $path) -or (Sha $path) -ne $protected[$path]) { throw 'Protected input changed' } }
if ((Git-State $Root) -ne $sourceStatus -or (Git-State $Worktree) -ne $worktreeStatus) { throw 'Git state changed during cleanup' }
$remaining = @($targets | Where-Object { Test-Path -LiteralPath $_ })
$receipt = @{date='2026-11-12';targets=$removed.Count;files=($removed | Measure-Object files -Sum).Sum;bytes=($removed | Measure-Object bytes -Sum).Sum;remaining=$remaining.Count;protectedFiles=$protected.Count;protectedChanges=0;gitStatePreserved=$true;errors=0;removed=$removed}
Save-Json (Join-Path $releaseRoot 'cleanup.json') $receipt
$receipt | Select-Object targets,files,bytes,remaining,protectedFiles,protectedChanges,gitStatePreserved,errors | ConvertTo-Json -Compress
