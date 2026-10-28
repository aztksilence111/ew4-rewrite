param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$releaseWorkspace = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$releaseBatch = 'build/release-0.2.25-20261028'
$targets = @("$releaseBatch/testing", 'build/diplomacy-20261028', 'build/startup-assets-20261026/checks')
$cutoff = [datetime]'2026-10-28T21:45:00'
foreach ($project in Get-ChildItem (Join-Path $releaseWorkspace 'rewrite') -Directory -Filter 'Ew4.*') {
    foreach ($output in 'bin','obj') { $targets += "rewrite/$($project.Name)/$output" }
}
foreach ($report in Get-ChildItem (Join-Path $releaseWorkspace 'rewrite/reports') -Force) {
    if ($report.LastWriteTime -gt $cutoff) { $targets += 'rewrite/reports/' + $report.Name }
}
foreach ($check in Get-ChildItem (Join-Path $releaseWorkspace 'build/android-startup-20261024') -Directory -Filter 'check-*') {
    $targets += 'build/android-startup-20261024/' + $check.Name
}
$batches = @(Get-ChildItem (Join-Path $releaseWorkspace 'build') -Directory -Filter 'release-*' |
    Where-Object { $_.Name -match '^release-\d+\.\d+\.\d+-\d{8}$' } |
    Sort-Object { [version]($_.Name -replace '^release-','' -replace '-\d{8}$','') } -Descending)
$retained = @($batches | Select-Object -First 3 -ExpandProperty Name)
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.Name -in @('dotnet.exe','Ew4.Web.exe','Ew4.Verify.exe','Ew4.ModChecks.exe','node.exe','python.exe') })
$protected = @()
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
    $items += [pscustomobject]@{ path=$relative; files=$files.Count; bytes=($files | Measure-Object Length -Sum).Sum }
}
if (-not $Execute) {
    [pscustomobject]@{targets=$items.Count;files=($items|Measure-Object files -Sum).Sum;bytes=($items|Measure-Object bytes -Sum).Sum;retained=$retained;protected=$protected} | ConvertTo-Json -Compress
    exit
}
foreach ($item in $items) { Remove-Item -LiteralPath (Join-Path $releaseWorkspace $item.path) -Recurse -Force }
$receipt = [pscustomobject]@{targets=$items.Count;files=($items|Measure-Object files -Sum).Sum;bytes=($items|Measure-Object bytes -Sum).Sum;
    remaining=@($items|Where-Object {Test-Path -LiteralPath (Join-Path $releaseWorkspace $_.path)}).Count;errors=0;retained=$retained;protected=$protected;items=$items}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseWorkspace "$releaseBatch/cleanup.json") -Encoding utf8
$receipt | Select-Object targets,files,bytes,remaining,errors,retained,protected | ConvertTo-Json -Compress
