$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$taskPath = Join-Path $rootPath 'build/mountain-installation-20261101'
$previewPath = Join-Path $rootPath 'build/r-mountain-installation-preview-20261101'
$receiptPath = Join-Path $rootPath 'build/maintenance-20261101/mountain-installation.json'
$browser = Get-Content (Join-Path $taskPath 'browser.json') -Raw | ConvertFrom-Json
if ($browser.checks -ne 35 -or $browser.errors.Count -or $browser.missing.Count) { throw 'Browser checks must pass before cleanup' }
$hostProcess = Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -eq (Join-Path $previewPath 'Ew4.Web.exe') -and $_.CommandLine.Contains($taskPath)
}
if (@($hostProcess).Count -ne 1) { throw 'Unexpected test host identity' }
$statusBefore = @(git -C $rootPath status --short)
$active = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like ($rootPath + '\*') })
$targets = @($taskPath)
$geography = Join-Path $rootPath 'build/impassable-geography-20261025'
if (Test-Path -LiteralPath $geography) {
    $files = @(Get-ChildItem -LiteralPath $geography -Force)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'core-checks.json') { throw 'Unexpected geography test directory contents' }
    $targets += $geography
}
$entries = foreach ($target in $targets) {
    $absolute = [IO.Path]::GetFullPath($target)
    if (-not $absolute.StartsWith((Join-Path $rootPath 'build') + '\', [StringComparison]::OrdinalIgnoreCase) -or
        (Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Out of scope: $absolute" }
    $children = @(Get-Item -LiteralPath $absolute -Force) + @(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
    if ($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Linked cleanup target: $absolute" }
    $relative = [IO.Path]::GetRelativePath($rootPath, $absolute).Replace('\','/')
    if (@(git -C $rootPath ls-files -- $relative).Count) { throw "Tracked cleanup target: $relative" }
    foreach ($consumer in $active | Where-Object ProcessId -ne $hostProcess.ProcessId) {
        if ($consumer.ExecutablePath.StartsWith($absolute + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $consumer.CommandLine.Contains($absolute)) { throw "Active consumer: $absolute" }
    }
    $files = @($children | Where-Object { -not $_.PSIsContainer })
    [pscustomobject]@{path=$absolute;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
}
Stop-Process -Id $hostProcess.ProcessId
Wait-Process -Id $hostProcess.ProcessId -ErrorAction SilentlyContinue
foreach ($entry in $entries) { Remove-Item -LiteralPath $entry.path -Recurse -Force }
if (Compare-Object $statusBefore @(git -C $rootPath status --short)) { throw 'Cleanup changed Git status' }
$remaining = @($entries | Where-Object { Test-Path -LiteralPath $_.path })
if ($remaining.Count) { throw 'Cleanup targets remain' }
New-Item -ItemType Directory -Path (Split-Path $receiptPath -Parent) -Force | Out-Null
$receipt = [ordered]@{coreChecks=30311;focusedChecks=46;browserChecks=35;webCompile='passed';androidArm64Compile='passed';
    targets=$entries;files=($entries | Measure-Object files -Sum).Sum;bytes=($entries | Measure-Object bytes -Sum).Sum;
    remaining=0;errors=0;gitStatusPreserved=$true;scope='Source fix; no APK or release; no Android device test'}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding utf8
if (Get-NetTCPConnection -State Listen -LocalPort 57941 -ErrorAction SilentlyContinue) { throw 'Preview port is occupied' }
$previewProcess = Start-Process -FilePath (Join-Path $previewPath 'Ew4.Web.exe') -ArgumentList @('--urls','http://127.0.0.1:57941',
    '--contentRoot',(Join-Path $rootPath 'rewrite/Ew4.Web'),'--data-root',(Join-Path $rootPath 'rewrite/data'),
    '--save-root',(Join-Path $previewPath 'saves'),'--mod-root',(Join-Path $previewPath 'mods')) -WorkingDirectory $previewPath -WindowStyle Hidden -PassThru
[ordered]@{files=$receipt.files;bytes=$receipt.bytes;remaining=0;errors=0;previewPid=$previewProcess.Id;url='http://127.0.0.1:57941'} | ConvertTo-Json -Compress
