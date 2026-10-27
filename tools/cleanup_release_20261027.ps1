param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$releaseWorkspace = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$targets = @('build/campaign-label-check-20261027', 'build/army-overflow-20261027/preview/host')
foreach ($project in Get-ChildItem (Join-Path $releaseWorkspace 'rewrite') -Directory -Filter 'Ew4.*') {
    foreach ($output in 'bin','obj') { $targets += "rewrite/$($project.Name)/$output" }
}
foreach ($report in Get-ChildItem (Join-Path $releaseWorkspace 'rewrite/reports') -Force) {
    if ($report.LastWriteTime -gt [datetime]'2026-10-27T23:25:00') { $targets += 'rewrite/reports/' + $report.Name }
}
foreach ($batch in Get-ChildItem (Join-Path $releaseWorkspace 'build') -Directory -Filter 'release-*') {
    if ($batch.Name -in @('release-0.2.24-20261027','release-0.2.23-20261027','release-0.2.22-20261027')) { continue }
    foreach ($part in 'assets','web') { $targets += "build/$($batch.Name)/$part" }
    foreach ($package in Get-ChildItem -LiteralPath $batch.FullName -File | Where-Object Extension -In '.apk','.zip') {
        $targets += "build/$($batch.Name)/$($package.Name)"
    }
}
$tracked = @(git -C $releaseWorkspace ls-files)
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.Name -in @('dotnet.exe','Ew4.Web.exe','node.exe','python.exe') })
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
if (-not $Execute) { $items | Format-Table -AutoSize; exit }
foreach ($item in $items) { Remove-Item -LiteralPath (Join-Path $releaseWorkspace $item.path) -Recurse -Force }
$receipt = [pscustomobject]@{ targets=$items.Count; files=($items | Measure-Object files -Sum).Sum; bytes=($items | Measure-Object bytes -Sum).Sum;
    remaining=@($items | Where-Object { Test-Path -LiteralPath (Join-Path $releaseWorkspace $_.path) }).Count; errors=0; items=$items }
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseWorkspace 'build/release-0.2.24-20261027/cleanup.json') -Encoding utf8
$receipt | Select-Object targets,files,bytes,remaining,errors | ConvertTo-Json -Compress
