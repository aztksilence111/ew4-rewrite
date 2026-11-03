param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$relativeBatch = 'build/release-0.2.31-20261102'
$batch = Join-Path $workspace $relativeBatch
$audit = Get-Content "$batch/remote-audit.json" -Raw | ConvertFrom-Json
$verification = Get-Content "$batch/verification.json" -Raw | ConvertFrom-Json
if ($audit.errors -or $verification.errors -or $verification.nativeLibraries -ne 10) { throw 'Release is not verified' }
$cutoff = (Get-Item -LiteralPath $batch).CreationTime
$processes = @(Get-CimInstance Win32_Process | Where-Object CommandLine)
$status = @(git -C $workspace status --porcelain=v1 -uall)
$baseline = @(Get-Content "$batch/player-data-before.json" -Raw | ConvertFrom-Json)
# ArmyCapacityChecks.Run owns this fixed test account and its autosave slots.
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'build/army-capacity-20261026/check-profile'))
$excluded = @($baseline | Where-Object { $_.path -in @("$fixtureRoot\account.json", "$fixtureRoot\saves\mode-2\slot-0.json", "$fixtureRoot\saves\mode-2\slot-0.json.bak") })
$protected = @($baseline | Where-Object { $_.path -notin $excluded.path })
$targets = @('build/advanced-units-20261102','build/commander-abilities-20261102','build/critical-audit-20261102',
    'build/mountain-installation-20261101','build/skill-interfaces-20261102','build/unit-layers-20261102',
    'build/army-capacity-20261026/check-profile')
foreach ($name in 'android-artifacts','web-artifacts','check-artifacts','mod-artifacts','apk-il','testing') { $targets += "$relativeBatch/$name" }
foreach ($file in Get-ChildItem -LiteralPath $batch -File -Filter '*.log') { $targets += "$relativeBatch/$($file.Name)" }
foreach ($name in 'test-host.pid','ability-host.pid','test-hosts.json','index-before.txt','source-paths.json') { $targets += "$relativeBatch/$name" }
foreach ($directory in Get-ChildItem (Join-Path $workspace 'build') -Directory -Filter 'save-compatibility-*') {
    if ($directory.CreationTime -ge $cutoff) { $targets += 'build/' + $directory.Name }
}
foreach ($parent in 'rewrite/reports/client-slot-test','rewrite/reports/mobile-display-pan-20261022',
    'build/save-export-checks','build/feedback-undo-fix-20261022/client','build/diplomacy-20261028',
    'rewrite/reports/commander-dialogues-20261021','rewrite/reports/legacy-skill-parameters-20261021',
    'rewrite/reports/skill-descriptions-20261023') {
    foreach ($child in Get-ChildItem (Join-Path $workspace $parent) -Force -ErrorAction SilentlyContinue) {
        if ($child.CreationTime -ge $cutoff) { $targets += "$parent/$($child.Name)" }
    }
}
foreach ($parent in 'rewrite/reports/item-extensions-20261021','rewrite/reports/jurisdiction-extension-20261020',
    'rewrite/reports/mod-import-20261019','rewrite/reports/save-check') {
    if ((Get-Item (Join-Path $workspace $parent) -ErrorAction SilentlyContinue).CreationTime -ge $cutoff) { $targets += $parent }
}
foreach ($file in Get-ChildItem (Join-Path $workspace 'rewrite/reports') -File -Force | Where-Object LastWriteTime -ge $cutoff) {
    $targets += 'rewrite/reports/' + $file.Name
}
foreach ($parent in 'rewrite/reports/common-skills-20261021','build/scenario-armies-client-20261031/saves/mode-2') {
    foreach ($file in Get-ChildItem (Join-Path $workspace $parent) -File -ErrorAction SilentlyContinue | Where-Object LastWriteTime -ge $cutoff) {
        $targets += "$parent/$($file.Name)"
    }
}
$batches = @(Get-ChildItem (Join-Path $workspace 'build') -Directory -Filter 'release-*' |
    Where-Object Name -Match '^release-\d+\.\d+\.\d+-\d{8}$' |
    Sort-Object { [version]($_.Name -replace '^release-','' -replace '-\d{8}$','') } -Descending)
$retained = @($batches | Select-Object -First 3)
foreach ($older in $batches | Select-Object -Skip 3) {
    $targets += "build/$($older.Name)/assets"
    $runtime = Join-Path $older.FullName 'web'
    $consumers = @($processes | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($runtime + '\', [StringComparison]::OrdinalIgnoreCase) })
    $players = @(Get-ChildItem -LiteralPath $runtime -Directory -ErrorAction SilentlyContinue | Where-Object Name -In 'saves','profile','profiles','mods','content-mods')
    if (!$consumers.Count -and !$players.Count) { $targets += "build/$($older.Name)/web" }
}
$skipped = @()
$items = @(foreach ($relative in $targets | Sort-Object -Unique) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $workspace $relative))
    if (!$absolute.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $relative" }
    if (!(Test-Path -LiteralPath $absolute)) { continue }
    if (@($protected | Where-Object { $_.path -eq $absolute -or $_.path.StartsWith($absolute + '\', [StringComparison]::OrdinalIgnoreCase) }).Count) {
        $skipped += $relative; continue
    }
    $ancestor = $absolute
    while ($ancestor -ne $workspace) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Linked ancestor: $relative" }
        $ancestor = Split-Path $ancestor -Parent
    }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute -or @(git -C $workspace ls-files -- $relative).Count) { throw "Unsafe target: $relative" }
    $children = @(Get-Item -LiteralPath $absolute -Force)
    if ($children[0].PSIsContainer) { $children += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if (@($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }).Count) { throw "Linked child: $relative" }
    foreach ($process in $processes | Where-Object { $_.Name -in 'Ew4.Web.exe','dotnet.exe','node.exe','msedge.exe','chrome.exe' }) {
        if ($process.CommandLine.Replace('/','\').Contains($absolute + '\') -or $process.ExecutablePath -eq $absolute) { throw "Active target: $relative" }
    }
    $files = @($children | Where-Object { !$_.PSIsContainer })
    [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
})
$plan = [ordered]@{targets=$items.Count;files=($items | Measure-Object files -Sum).Sum;bytes=($items | Measure-Object bytes -Sum).Sum;
    retained=@($retained.Name);skippedProtected=$skipped;items=$items}
if (!$Execute) { $plan | ConvertTo-Json -Depth 5 | Set-Content "$batch/cleanup-plan.json" -Encoding utf8; [pscustomobject]$plan | Select-Object targets,files,bytes,retained,skippedProtected | ConvertTo-Json -Compress; exit }
foreach ($entry in $protected) {
    if (!(Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Protected data changed: $($entry.path)" }
}
$excluded | ConvertTo-Json -Depth 3 | Set-Content "$batch/test-fixture-baseline-exclusions.json" -Encoding utf8
foreach ($item in $items) {
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try { Remove-Item -LiteralPath (Join-Path $workspace $item.path) -Recurse -Force; break }
        catch { if ($attempt -eq 4) { throw }; Start-Sleep -Milliseconds 500 }
    }
}
$remaining = @($items | Where-Object { Test-Path -LiteralPath (Join-Path $workspace $_.path) }).Count
$changed = @($protected | Where-Object { !(Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256 }).Count
$gitPreserved = !(Compare-Object $status @(git -C $workspace status --porcelain=v1 -uall))
$hashErrors = 0; $packages = 0
foreach ($release in $retained) {
    $manifest = Get-Content (Join-Path $release.FullName 'assets/release-manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        $packages++
        if ((Get-FileHash -LiteralPath (Join-Path $release.FullName ('assets/' + $file.name)) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { $hashErrors++ }
    }
}
$services = foreach ($port in 56173,56174) { [pscustomobject]@{port=$port;status=(Invoke-WebRequest "http://127.0.0.1:$port" -UseBasicParsing).StatusCode} }
$receipt = [ordered]@{targets=$plan.targets;files=$plan.files;bytes=$plan.bytes;remaining=$remaining;protectedFiles=$protected.Count;
    changedPlayerFiles=$changed;excludedTestFiles=$excluded.Count;gitStatusUnchanged=$gitPreserved;retained=@($retained.Name);packages=$packages;
    packageHashErrors=$hashErrors;services=@($services);skippedProtected=$skipped;items=$items;errors=($remaining + $changed + $hashErrors + [int](!$gitPreserved))}
$receipt | ConvertTo-Json -Depth 6 | Set-Content "$batch/cleanup.json" -Encoding utf8
if ($receipt.errors) { throw 'Post-cleanup verification failed' }
[pscustomobject]$receipt | Select-Object targets,files,bytes,remaining,protectedFiles,changedPlayerFiles,excludedTestFiles,gitStatusUnchanged,retained,packages,errors | ConvertTo-Json -Compress
