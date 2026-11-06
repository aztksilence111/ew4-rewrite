param([int]$QaProcessId = 26452)
$ErrorActionPreference = 'Stop'
$saveFeatureRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$saveFeatureReceipt = Join-Path $saveFeatureRoot 'build/maintenance-20261105'
New-Item -ItemType Directory -Force $saveFeatureReceipt | Out-Null
$before = @(git -C $saveFeatureRoot status --porcelain=v1)
$runtime = Join-Path $saveFeatureRoot 'build/save-features-preview-20261105'
$source = Join-Path $saveFeatureRoot 'rewrite/Ew4.Web/bin/Release/net10.0'
$runtimeFiles = @(Get-ChildItem -LiteralPath $runtime -File | ForEach-Object {
    $actual = (Get-FileHash -LiteralPath $_.FullName).Hash
    if ($actual -ne (Get-FileHash -LiteralPath (Join-Path $source $_.Name)).Hash) { throw "Runtime differs: $($_.Name)" }
    [pscustomobject]@{name=$_.Name;sha256=$actual;bytes=$_.Length}
})
$validation = [ordered]@{date='2026-11-05';referenceApkSha256='cf7e248f9c4b3bc93c4967b510e6634a2e0e0c4c8d6ba28da5dc8ebc0ac70bfb';decodedScripts=6;paidSurrenderChecks=20;diplomacyChecks=748;saveExportChecks=35;saveImportChecks=41;browserChecks=48;browserErrors=0;compatibleFiles=54;webBuild='passed';androidArm64Compile='passed';androidDeviceTest=$false;newApk=$false;runtimeFiles=$runtimeFiles}
$validation | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $saveFeatureReceipt 'save-features-validation.json') -Encoding utf8
$qa = Get-CimInstance Win32_Process -Filter "ProcessId=$QaProcessId"
if (!$qa -or $qa.CommandLine.Replace('\','/') -notlike '*build/save-features-preview-20261105/Ew4.Web.dll*' -or
    $qa.CommandLine -notlike '*--save-root E:/Workplace/game/o4mod/build/save-features-qa-20261105/profile*') { throw 'Unexpected QA host' }
$running = Get-Process -Id $QaProcessId
$running.Kill(); $running.WaitForExit()
$relativeTargets = @('build/save-features-qa-20261105','build/glory-save-audit-20261105',
    'build/diplomacy-20261028/check-profile','build/diplomacy-20261028/homeland-client','build/diplomacy-20261028/browser-fixtures',
    'build/save-export-checks','build/save-import-checks')
foreach ($project in @('Ew4.Android','Ew4.Client','Ew4.Core','Ew4.Legacy','Ew4.ModChecks','Ew4.Mods','Ew4.Persistence','Ew4.Verify','Ew4.Web')) {
    $relativeTargets += "rewrite/$project/bin", "rewrite/$project/obj"
}
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine })
$plan = @()
foreach ($relative in $relativeTargets) {
    $target = [IO.Path]::GetFullPath((Join-Path $saveFeatureRoot $relative))
    if (!$target.StartsWith($saveFeatureRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw "Out of workspace: $target" }
    if (!(Test-Path -LiteralPath $target)) { continue }
    $items = @(Get-Item -LiteralPath $target) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Link in target: $target" }
    $normalized = $target.Replace('\','/')
    if (@($processes | Where-Object { $_.CommandLine.Replace('\','/').IndexOf($normalized,[StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count) { throw "Active consumer: $target" }
    $tracked = @(git -C $saveFeatureRoot ls-files -- $relative)
    if ($tracked.Count) { throw "Tracked file in target: $target" }
    $files = @($items | Where-Object { !$_.PSIsContainer })
    $bytes = ($files | Measure-Object -Property Length -Sum).Sum
    $plan += [pscustomobject]@{path=$target;files=$files.Count;bytes=[long]$bytes}
}
$plan | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $saveFeatureReceipt 'save-features-cleanup-plan.json') -Encoding utf8
foreach ($item in $plan) { Remove-Item -LiteralPath $item.path -Recurse -Force }
$remaining = @($plan | Where-Object { Test-Path -LiteralPath $_.path })
$after = @(git -C $saveFeatureRoot status --porcelain=v1)
$unchanged = ($before -join "`n") -ceq ($after -join "`n")
$receipt = [ordered]@{date='2026-11-05';targets=$plan.Count;files=($plan | Measure-Object files -Sum).Sum;bytes=($plan | Measure-Object bytes -Sum).Sum;remaining=$remaining.Count;errors=0;gitStatusUnchanged=$unchanged;runtimeFiles=$runtimeFiles.Count}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $saveFeatureReceipt 'save-features-cleanup.json') -Encoding utf8
if ($remaining.Count -or !$unchanged) { throw 'Cleanup post-check failed' }
$profile = Join-Path $saveFeatureRoot 'rewrite/local/save-features-preview-20261105'
if (Test-Path -LiteralPath $profile) { throw 'Clean profile already exists; preserve it' }
$arguments = @((Join-Path $runtime 'Ew4.Web.dll'),'--root',$saveFeatureRoot,'--save-root',$profile,'--contentRoot',(Join-Path $saveFeatureRoot 'rewrite/Ew4.Web'),'--urls','http://127.0.0.1:56183','--Logging:LogLevel:Default','Warning')
$hostProcess = Start-Process dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru
$receipt.previewPid = $hostProcess.Id
$receipt.previewUrl = 'http://127.0.0.1:56183'
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $saveFeatureReceipt 'save-features-cleanup.json') -Encoding utf8
$receipt | ConvertTo-Json -Compress
