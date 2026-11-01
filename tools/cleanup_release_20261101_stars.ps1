param([switch]$Execute, [switch]$Resume)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$batch = Join-Path $rootPath 'build/release-0.2.30-20261101'
$manifest = Get-Content (Join-Path $batch 'assets/release-manifest.json') -Raw | ConvertFrom-Json
$verification = Get-Content (Join-Path $batch 'verification.json') -Raw | ConvertFrom-Json
$audit = Get-Content (Join-Path $batch 'remote-audit.json') -Raw | ConvertFrom-Json
if ($verification.errors -or $verification.nativeLibraries -ne 10 -or $audit.errors -or $audit.source -ne $manifest.source) { throw 'Release verification incomplete' }
$cutoff = (Get-Item -LiteralPath $batch).CreationTime
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine })
$testHost = @($processes | Where-Object { $_.ExecutablePath -eq (Join-Path $batch 'web/Ew4.Web.exe') -and $_.CommandLine.Contains('57943') })
if ($testHost.Count -gt 1 -or ($testHost.Count -eq 0 -and -not $Resume)) { throw 'Unexpected package test host' }
if ($testHost.Count -eq 1 -and -not $testHost[0].CommandLine.Contains('release-0.2.30-20261101/testing/browser-profile')) { throw 'Unexpected package test profile' }
$oldPreview = Join-Path $rootPath 'build/r-campaign-shop-stars-preview-20261101'
$oldHost = @($processes | Where-Object { $_.ExecutablePath -eq (Join-Path $oldPreview 'Ew4.Web.exe') })
if ($oldHost.Count -gt 1) { throw 'Unexpected old preview consumers' }
if (Test-Path (Join-Path $oldPreview 'saves/account.json')) { throw 'Old preview now contains player data; preserve it' }
$stoppedIds = @($testHost | ForEach-Object ProcessId) + @($oldHost | ForEach-Object ProcessId)
$targets = @('build/campaign-shop-stars-20261101', 'build/r-campaign-shop-stars-preview-20261101')
foreach ($name in 'testing','android','android-arm64','compile','android-compile','android-arm64-compile','verify-compile') {
    $targets += 'build/release-0.2.30-20261101/' + $name
}
foreach ($project in 'Core','Legacy','Persistence','Client','Mods','ModChecks') {
    foreach ($name in 'bin','obj') { $targets += "rewrite/Ew4.$project/$name" }
}
foreach ($parent in 'rewrite/reports/client-slot-test','rewrite/reports/mobile-display-pan-20261022','build/save-export-checks') {
    foreach ($child in Get-ChildItem (Join-Path $rootPath $parent) -Force -ErrorAction SilentlyContinue) {
        if ($child.CreationTime -ge $cutoff) { $targets += "$parent/$($child.Name)" }
    }
}
if ((Get-Item (Join-Path $rootPath 'rewrite/reports/client_host.json') -ErrorAction SilentlyContinue).LastWriteTime -ge $cutoff) {
    $targets += 'rewrite/reports/client_host.json'
}
$batches = @(Get-ChildItem (Join-Path $rootPath 'build') -Directory -Filter 'release-*' |
    Where-Object Name -Match '^release-\d+\.\d+\.\d+-\d{8}$' |
    Sort-Object { [version]($_.Name -replace '^release-','' -replace '-\d{8}$','') } -Descending)
$retained = @($batches | Select-Object -First 3)
foreach ($older in $batches | Select-Object -Skip 3) {
    $targets += 'build/' + $older.Name + '/assets'
    $runtime = Join-Path $older.FullName 'web'
    $consumers = @($processes | Where-Object { $_.CommandLine.Replace('/','\').Contains($runtime) })
    $playerDirectories = @(Get-ChildItem $runtime -Directory -ErrorAction SilentlyContinue | Where-Object Name -In 'saves','profile','profiles','content-mods','mods')
    if (-not $consumers.Count -and -not $playerDirectories.Count) { $targets += 'build/' + $older.Name + '/web' }
}
$statusBefore = @(git -C $rootPath status --short)
$items = foreach ($relative in $targets | Sort-Object -Unique) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $rootPath $relative))
    if (-not $absolute.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Target outside workspace' }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Target resolution mismatch: $relative" }
    if (@(git -C $rootPath ls-files -- $relative).Count) { throw "Tracked target: $relative" }
    $children = @(Get-Item -LiteralPath $absolute -Force)
    if ($children[0].PSIsContainer) { $children += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Linked target: $relative" }
    foreach ($consumer in $processes | Where-Object { $_.ProcessId -notin $stoppedIds }) {
        if ($consumer.CommandLine.Replace('/','\').Contains($absolute + '\')) { throw "Active target: $relative" }
    }
    $files = @($children | Where-Object { -not $_.PSIsContainer })
    [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
}
if (-not $Execute) {
    [ordered]@{targets=@($items).Count;files=($items | Measure-Object files -Sum).Sum;bytes=($items | Measure-Object bytes -Sum).Sum;
        retained=@($retained.Name);stopProcesses=$stoppedIds} | ConvertTo-Json -Compress
    exit
}
if ($Resume) {
    $protected = @(Get-Content (Join-Path $batch 'player-data-before.json') -Raw | ConvertFrom-Json)
} else {
    $protected = @(Get-Content (Join-Path $rootPath 'build/release-0.2.29-20261101/player-data-before.json') -Raw | ConvertFrom-Json)
    $protected = @($protected | Where-Object { Test-Path -LiteralPath $_.path } | ForEach-Object {
        [pscustomobject]@{path=$_.path;sha256=(Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash}
    })
    $protected | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $batch 'player-data-before.json') -Encoding utf8
    [ordered]@{targets=@($items).Count;files=($items | Measure-Object files -Sum).Sum;bytes=($items | Measure-Object bytes -Sum).Sum} |
        ConvertTo-Json | Set-Content (Join-Path $batch 'cleanup-plan.json') -Encoding utf8
}
$plan = Get-Content (Join-Path $batch 'cleanup-plan.json') -Raw | ConvertFrom-Json
foreach ($id in $stoppedIds) { Stop-Process -Id $id; Wait-Process -Id $id -ErrorAction SilentlyContinue }
foreach ($item in $items) {
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try { Remove-Item -LiteralPath (Join-Path $rootPath $item.path) -Recurse -Force; break }
        catch { if ($attempt -eq 4) { throw }; Start-Sleep -Milliseconds 500 }
    }
}
$remaining = @($items | Where-Object { Test-Path -LiteralPath (Join-Path $rootPath $_.path) }).Count
$changed = @($protected | Where-Object { -not (Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.sha256 }).Count
$gitPreserved = -not [bool](Compare-Object $statusBefore @(git -C $rootPath status --short))
$receipt = [ordered]@{targets=$plan.targets;files=$plan.files;bytes=$plan.bytes;
    remaining=$remaining;errors=0;retained=@($retained.Name);protectedFiles=$protected.Count;changedPlayerFiles=$changed;gitStatusUnchanged=$gitPreserved;items=@($items)}
$receipt | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $batch 'cleanup.json') -Encoding utf8
if ($remaining -or $changed -or -not $gitPreserved) { throw 'Post-cleanup integrity failure' }
$packageErrors = 0; $packages = 0
foreach ($release in $retained) {
    $saved = Get-Content (Join-Path $release.FullName 'assets/release-manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $saved.files) {
        $packages++
        $filePath = Join-Path $release.FullName ('assets/' + $file.name)
        if ((Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { $packageErrors++ }
    }
}
$services = foreach ($port in 56167,56168,56169,56171,56172,56173,56174,5184) {
    [pscustomobject]@{port=$port;status=(Invoke-WebRequest "http://127.0.0.1:$port" -UseBasicParsing).StatusCode}
}
$post = [ordered]@{protectedFiles=$protected.Count;changedPlayerFiles=$changed;retained=@($retained.Name);packages=$packages;
    packageHashErrors=$packageErrors;services=@($services);temporaryTargetsRemaining=$remaining;errors=$packageErrors}
$post | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $batch 'post-cleanup-verification.json') -Encoding utf8
if ($packageErrors) { throw 'Retained package hash mismatch' }
[pscustomobject]$receipt | Select-Object targets,files,bytes,remaining,errors,retained,protectedFiles,changedPlayerFiles,gitStatusUnchanged | ConvertTo-Json -Compress
