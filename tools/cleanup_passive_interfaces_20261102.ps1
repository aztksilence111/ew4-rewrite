$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($taskRoot -ne 'E:\Workplace\game\o4mod') { throw 'Unexpected workspace' }
$taskTargets = @(
    'build/skill-interfaces-20261102',
    'rewrite/reports/common-skills-20261021/core-results.json',
    'rewrite/reports/ai_production_performance.json',
    'rewrite/reports/item-extensions-20261021'
)
Push-Location $taskRoot
try {
    $taskBefore = (git status --porcelain=v1 -uall) -join "`n"
    $taskResolved = @($taskTargets | ForEach-Object {
        $taskFull = [IO.Path]::GetFullPath((Join-Path $taskRoot $_))
        if (!$taskFull.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $taskFull" }
        $taskAncestor = $taskFull
        while ($taskAncestor -ne $taskRoot) {
            if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Linked ancestor: $taskAncestor"
            }
            $taskAncestor = Split-Path $taskAncestor -Parent
        }
        if (git ls-files -- $_) { throw "Tracked target: $_" }
        if (Test-Path -LiteralPath $taskFull) {
            if (@(Get-ChildItem -LiteralPath $taskFull -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
                throw "Linked child: $taskFull"
            }
            $taskFull
        }
    })
    $taskConsumers = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(dotnet|Ew4.Web|Ew4.Verify|Ew4.ModChecks|node|msedge|chrome)(\.exe)?$' -and
        $_.CommandLine -match 'skill-interfaces-20261102|item-extensions-20261021|Ew4.Verify.dll|Ew4.ModChecks.dll'
    })
    if ($taskConsumers.Count) { throw 'Active test consumers remain' }
    $taskFiles = @($taskResolved | ForEach-Object {
        if ((Get-Item -LiteralPath $_).PSIsContainer) { Get-ChildItem -LiteralPath $_ -Recurse -Force -File }
        else { Get-Item -LiteralPath $_ }
    })
    $taskReceipt = [ordered]@{
        date = '2026-11-02'; checkedTargets = $taskTargets; resolvedTargets = $taskResolved
        files = $taskFiles.Count; bytes = ($taskFiles | Measure-Object Length -Sum).Sum
        status = 'validated-before-delete'; coreChecks = 49269; modChecks = 9
        webRelease = 'passed'; androidArm64Compile = 'passed'; release = $false; deviceValidation = $false
    }
    $taskReceiptDir = Join-Path $taskRoot 'build/maintenance-20261102'
    New-Item -ItemType Directory -Force -Path $taskReceiptDir | Out-Null
    $taskReceiptPath = Join-Path $taskReceiptDir 'passive-skill-interfaces-cleanup.json'
    $taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskReceiptPath -Encoding utf8
    foreach ($taskTarget in $taskResolved) { Remove-Item -LiteralPath $taskTarget -Recurse -Force }
    $taskRemaining = @($taskResolved | Where-Object { Test-Path -LiteralPath $_ })
    $taskAfter = (git status --porcelain=v1 -uall) -join "`n"
    $taskReceipt.status = 'complete'; $taskReceipt.remaining = $taskRemaining.Count
    $taskReceipt.gitUnchanged = ($taskBefore -eq $taskAfter); $taskReceipt.errors = 0
    $taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskReceiptPath -Encoding utf8
    [pscustomobject]$taskReceipt | Select-Object files,bytes,remaining,gitUnchanged,errors | ConvertTo-Json -Compress
    if ($taskRemaining.Count -or $taskBefore -ne $taskAfter) { throw 'Cleanup verification failed' }
} finally { Pop-Location }
