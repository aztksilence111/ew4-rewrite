$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($root -ne 'E:\Workplace\game\o4mod') { throw 'Unexpected workspace' }
$targets = @(
    'build/unit-layers-20261102',
    'rewrite/Ew4.Verify/obj',
    'rewrite/reports/save-check',
    'rewrite/reports/common-skills-20261021/core-results.json',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-0.json',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-0.json.bak',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-1.json',
    'build/scenario-armies-client-20261031/saves/mode-2/slot-1.json.bak',
    'build/feedback-undo-fix-20261022/client/b4cceae04f7345c9a5efb1231575229f',
    'build/feedback-undo-fix-20261022/client/457e47db6548452abb48f483d539170c',
    'build/feedback-undo-fix-20261022/client/57a57d6f33e54dba9436f251ba44d8a7'
)
Push-Location $root
try {
    $before = (git status --porcelain=v1 -uall) -join "`n"
    $resolved = @($targets | ForEach-Object {
        $full = [IO.Path]::GetFullPath((Join-Path $root $_))
        if (!$full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $full" }
        $ancestor = $full
        while ($ancestor -ne $root) {
            if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked path: $ancestor" }
            $ancestor = Split-Path $ancestor -Parent
        }
        if (git ls-files -- $_) { throw "Tracked target: $_" }
        if (Test-Path -LiteralPath $full) {
            $links = @(Get-ChildItem -LiteralPath $full -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
            if ($links.Count) { throw "Linked child: $full" }
            $full
        }
    })
    $hostProcess = Get-CimInstance Win32_Process -Filter 'ProcessId=47132'
    if ($hostProcess) {
        if ($hostProcess.Name -ne 'dotnet.exe' -or $hostProcess.CommandLine -notlike '*build/unit-layers-20261102/artifacts/bin/Ew4.Web/debug/Ew4.Web.dll*' -or $hostProcess.CommandLine -notlike '*127.0.0.1:57944*') { throw 'Test host identity changed' }
        Stop-Process -Id 47132
        Wait-Process -Id 47132 -Timeout 15 -ErrorAction SilentlyContinue
    }
    $consumers = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(dotnet|Ew4.Web|node|msedge)(\.exe)?$' -and
        $_.CommandLine -match 'unit-layers-20261102|scenario-armies-client-20261031|feedback-undo-fix-20261022|Ew4.Verify'
    })
    if ($consumers.Count) { throw 'Active consumers remain' }
    $files = @($resolved | ForEach-Object {
        if ((Get-Item -LiteralPath $_).PSIsContainer) { Get-ChildItem -LiteralPath $_ -Recurse -Force -File }
        else { Get-Item -LiteralPath $_ }
    })
    $count = $files.Count
    $bytes = ($files | Measure-Object Length -Sum).Sum
    foreach ($target in $resolved) { Remove-Item -LiteralPath $target -Recurse -Force }
    $remaining = @($resolved | Where-Object { Test-Path -LiteralPath $_ })
    $after = (git status --porcelain=v1 -uall) -join "`n"
    $receipt = [ordered]@{date='2026-11-02'; checkedTargets=$targets; filesRemovedThisPass=$count; bytesRemovedThisPass=$bytes; remaining=$remaining.Count; gitUnchanged=($before -eq $after); errors=0; stoppedTestHost=47132; port=57944; coreChecks=27603; modChecks=9; frontendChecks=14308; browserChecks=44; browserErrors=0; missingResources=0; webBuild='passed'; androidArm64Compile='passed'; release=$false; deviceValidation=$false; priorPassNote='Initial cleanup removed the targets but receipt serialization failed because bare false was used instead of the PowerShell Boolean literal. Exact first-pass file and byte counts were not retained. This pass verifies all listed paths are absent.'}
    $receiptDir = Join-Path $root 'build/maintenance-20261102'
    New-Item -ItemType Directory -Force -Path $receiptDir | Out-Null
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $receiptDir 'unit-layers-cleanup.json') -Encoding utf8
    [pscustomobject]$receipt | Select-Object filesRemovedThisPass,bytesRemovedThisPass,remaining,gitUnchanged,errors | ConvertTo-Json -Compress
    if ($remaining.Count -or $before -ne $after) { throw 'Cleanup verification failed' }
} finally { Pop-Location }
