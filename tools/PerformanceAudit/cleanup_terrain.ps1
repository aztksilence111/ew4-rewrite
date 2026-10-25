$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = Join-Path $repo 'build/terrain-visual-fix-20261025'
$verified = Get-Content -LiteralPath (Join-Path $evidence 'package/apk-verification.json') -Raw | ConvertFrom-Json
$delivery = Join-Path $repo $verified.apk
if ($verified.errors -ne 0 -or (Get-FileHash -LiteralPath $delivery -Algorithm SHA256).Hash.ToLowerInvariant() -ne $verified.sha256) { throw 'Unverified delivery' }
$targets = @(
    'rewrite/Ew4.Android/bin/Release/net10.0-android/android-arm64/org.ew4.reconstruction-Signed.apk',
    'rewrite/Ew4.Android/bin/Release/net10.0-android/android-arm64/org.ew4.reconstruction.apk',
    'rewrite/Ew4.Android/obj/Release/net10.0-android/android-arm64/android/bin/org.ew4.reconstruction.apk',
    'build/terrain-visual-fix-20261025/actual70/profile'
)
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID })
$gitState = @(git -C $repo status --short)
$rows = @()
foreach ($relative in $targets) {
    $candidate = Join-Path $repo $relative
    if (-not (Test-Path -LiteralPath $candidate)) { continue }
    $resolved = (Resolve-Path -LiteralPath $candidate).Path
    if (-not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Target escapes repository' }
    if (@(git -C $repo ls-files -- $relative).Count) { throw "Tracked target: $relative" }
    if ($processes | Where-Object { $_.CommandLine -and $_.CommandLine.Replace([char]47,[char]92).Contains($resolved,[StringComparison]::OrdinalIgnoreCase) }) { throw "Active target: $resolved" }
    $item = Get-Item -LiteralPath $resolved
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse target requires inspection' }
    $files = if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $resolved -Recurse -File -Force) } else { @($item) }
    $rows += [pscustomobject]@{path=$resolved; files=$files.Count; bytes=[long](($files | Measure-Object Length -Sum).Sum); sha256=if($item.PSIsContainer){$null}else{(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()}}
}
if (($rows | Where-Object { $_.path -like '*-Signed.apk' }).sha256 -ne $verified.sha256) { throw 'Signed output differs from delivery' }
$unsigned = @($rows | Where-Object { $_.path -like '*reconstruction.apk' })
if ($unsigned.Count -eq 2 -and $unsigned[0].sha256 -ne $unsigned[1].sha256) { throw 'Unsigned copies differ' }
$receipt = [ordered]@{errors=0; state='checked'; delivery=$delivery; sha256=$verified.sha256; dirtyPaths=$gitState.Count; targets=$rows}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'cleanup.json') -Encoding utf8
foreach ($row in $rows) { Remove-Item -LiteralPath $row.path -Recurse -Force }
$receipt.state = 'complete'
$receipt.removedBytes = [long](($rows | Measure-Object bytes -Sum).Sum)
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'cleanup.json') -Encoding utf8
[pscustomobject]@{errors=0; targets=$rows.Count; removedBytes=$receipt.removedBytes; sha256=$verified.sha256} | ConvertTo-Json -Compress
