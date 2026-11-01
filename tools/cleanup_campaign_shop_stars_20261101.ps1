$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$taskPath = Join-Path $rootPath 'build/campaign-shop-stars-20261101'
$runtimePath = Join-Path $rootPath 'build/r-campaign-shop-stars-preview-20261101'
$receiptPath = Join-Path $rootPath 'build/maintenance-20261101/campaign-shop-stars.json'
$report = Get-Content (Join-Path $taskPath 'browser.json') -Raw | ConvertFrom-Json
if ($report.checks -ne 27 -or $report.errors.Count -or $report.missing.Count) { throw 'Browser verification did not pass' }
$hostProcess = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -eq (Join-Path $runtimePath 'Ew4.Web.exe') -and $_.CommandLine.Contains('campaign-shop-stars-20261101/browser-profile')
})
if ($hostProcess.Count -ne 1) { throw 'Unexpected test host identity' }
$statusBefore = @(git -C $rootPath status --short)
$absolute = [IO.Path]::GetFullPath($taskPath)
if (-not $absolute.StartsWith((Join-Path $rootPath 'build') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    (Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw 'Cleanup target outside task directory' }
$children = @(Get-Item -LiteralPath $absolute -Force) + @(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
if ($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw 'Linked cleanup target' }
if (@(git -C $rootPath ls-files -- 'build/campaign-shop-stars-20261101').Count) { throw 'Tracked cleanup target' }
foreach ($consumer in Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $hostProcess[0].ProcessId -and $_.CommandLine }) {
    if ($consumer.CommandLine.Replace('/','\').Contains($absolute + '\') -or
        $consumer.ExecutablePath -and $consumer.ExecutablePath.StartsWith($absolute + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Active cleanup target consumer: $($consumer.ProcessId)"
    }
}
$files = @($children | Where-Object { -not $_.PSIsContainer })
Stop-Process -Id $hostProcess[0].ProcessId
Wait-Process -Id $hostProcess[0].ProcessId -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $absolute -Recurse -Force
if (Test-Path -LiteralPath $absolute) { throw 'Cleanup target remains' }
if (Compare-Object $statusBefore @(git -C $rootPath status --short)) { throw 'Cleanup changed Git status' }
New-Item -ItemType Directory -Path (Split-Path $receiptPath -Parent) -Force | Out-Null
$receipt = [ordered]@{date='2026-11-01';technologyChecks=2726;browserChecks=27;browserErrors=0;missingAssets=0;
    webCompile='passed';androidArm64Compile='passed';compatibleFrontendFiles=54;target=$absolute;
    files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum);remaining=0;errors=0;gitStatusPreserved=$true;
    scope='Source fix and clean preview; no APK/release or Android device test'}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding utf8
if (Get-NetTCPConnection -State Listen -LocalPort 57942 -ErrorAction SilentlyContinue) { throw 'Preview port is occupied' }
$preview = Start-Process -FilePath (Join-Path $runtimePath 'Ew4.Web.exe') -ArgumentList @('--urls','http://127.0.0.1:57942',
    '--contentRoot',(Join-Path $rootPath 'rewrite/Ew4.Web'),'--data-root',(Join-Path $rootPath 'rewrite/data'),
    '--save-root',(Join-Path $runtimePath 'saves'),'--mod-root',(Join-Path $runtimePath 'mods')) -WorkingDirectory $runtimePath -WindowStyle Hidden -PassThru
[ordered]@{files=$receipt.files;bytes=$receipt.bytes;remaining=0;errors=0;previewPid=$preview.Id;url='http://127.0.0.1:57942'} | ConvertTo-Json -Compress
