$ErrorActionPreference = 'Stop'
$reviewWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$reviewRuntime = Join-Path $reviewWorkspace 'build/map-review-runtime-20261027'
$reviewStatus = Invoke-RestMethod 'http://127.0.0.1:5585/api/mods'
$reviewExpected = Get-Content (Join-Path $reviewRuntime 'mods/active.json') -Raw | ConvertFrom-Json
if ($reviewStatus.active -ne $reviewExpected) { throw 'The final review Mod must remain active.' }
$reviewTargets = @(
    'build/map-review-tests-20261027',
    'maps/japan_hre_source_20261026/game-japan-overview.jpg',
    'maps/japan_hre_source_20261026/game-hre-overview.jpg',
    'build/map-review-runtime-20261027/saves/startup-assets/3116844db72e212cf3ab7e8994886bfec294c9d2ea2f2d9d0ceb4d92eab7e0a3',
    'build/map-review-runtime-20261027/saves/startup-assets/c3d7e0453452da80ac9b45cbd69a90ee5ca71f42bdfd3d3b3d47a7f0c8905b8f',
    'build/map-review-runtime-20261027/saves/startup-assets/e4259c987c42c85025a4a7276988d4b5c600d5cd409847e10d6cbe57733edac6'
)
$reviewOldKeys = @(
    '9a308d11defc6c9d753db743c2976b9d426ac4c16c9bd1968c2e77efb89fffc5',
    'c0bf40c8df4a01a2acb7885299de734257acdb5f9fbb19c27f9c670ba50e001e',
    'f9c221ba829b5e68d23ffbecb9bbc94f84959c577c54f77bfef203bd77f974eb',
    '24e061dffe1ba8b23ec7a2ccd7ca4c6a7d6f5a66428757ff51154381f7215e2d',
    '2bb751c524dfb42f00276f72e07b23c86707b421eef4b2e8a57c9e051ba39fc4'
)
$reviewProcesses = @(Get-CimInstance Win32_Process)
$reviewResults = @()
foreach ($reviewRelative in ($reviewTargets + @($reviewOldKeys | ForEach-Object { "build/map-review-runtime-20261027/mods/$_" }))) {
    $reviewTarget = [IO.Path]::GetFullPath((Join-Path $reviewWorkspace $reviewRelative))
    if (-not $reviewTarget.StartsWith($reviewWorkspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $reviewTarget" }
    if (-not (Test-Path -LiteralPath $reviewTarget)) { continue }
    $reviewItem = Get-Item -LiteralPath $reviewTarget -Force
    $reviewChildren = if ($reviewItem.PSIsContainer) { @(Get-ChildItem -LiteralPath $reviewTarget -Recurse -Force) } else { @($reviewItem) }
    if (@($reviewItem) + $reviewChildren | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Linked cleanup target: $reviewTarget" }
    if ($reviewProcesses | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -eq $reviewTarget -or $_.ExecutablePath.StartsWith($reviewTarget + '\', [StringComparison]::OrdinalIgnoreCase)) }) { throw "Active cleanup target: $reviewTarget" }
    $reviewFiles = @($reviewChildren | Where-Object { -not $_.PSIsContainer })
    $reviewBytes = ($reviewFiles | Measure-Object -Property Length -Sum).Sum
    if ($reviewRelative -match '/mods/([a-f0-9]{64})$') {
        $reviewKey = $Matches[1]
        $reviewMod = $reviewStatus.mods | Where-Object { $_.key -eq $reviewKey }
        if (-not $reviewMod -or $reviewMod.id -ne 'three-map-review' -or $reviewMod.active -or $reviewKey -eq $reviewExpected) { throw "Not an obsolete review Mod: $reviewKey" }
        $null = Invoke-RestMethod 'http://127.0.0.1:5585/api/mods' -Method Post -ContentType 'application/json' -Body (@{op='delete'; key=$reviewKey} | ConvertTo-Json -Compress)
    } else {
        Remove-Item -LiteralPath $reviewTarget -Force -Recurse
    }
    $reviewResults += [PSCustomObject]@{path=$reviewRelative; files=$reviewFiles.Count; bytes=[long]$reviewBytes; remaining=(Test-Path -LiteralPath $reviewTarget)}
}
$reviewFinal = Invoke-RestMethod 'http://127.0.0.1:5585/api/mods'
if ($reviewFinal.active -ne $reviewExpected) { throw 'Active review Mod changed during cleanup.' }
$reviewReport = [ordered]@{date='2026-10-27'; targets=$reviewResults.Count; files=($reviewResults | Measure-Object files -Sum).Sum; bytes=($reviewResults | Measure-Object bytes -Sum).Sum; remaining=@($reviewResults | Where-Object remaining).Count; errors=0; activeKey=$reviewExpected; results=$reviewResults}
$reviewReport | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reviewWorkspace 'maps/japan_hre_source_20261026/game-integration-cleanup.json') -Encoding utf8
[PSCustomObject]$reviewReport | Select-Object targets,files,bytes,remaining,errors | ConvertTo-Json -Compress
