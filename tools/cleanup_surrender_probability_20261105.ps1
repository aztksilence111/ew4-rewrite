param([int]$QaProcessId = 2744)
$ErrorActionPreference = 'Stop'
$probabilityRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$receiptDirectory = Join-Path $probabilityRoot 'build/maintenance-20261105'
$runtime = Join-Path $probabilityRoot 'build/surrender-probability-preview-20261105'
$source = Join-Path $probabilityRoot 'rewrite/Ew4.Web/bin/Release/net10.0'
$previewProfile = Join-Path $probabilityRoot 'rewrite/local/surrender-probability-preview-20261105'
if (Test-Path -LiteralPath $previewProfile) { throw 'Clean preview already exists; preserve its data' }
$before = @(git -C $probabilityRoot status --porcelain=v1)
$runtimeFiles = @(Get-ChildItem -LiteralPath $runtime -File | ForEach-Object {
    $actual = (Get-FileHash -LiteralPath $_.FullName).Hash
    if ($actual -ne (Get-FileHash -LiteralPath (Join-Path $source $_.Name)).Hash) { throw "Runtime mismatch: $($_.Name)" }
    [pscustomobject]@{name=$_.Name;bytes=$_.Length;sha256=$actual}
})
if ($runtimeFiles.Count -ne 12) { throw 'Unexpected preview runtime inventory' }
$browserResult = Get-Content -LiteralPath (Join-Path $probabilityRoot 'build/surrender-probability-qa-20261105/browser/result.json') -Raw | ConvertFrom-Json
if ($browserResult.checks -ne 22 -or $browserResult.errors.Count -ne 0) { throw 'Browser verification incomplete' }
$validation = [ordered]@{date='2026-11-05';referenceApkSha256='cf7e248f9c4b3bc93c4967b510e6634a2e0e0c4c8d6ba28da5dc8ebc0ac70bfb';referenceImagesOpened=4;
    referenceEvidence='Embedded DiplomacySettings.json and prior six-script audit; probability adaptation is R-specific';
    probabilityChecks=63;saveCompatibilityChecks=11;paidSurrenderChecks=20;diplomacyChecks=748;browserChecks=22;browserErrors=0;
    webBuild='passed';androidArm64Compile='passed';compatibleFiles=54;newApk=$false;androidDeviceTest=$false;runtimeFiles=$runtimeFiles}
$validation | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'surrender-probability-validation.json') -Encoding utf8
$qa = Get-CimInstance Win32_Process -Filter "ProcessId=$QaProcessId"
if (!$qa -or $qa.CommandLine.Replace('\','/') -notlike '*build/surrender-probability-preview-20261105/Ew4.Web.dll*' -or
    $qa.CommandLine.Replace('\','/') -notlike '*--save-root E:/Workplace/game/o4mod/build/surrender-probability-qa-20261105/profile*') { throw 'Unexpected QA host identity' }
$running = Get-Process -Id $QaProcessId
$running.Kill(); $running.WaitForExit()
$relativeTargets = @('build/surrender-probability-qa-20261105','build/diplomacy-20261028/check-profile',
    'build/diplomacy-20261028/homeland-client','build/diplomacy-20261028/browser-fixtures')
foreach ($project in @('Ew4.Android','Ew4.Client','Ew4.Core','Ew4.Legacy','Ew4.Mods','Ew4.Persistence','Ew4.Verify','Ew4.Web')) {
    $relativeTargets += "rewrite/$project/bin", "rewrite/$project/obj"
}
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine })
$plan = @()
foreach ($relative in $relativeTargets) {
    $target = [IO.Path]::GetFullPath((Join-Path $probabilityRoot $relative))
    if (!$target.StartsWith($probabilityRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw "Out of workspace: $target" }
    if (!(Test-Path -LiteralPath $target)) { continue }
    $items = @(Get-Item -LiteralPath $target) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Link in target: $target" }
    $normalized = $target.Replace('\','/')
    if (@($processes | Where-Object { $_.CommandLine.Replace('\','/').IndexOf($normalized,[StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count) { throw "Active consumer: $target" }
    if (@(git -C $probabilityRoot ls-files -- $relative).Count) { throw "Tracked file in target: $target" }
    $files = @($items | Where-Object { !$_.PSIsContainer })
    $plan += [pscustomobject]@{path=$target;files=$files.Count;bytes=[long]($files | Measure-Object Length -Sum).Sum}
}
$plan | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'surrender-probability-cleanup-plan.json') -Encoding utf8
foreach ($item in $plan) { Remove-Item -LiteralPath $item.path -Recurse -Force }
$remaining = @($plan | Where-Object { Test-Path -LiteralPath $_.path })
$after = @(git -C $probabilityRoot status --porcelain=v1)
$unchanged = ($before -join "`n") -ceq ($after -join "`n")
$receipt = [ordered]@{date='2026-11-05';targets=$plan.Count;files=($plan | Measure-Object files -Sum).Sum;bytes=($plan | Measure-Object bytes -Sum).Sum;
    remaining=$remaining.Count;errors=0;gitStatusUnchanged=$unchanged;runtimeFiles=$runtimeFiles.Count}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'surrender-probability-cleanup.json') -Encoding utf8
if ($remaining.Count -or !$unchanged) { throw 'Cleanup post-check failed' }
$arguments = @((Join-Path $runtime 'Ew4.Web.dll'),'--root',$probabilityRoot,'--save-root',$previewProfile,
    '--contentRoot',(Join-Path $probabilityRoot 'rewrite/Ew4.Web'),'--urls','http://127.0.0.1:56184','--Logging:LogLevel:Default','Warning')
$hostProcess = Start-Process dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru
$receipt.previewPid = $hostProcess.Id
$receipt.previewUrl = 'http://127.0.0.1:56184'
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'surrender-probability-cleanup.json') -Encoding utf8
$receipt | ConvertTo-Json -Compress
