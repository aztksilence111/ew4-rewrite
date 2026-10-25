# One-off sweep of verified test outputs after release 0.2.20.
param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$buildPath = Join-Path $rootPath build
$receiptPath = Join-Path $buildPath 'maintenance-20261025'
$retained = @(Get-ChildItem -LiteralPath $buildPath -Directory | Where-Object {
    $_.Name -match '^release-(\d+\.\d+\.\d+)-\d{8}$' -and (Test-Path (Join-Path $_.FullName 'assets/release-manifest.json'))
} | Sort-Object { [version]([regex]::Match($_.Name,'\d+\.\d+\.\d+').Value) } -Descending | Select-Object -First 3 -ExpandProperty Name)
if ($retained.Count -ne 3) { throw 'Expected three verified formal release batches' }
$latestAudit = Get-Content (Join-Path $buildPath "$($retained[0])/remote-audit.json") -Raw | ConvertFrom-Json
if ($latestAudit.errors -ne 0) { throw 'Latest formal release audit required' }
$protected = [Collections.Generic.List[string]]::new()
$protected.Add($receiptPath)
foreach ($batch in $retained) {
    foreach ($name in @('assets','web','release-remote.json','remote-audit.json','cleanup.json')) {
        $protected.Add((Join-Path $buildPath "$batch/$name"))
    }
}
foreach ($relative in @('build/release-0.2.13-20261022/web/saves','build/release-0.2.16-20261023/web/saves','build/release-20261020/web/saves','rewrite/local/web','rewrite/local/font-sources-20261020')) {
    $protected.Add((Join-Path $rootPath $relative))
}
function Inside([string]$child,[string]$parent) {
    return $child.Equals($parent,[StringComparison]::OrdinalIgnoreCase) -or $child.StartsWith($parent+'\',[StringComparison]::OrdinalIgnoreCase)
}
function SafePath([string]$target) {
    $absolute = [IO.Path]::GetFullPath($target)
    if (-not (Inside $absolute $rootPath) -or $absolute -eq $rootPath) { throw "Out of scope: $absolute" }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolution mismatch: $absolute" }
    if ((Get-Item -LiteralPath $absolute -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Reparse point: $absolute" }
    return $absolute
}
if (-not $Execute) {
$tracked = @(git -C $rootPath ls-files | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $rootPath $_)) })
$statusBefore = @(git -C $rootPath status --short)
$testProcesses = @(Get-CimInstance Win32_Process | Where-Object {
    $line = if ($_.CommandLine) { $_.CommandLine.Replace('/','\') } else { '' }
    ($_.Name -eq 'dotnet.exe' -and $line.Contains($buildPath+'\') -and $line.Contains('Ew4.Web.dll')) -or
    ($_.Name -eq 'Ew4.Web.exe' -and $line.Contains($rootPath+'\rewrite\local\eastasia-validation\')) -or
    ($_.Name -eq 'python.exe' -and $line.Contains($buildPath+'\map-view-design-20261025')) -or
    ($_.Name -eq 'node.exe' -and $line.Contains($buildPath+'\pause-objectives-style-20261025\server.cjs'))
} | Select-Object ProcessId,Name,CommandLine)
$roots = @($buildPath,(Join-Path $rootPath 'rewrite/reports'),(Join-Path $rootPath 'rewrite/local'))
foreach ($project in Get-ChildItem (Join-Path $rootPath rewrite) -Directory -Filter 'Ew4.*') {
    foreach ($name in @('bin','obj')) {
        $candidate = Join-Path $project.FullName $name
        if (Test-Path -LiteralPath $candidate) { $roots += $candidate }
    }
}
$targets = [Collections.Generic.List[object]]::new()
function Walk([string]$path) {
    $absolute = SafePath $path
    if ($protected | Where-Object { Inside $absolute $_ }) { return }
    if ($tracked | Where-Object { $_ -eq $absolute }) { return }
    $item = Get-Item -LiteralPath $absolute -Force
    $hasProtected = @($protected | Where-Object { Inside $_ $absolute }).Count -gt 0
    $hasTracked = @($tracked | Where-Object { Inside $_ $absolute }).Count -gt 0
    if ($item.PSIsContainer -and ($hasProtected -or $hasTracked)) {
        foreach ($child in Get-ChildItem -LiteralPath $absolute -Force) { Walk $child.FullName }
        return
    }
    if ($item.PSIsContainer) {
        $children = @(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
        if ($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Nested reparse point: $absolute" }
        $files = @($children | Where-Object { -not $_.PSIsContainer })
        $bytes = [long](($files | Measure-Object Length -Sum).Sum)
    } else { $files=@($item); $bytes=$item.Length }
    $targets.Add([ordered]@{path=$absolute;directory=$item.PSIsContainer;files=$files.Count;bytes=$bytes})
}
foreach ($candidate in $roots) { if (Test-Path -LiteralPath $candidate) { Walk $candidate } }
$preservedFiles = [Collections.Generic.List[object]]::new()
foreach ($path in $protected | Where-Object { Test-Path -LiteralPath $_ }) {
    $item = Get-Item -LiteralPath $path
    $files = if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $path -File -Recurse) } else { @($item) }
    foreach ($file in $files) { $preservedFiles.Add([ordered]@{path=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}) }
}
$plan = [ordered]@{stage='planned';targets=@($targets.ToArray());count=$targets.Count;files=($targets | ForEach-Object { $_.files } | Measure-Object -Sum).Sum;bytes=($targets | ForEach-Object { $_.bytes } | Measure-Object -Sum).Sum;stoppedTestProcesses=$testProcesses;protectedRoots=$protected.ToArray();preservedFiles=@($preservedFiles.ToArray());gitStatusBefore=$statusBefore;errors=@()}
New-Item -ItemType Directory -Path $receiptPath -Force | Out-Null
$plan | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $receiptPath 'test-artifact-cleanup.json') -Encoding utf8
if (-not $Execute) {
    [ordered]@{stage='planned';targets=$plan.count;files=$plan.files;bytes=$plan.bytes;testProcesses=$testProcesses.Count;preservedFiles=$preservedFiles.Count} | ConvertTo-Json -Compress
    return
}
}
$plan = Get-Content (Join-Path $receiptPath 'test-artifact-cleanup.json') -Raw | ConvertFrom-Json
if ($plan.stage -ne 'planned') { throw 'Generate a fresh cleanup plan before execution' }
$targets = $plan.targets
$testProcesses = $plan.stoppedTestProcesses
$preservedFiles = $plan.preservedFiles
foreach ($file in $preservedFiles) {
    if (-not (Test-Path -LiteralPath $file.path) -or (Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Preserved input changed since planning: $($file.path)" }
}
foreach ($entry in $testProcesses) {
    $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($entry.ProcessId)"
    if ($current -and $current.CommandLine -eq $entry.CommandLine) { Stop-Process -Id $entry.ProcessId }
}
$remaining = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.Name -in @('dotnet.exe','Ew4.Web.exe','node.exe','python.exe','chrome.exe','msedge.exe') } | Select-Object ProcessId,CommandLine)
foreach ($target in $targets) {
    $absolute = SafePath $target.path
    if ($remaining | Where-Object { $_.CommandLine.Replace('/','\').Contains($absolute) }) { throw "Target still in use: $absolute" }
    try { Remove-Item -LiteralPath $absolute -Recurse -Force } catch { $plan.errors += @{path=$absolute;message=$_.Exception.Message} }
}
foreach ($file in $preservedFiles) {
    if (-not (Test-Path -LiteralPath $file.path) -or (Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Preserved file changed: $($file.path)" }
}
$plan.stage = if ($plan.errors.Count) { 'partial' } else { 'complete' }
$plan | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $receiptPath 'test-artifact-cleanup.json') -Encoding utf8
[ordered]@{stage=$plan.stage;targets=$plan.count;files=$plan.files;bytes=$plan.bytes;errors=$plan.errors.Count;preservedFiles=$preservedFiles.Count;stoppedTestProcesses=$testProcesses.Count} | ConvertTo-Json -Compress
if ($plan.errors.Count) { exit 1 }
