param([string]$Root = (Get-Location).Path)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$evidencePath = Join-Path $rootPath 'build/turn-regression-followup-20261025'
$verification = Get-Content -LiteralPath (Join-Path $evidencePath 'verification.json') -Raw | ConvertFrom-Json
if ($verification.errors -ne 0) { throw 'Verify evidence first' }
$packagePath = [IO.Path]::GetFullPath((Join-Path $rootPath $verification.package.apk))
if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $verification.package.sha256) { throw 'Delivered APK drift' }
$receiptPath = Join-Path $evidencePath 'cleanup.json'
if (Test-Path -LiteralPath $receiptPath) {
    $prior = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($prior.stage -eq 'complete') {
        [long]$priorBytes = 0
        foreach ($entry in $prior.files) { $priorBytes += [long]$entry.bytes; if (Test-Path -LiteralPath $entry.path) { throw 'Cleaned output was recreated' } }
        $prior.bytes = $priorBytes
        $prior | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding utf8
        [ordered]@{count=$prior.count;bytes=$prior.bytes;errors=0} | ConvertTo-Json -Compress
        return
    }
}
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($rootPath) } | Select-Object ProcessId, CommandLine)
$gitStatus = @(git -C $rootPath status --short)
$targets = @(
    'rewrite/Ew4.Android/bin/Release/net10.0-android/android-arm64/org.ew4.reconstruction-Signed.apk',
    'rewrite/Ew4.Android/bin/Release/net10.0-android/android-arm64/org.ew4.reconstruction.apk',
    'rewrite/Ew4.Android/obj/Release/net10.0-android/android-arm64/android/bin/org.ew4.reconstruction.apk',
    'build/turn-regression-followup-20261025/package/assemblies.blob',
    'build/turn-regression-followup-20261025/package/superseded-projection-test.apk'
)
$files = @()
foreach ($relative in $targets) {
    $target = [IO.Path]::GetFullPath((Join-Path $rootPath $relative))
    if (-not $target.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $target -eq $packagePath) { throw 'Cleanup target escaped scope' }
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $resolved = (Resolve-Path -LiteralPath $target).Path
    if ($resolved -ne $target -or (Get-Item -LiteralPath $target).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected target resolution' }
    if ($processes | Where-Object { $_.CommandLine.Contains($target) }) { throw 'Target is in use' }
    $files += [ordered]@{path=$target;bytes=(Get-Item -LiteralPath $target).Length;sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$signed = $files | Where-Object { $_.path.EndsWith('org.ew4.reconstruction-Signed.apk') }
if ($signed -and $signed.sha256 -ne $verification.package.sha256) { throw 'Intermediate signed APK differs from delivery' }
[long]$totalBytes = 0
foreach ($entry in $files) { $totalBytes += [long]$entry['bytes'] }
$receipt = [ordered]@{stage='planned';files=$files;count=$files.Count;bytes=$totalBytes;preservedApk=$packagePath;preservedSha256=$verification.package.sha256;processes=$processes;gitStatusCount=$gitStatus.Count;errors=0}
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'cleanup.json') -Encoding utf8
foreach ($file in $files) { Remove-Item -LiteralPath $file.path -Force }
$receipt.stage='complete'
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'cleanup.json') -Encoding utf8
[ordered]@{count=$receipt.count;bytes=$receipt.bytes;errors=0} | ConvertTo-Json -Compress
