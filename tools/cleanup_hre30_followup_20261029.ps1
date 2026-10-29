$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $root 'build/hre30-followup-20261029'
$result = Get-Content -LiteralPath (Join-Path $scratch 'full-round/results.json') -Raw | ConvertFrom-Json
if (-not $result.complete -or $result.steps -ne 15590 -or $result.units -ne 14524) { throw 'Full-round verification missing' }
if ($result.hash -ne 'f845d067f70769c8407b075cef6a864ec6864567b98212023d1db5483c1a57ae') { throw 'World replay differs' }
if (-not (Test-Path -LiteralPath (Join-Path $root 'reverse/HRE_AI_FOLLOWUP_20261029.md'))) { throw 'Durable report missing' }
$manifest = Get-Content -LiteralPath (Join-Path $scratch 'cleanup-targets.json') -Raw | ConvertFrom-Json
$targets = @($manifest.directories) + @($manifest.files.path) + @('build/hre30-followup-20261029')
$processes = @(Get-CimInstance Win32_Process)
$gitBefore = @(git -C $root status --short)
$rows = @()
$selected = @()
$preserved = @()
# Verify every target and the captured shared-file hashes before removing anything.
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Unexpected real path: $absolute" }
    $items = @((Get-Item -LiteralPath $absolute -Force))
    if ($items[0].PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked target: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').IndexOf($absolute, [StringComparison]::OrdinalIgnoreCase) -ge 0 }) {
        throw "Active consumer: $absolute"
    }
    $expected = $manifest.files | Where-Object path -eq $relative
    if ($expected -and (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash -ne $expected.sha256) {
        $preserved += $relative
        continue
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{ path = $relative; files = $files.Count; bytes = [long](($files | Measure-Object Length -Sum).Sum) }
    $selected += $relative
}
foreach ($relative in $selected) { Remove-Item -LiteralPath ([IO.Path]::GetFullPath((Join-Path $root $relative))) -Recurse -Force }
$remaining = @($selected | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) })
$gitAfter = @(git -C $root status --short)
$receipt = [ordered]@{ date = '2026-10-29'; targets = $rows; files = ($rows | Measure-Object files -Sum).Sum;
    bytes = ($rows | Measure-Object bytes -Sum).Sum; remaining = $remaining.Count; errors = 0; preservedConcurrentFiles = $preserved;
    gitStatusUnchanged = ($gitBefore -join "`n") -eq ($gitAfter -join "`n");
    replay = @{ steps = $result.steps; units = $result.units; hash = $result.hash; actionMs = $result.actionMs } }
$destination = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'hre30-followup-cleanup.json') -Encoding utf8
[pscustomobject]@{ files = $receipt.files; bytes = $receipt.bytes; remaining = $receipt.remaining; errors = 0;
    gitStatusUnchanged = $receipt.gitStatusUnchanged } | ConvertTo-Json -Compress
if ($remaining.Count) { throw 'Cleanup incomplete' }
