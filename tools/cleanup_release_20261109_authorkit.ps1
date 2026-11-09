param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$relativeBatch = 'build/release-0.2.35-20261109'
$batch = Join-Path $workspace $relativeBatch
$protected = @(Get-Content "$batch/player-data-before.json" -Raw | ConvertFrom-Json)
function Check-Players {
    foreach ($entry in $protected) {
        if (!(Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.sha256) { throw 'Player data changed' }
    }
}
Check-Players
if ($Execute) {
    foreach ($name in 'remote-audit','verification','validation-summary') {
        if ((Get-Content "$batch/$name.json" -Raw | ConvertFrom-Json).errors -ne 0) { throw 'Release validation incomplete' }
    }
    $hostEntry = Get-Content "$batch/test-hosts.json" -Raw | ConvertFrom-Json
    $hostProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($hostEntry.pid)"
    if ($hostProcess) {
        if ($hostProcess.ExecutablePath -ne $hostEntry.path -or $hostProcess.CommandLine -notlike "*127.0.0.1:$($hostEntry.port)*" -or $hostProcess.CommandLine.Replace('/','\') -notlike '*release-0.2.35-20261109\testing\*') { throw 'Test host identity changed' }
        $running = Get-Process -Id $hostEntry.pid
        $running.Kill(); $running.WaitForExit()
    }
}
$status = @(& git -C $workspace status --porcelain=v1 --untracked-files=all)
$oldFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$oldDirectories = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in @(Get-Content "$batch/ignored-before.json" -Raw | ConvertFrom-Json)) {
    [void]$oldFiles.Add($file)
    $parent = $file
    while ($parent.Contains('/')) {
        $parent = $parent.Substring(0, $parent.LastIndexOf('/'))
        [void]$oldDirectories.Add($parent)
    }
}
$targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
# Only ignored files created by this task outside its release batch are eligible.
foreach ($file in @(& git -C $workspace -c core.quotepath=false ls-files --others --ignored --exclude-standard -- build rewrite/reports)) {
    if ($oldFiles.Contains($file) -or $file.StartsWith($relativeBatch + '/')) { continue }
    if ($file -notmatch '^build/(android-startup-20261024|army-capacity-20261026|custom-audio-20261109|diplomacy-20261028|hq-objectives-20261025|map-colors-20261025|mountain-installation-20261101|movement-morale-20261030|player-ai-20261026|r-feedback-20261023|startup-assets-20261026|unit-layers-20261102)/' -and $file -notmatch '^rewrite/reports/(commander-dialogues-20261021|consumables-20261021|defeated-faction-test|equipment-fix-20261021|item-extensions-20261021|jurisdiction-extension-20261020|save-check|scenario-control-20261021|scenario-events-20261021|scenario-factions-20261021|scenario-recurrence-diplomacy-20261021|skill-descriptions-20261023)/') { continue }
    $target = $file
    $parent = $target.Substring(0, $target.LastIndexOf('/'))
    while ($parent -notin 'build','rewrite/reports' -and !$oldDirectories.Contains($parent)) {
        $target = $parent
        $parent = $target.Substring(0, $target.LastIndexOf('/'))
    }
    [void]$targets.Add($target)
}
foreach ($name in 'android-artifacts','web-artifacts','check-artifacts','mod-artifacts','apk-arm64','apk-x64','android','testing','author-build','author-qa','final-author-qa','candidate-before-ui','ignored-before.json','test-hosts.json','processes-before.json') {
    [void]$targets.Add("$relativeBatch/$name")
}
[void]$targets.Add("$relativeBatch/author/EW4-Mod-Author-0.2.35-win-x64.zip")
[void]$targets.Add("$relativeBatch/author/EW4-Mod-Author-0.2.35-win-x64.zip.sha256")
foreach ($file in Get-ChildItem -LiteralPath $batch -File -Filter '*.log') { [void]$targets.Add("$relativeBatch/$($file.Name)") }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $workspace 'tools/__pycache__') -File -ErrorAction SilentlyContinue | Where-Object Name -Match '^(package_release|verify_author_kit)\..*\.pyc$') { [void]$targets.Add("tools/__pycache__/$($file.Name)") }
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine })
$batches = @(Get-ChildItem (Join-Path $workspace 'build') -Directory -Filter 'release-*' |
    Where-Object Name -Match '^release-\d+\.\d+\.\d+-\d{8}$' |
    Sort-Object { [version]($_.Name -replace '^release-','' -replace '-\d{8}$','') } -Descending)
$retained = @($batches | Select-Object -First 3)
foreach ($older in $batches | Select-Object -Skip 3) {
    [void]$targets.Add("build/$($older.Name)/assets")
    $runtime = Join-Path $older.FullName 'web'
    $players = @(Get-ChildItem -LiteralPath $runtime -Directory -ErrorAction SilentlyContinue | Where-Object Name -In 'saves','profile','profiles','mods','content-mods')
    $consumers = @($processes | Where-Object { $_.CommandLine.Replace('/','\').Contains($runtime) })
    if (!$players.Count -and !$consumers.Count) { [void]$targets.Add("build/$($older.Name)/web") }
}
$minimal = @()
foreach ($relative in @($targets | Sort-Object Length)) {
    if (!@($minimal | Where-Object { $relative.StartsWith($_ + '/') }).Count) { $minimal += $relative }
}
$items = @(foreach ($relative in $minimal) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $workspace $relative))
    if (!$absolute.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace' }
    if (!(Test-Path -LiteralPath $absolute)) { continue }
    if (@($protected | Where-Object { $_.path -eq $absolute -or $_.path.StartsWith($absolute + '\',[StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'Player file in cleanup target' }
    $ancestor = $absolute
    while ($ancestor -ne $workspace) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Linked cleanup ancestor' }
        $ancestor = Split-Path $ancestor -Parent
    }
    if (@(& git -C $workspace ls-files -- $relative).Count) { throw "Tracked cleanup target: $relative" }
    $children = @(Get-Item -LiteralPath $absolute -Force)
    if ($children[0].PSIsContainer) { $children += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if (@($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }).Count) { throw 'Linked cleanup child' }
    foreach ($process in $processes) {
        if ($process.CommandLine.Replace('/','\').Contains($absolute + '\') -or $process.ExecutablePath -eq $absolute) { throw "Active cleanup consumer: $relative" }
    }
    $files = @($children | Where-Object { !$_.PSIsContainer })
    [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long]($files | Measure-Object Length -Sum).Sum}
})
$plan = [ordered]@{targets=$items.Count;files=($items | Measure-Object files -Sum).Sum;bytes=($items | Measure-Object bytes -Sum).Sum;retained=@($retained.Name);items=$items}
$plan | ConvertTo-Json -Depth 5 | Set-Content "$batch/cleanup-plan.json" -Encoding utf8
if (!$Execute) { [pscustomobject]$plan | Select-Object targets,files,bytes,retained | ConvertTo-Json -Compress; exit }
foreach ($item in $items) { Remove-Item -LiteralPath (Join-Path $workspace $item.path) -Recurse -Force }
$remaining = @($items | Where-Object { Test-Path -LiteralPath (Join-Path $workspace $_.path) }).Count
Check-Players
$gitPreserved = ($status -join "`n") -ceq (@(& git -C $workspace status --porcelain=v1 --untracked-files=all) -join "`n")
$hashErrors = 0; $packages = 0
foreach ($release in $retained) {
    $manifest = Get-Content (Join-Path $release.FullName 'assets/release-manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        $packages++
        if ((Get-FileHash -LiteralPath (Join-Path $release.FullName ('assets/' + $file.name))).Hash.ToLowerInvariant() -ne $file.sha256) { $hashErrors++ }
    }
}
$receipt = [ordered]@{targets=$plan.targets;files=$plan.files;bytes=$plan.bytes;remaining=$remaining;protectedFiles=$protected.Count;
    changedPlayerFiles=0;gitStatusUnchanged=$gitPreserved;retained=@($retained.Name);packages=$packages;
    packageHashErrors=$hashErrors;errors=($remaining + $hashErrors + [int](!$gitPreserved))}
$receipt | ConvertTo-Json -Depth 5 | Set-Content "$batch/cleanup.json" -Encoding utf8
if ($receipt.errors) { throw 'Cleanup verification failed' }
[pscustomobject]$receipt | ConvertTo-Json -Compress
