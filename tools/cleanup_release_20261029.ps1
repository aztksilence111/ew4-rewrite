param([switch]$Execute, [string]$ReleaseBatch = 'build/release-0.2.26-20261029', [string[]]$AdditionalTargets = @())
$ErrorActionPreference = 'Stop'
$releaseWorkspace = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$releaseBatch = $ReleaseBatch
$manifest = Get-Content (Join-Path $releaseWorkspace "$releaseBatch/assets/release-manifest.json") -Raw | ConvertFrom-Json
$cutoff = [DateTimeOffset]::Parse((git -C $releaseWorkspace show -s --format=%cI $manifest.source)).LocalDateTime
$batchCreated = (Get-Item -LiteralPath (Join-Path $releaseWorkspace $releaseBatch)).CreationTime
if ($batchCreated -lt $cutoff) { $cutoff = $batchCreated }
$targets = @("$releaseBatch/testing", "$releaseBatch/android", 'build/honor-startup-20261028')
$targets += $AdditionalTargets
foreach ($project in Get-ChildItem (Join-Path $releaseWorkspace 'rewrite') -Directory -Filter 'Ew4.*') {
    foreach ($output in 'bin','obj') { $targets += "rewrite/$($project.Name)/$output" }
}
foreach ($parent in @('build/save-export-checks','build/startup-assets-20261026/checks','build/android-startup-20261024',
        'build/diplomacy-20261028/mod-checks','rewrite/reports/defeated-faction-test','rewrite/reports/client-slot-test')) {
    foreach ($child in Get-ChildItem (Join-Path $releaseWorkspace $parent) -Force -ErrorAction SilentlyContinue) {
        if ($child.CreationTime -ge $cutoff) { $targets += "$parent/$($child.Name)" }
    }
}
foreach ($temporary in @('build/attack-bounds-check-20261029')) {
    $path = Join-Path $releaseWorkspace $temporary
    if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).CreationTime -ge $cutoff) { $targets += $temporary }
}
foreach ($report in Get-ChildItem (Join-Path $releaseWorkspace 'rewrite/reports') -File) {
    if ($report.CreationTime -ge $cutoff) { $targets += 'rewrite/reports/' + $report.Name }
}
$batches = @(Get-ChildItem (Join-Path $releaseWorkspace 'build') -Directory -Filter 'release-*' |
    Where-Object { $_.Name -match '^release-\d+\.\d+\.\d+-\d{8}$' } |
    Sort-Object { [version]($_.Name -replace '^release-','' -replace '-\d{8}$','') } -Descending)
$retained = @($batches | Select-Object -First 3 -ExpandProperty Name)
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and
    $_.Name -in @('dotnet.exe','Ew4.Web.exe','Ew4.Verify.exe','Ew4.ModChecks.exe','node.exe','python.exe','msedge.exe','chrome.exe') })
$protected = @()
$sharedFrontend = Join-Path $releaseWorkspace 'rewrite\Ew4.Web'
if ($processes | Where-Object { $_.CommandLine.Replace('/','\').Contains($sharedFrontend) }) {
    $protected += $sharedFrontend + '\wwwroot\compat', $sharedFrontend + '\wwwroot\build-info.json'
} else {
    $targets += 'rewrite/Ew4.Web/wwwroot/compat', 'rewrite/Ew4.Web/wwwroot/build-info.json'
}
foreach ($batch in $batches | Select-Object -Skip 3) {
    $targets += "build/$($batch.Name)/assets"
    $runtime = Join-Path $batch.FullName 'web'
    $active = @($processes | Where-Object { $_.CommandLine.Replace('/','\').Contains($runtime) })
    $playerData = @(Get-ChildItem -LiteralPath $runtime -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('saves','profile','profiles','content-mods') })
    if ($active.Count -or $playerData.Count) { $protected += $runtime }
    else { $targets += "build/$($batch.Name)/web" }
    foreach ($package in Get-ChildItem -LiteralPath $batch.FullName -File | Where-Object Extension -In '.apk','.zip') {
        $targets += "build/$($batch.Name)/$($package.Name)"
    }
}
$gitBefore = @(git -C $releaseWorkspace status --short)
$tracked = @(git -C $releaseWorkspace ls-files)
$items = @()
foreach ($relative in $targets | Sort-Object -Unique) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $releaseWorkspace $relative))
    if (-not $absolute.StartsWith($releaseWorkspace+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Outside workspace' }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolution mismatch: $relative" }
    if ($tracked | Where-Object { $_ -eq $relative -or $_.StartsWith($relative+'/') }) { throw "Tracked input: $relative" }
    if ($processes | Where-Object { $_.CommandLine.Replace('/','\').Contains($absolute) }) { throw "Active consumer: $relative" }
    $children = @(Get-Item -LiteralPath $absolute -Force)
    if ($children[0].PSIsContainer) { $children += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Linked target: $relative" }
    $files = @($children | Where-Object { -not $_.PSIsContainer })
    $items += [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
}
if (-not $Execute) {
    [pscustomobject]@{targets=$items.Count;files=($items|Measure-Object files -Sum).Sum;
        bytes=($items|Measure-Object bytes -Sum).Sum;retained=$retained;protected=$protected} | ConvertTo-Json -Compress
    exit
}
foreach ($item in $items) { Remove-Item -LiteralPath (Join-Path $releaseWorkspace $item.path) -Recurse -Force }
$receipt = [pscustomobject]@{targets=$items.Count;files=($items|Measure-Object files -Sum).Sum;bytes=($items|Measure-Object bytes -Sum).Sum;
    remaining=@($items|Where-Object {Test-Path -LiteralPath (Join-Path $releaseWorkspace $_.path)}).Count;
    errors=0;retained=$retained;protected=$protected;items=$items;
    gitStatusUnchanged=[bool](-not (Compare-Object $gitBefore @(git -C $releaseWorkspace status --short)))}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseWorkspace "$releaseBatch/cleanup.json") -Encoding utf8
$receipt | Select-Object targets,files,bytes,remaining,errors,retained,protected,gitStatusUnchanged | ConvertTo-Json -Compress
