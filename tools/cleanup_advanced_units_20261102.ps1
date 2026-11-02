$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($taskRoot -ne 'E:\Workplace\game\o4mod') { throw 'Unexpected workspace' }
$taskTargets = @(
    'build/advanced-units-20261102',
    'build/unit-layers-20261102',
    'rewrite/reports/save-check',
    'rewrite/reports/common-skills-20261021/core-results.json',
    'rewrite/reports/ai_production_performance.json',
    'rewrite/reports/frontend_native.json',
    'rewrite/reports/legacy_buildings.json',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-0.json',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-0.json.bak',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-1.json',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-1.json.bak',
    'build/feedback-undo-fix-20261022/client/3717495c61a747c384b4f4cfe1525b11',
    'build/feedback-undo-fix-20261022/client/6a313753d7614cf497b7b3db0a0cd49e',
    'build/feedback-undo-fix-20261022/client/b458e1b743434046b1aeea7991332fe5',
    'build/feedback-undo-fix-20261022/client/edb89ea6b8c841d99e21557d32ff3181'
)
Push-Location $taskRoot
try {
    $taskBefore = (git status --porcelain=v1 -uall) -join "`n"
    $taskResolved = @($taskTargets | ForEach-Object {
        $taskFull = [IO.Path]::GetFullPath((Join-Path $taskRoot $_))
        if (!$taskFull.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $taskFull" }
        $taskAncestor = $taskFull
        while ($taskAncestor -ne $taskRoot) {
            if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked ancestor: $taskAncestor" }
            $taskAncestor = Split-Path $taskAncestor -Parent
        }
        if (git ls-files -- $_) { throw "Tracked target: $_" }
        if (Test-Path -LiteralPath $taskFull) {
            if (@(Get-ChildItem -LiteralPath $taskFull -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Linked child: $taskFull" }
            $taskFull
        }
    })
    $taskHostProcess = Get-CimInstance Win32_Process -Filter 'ProcessId=11704'
    if ($taskHostProcess) {
        if ($taskHostProcess.Name -ne 'dotnet.exe' -or $taskHostProcess.CommandLine -notlike '*build/advanced-units-20261102/artifacts/bin/Ew4.Web/debug/Ew4.Web.dll*' -or $taskHostProcess.CommandLine -notlike '*127.0.0.1:57945*') { throw 'Test host identity changed' }
        Stop-Process -Id 11704
        Wait-Process -Id 11704 -Timeout 15 -ErrorAction SilentlyContinue
    }
    $taskConsumers = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(dotnet|Ew4.Web|node|msedge)(\.exe)?$' -and
        $_.CommandLine -match 'advanced-units-20261102|unit-layers-20261102|scenario-armies-client-20261031|feedback-undo-fix-20261022|Ew4.Verify'
    })
    if ($taskConsumers.Count) { throw 'Active consumers remain' }
    $taskFiles = @($taskResolved | ForEach-Object {
        if ((Get-Item -LiteralPath $_).PSIsContainer) { Get-ChildItem -LiteralPath $_ -Recurse -Force -File }
        else { Get-Item -LiteralPath $_ }
    })
    $taskReceipt = [ordered]@{
        date = '2026-11-02'; checkedTargets = $taskTargets; resolvedTargets = $taskResolved
        files = $taskFiles.Count; bytes = ($taskFiles | Measure-Object Length -Sum).Sum
        stoppedTestHost = 11704; port = 57945; status = 'validated-before-delete'
        coreChecks = 59390; modChecks = 35; frontendChecks = 14308; browserChecks = 41
        webBuild = 'passed'; androidArm64Compile = 'passed'; release = $false; deviceValidation = $false
    }
    $taskReceiptDir = Join-Path $taskRoot 'build/maintenance-20261102'
    New-Item -ItemType Directory -Force -Path $taskReceiptDir | Out-Null
    $taskReceiptPath = Join-Path $taskReceiptDir 'advanced-units-cleanup.json'
    $taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskReceiptPath -Encoding utf8
    foreach ($taskTarget in $taskResolved) { Remove-Item -LiteralPath $taskTarget -Recurse -Force }
    $taskRemaining = @($taskResolved | Where-Object { Test-Path -LiteralPath $_ })
    $taskAfter = (git status --porcelain=v1 -uall) -join "`n"
    $taskReceipt.status = 'complete'
    $taskReceipt.remaining = $taskRemaining.Count
    $taskReceipt.gitUnchanged = ($taskBefore -eq $taskAfter)
    $taskReceipt.errors = 0
    $taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskReceiptPath -Encoding utf8
    [pscustomobject]$taskReceipt | Select-Object files,bytes,remaining,gitUnchanged,errors | ConvertTo-Json -Compress
    if ($taskRemaining.Count -or $taskBefore -ne $taskAfter) { throw 'Cleanup verification failed' }
} finally { Pop-Location }
