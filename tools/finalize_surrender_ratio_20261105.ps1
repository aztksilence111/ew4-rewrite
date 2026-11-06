param([int]$QaProcessId = 30664, [int]$PreviewProcessId = 18952)
$ErrorActionPreference = 'Stop'
$ratioRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$receiptDirectory = Join-Path $ratioRoot 'build/maintenance-20261105'
$qaRoot = Join-Path $ratioRoot 'build/surrender-ratio-qa-20261105'
$runtime = Join-Path $ratioRoot 'build/surrender-probability-preview-20261105'
$source = Join-Path $ratioRoot 'rewrite/Ew4.Web/bin/Release/net10.0'
$profile = Join-Path $ratioRoot 'rewrite/local/surrender-probability-preview-20261105'
$before = @(git -C $ratioRoot status --porcelain=v1)
$browser = Get-Content -LiteralPath (Join-Path $qaRoot 'browser/result.json') -Raw | ConvertFrom-Json
if ($browser.checks -ne 24 -or $browser.errors.Count) { throw 'Browser verification incomplete' }
$runtimeFiles = @(Get-ChildItem -LiteralPath (Join-Path $qaRoot 'runtime') -File | ForEach-Object {
    $actual = (Get-FileHash -LiteralPath $_.FullName).Hash
    if ($actual -ne (Get-FileHash -LiteralPath (Join-Path $source $_.Name)).Hash) { throw "Runtime differs: $($_.Name)" }
    [pscustomobject]@{name=$_.Name;sha256=$actual;bytes=$_.Length}
})
if ($runtimeFiles.Count -ne 12) { throw 'Unexpected runtime inventory' }
$protectedFiles = @(Get-ChildItem -LiteralPath $profile -Recurse -File | ForEach-Object {
    [pscustomobject]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
$validation = [ordered]@{date='2026-11-05';formula='commanderBonus * deadInitialCommanders / initialCommanders; commanderBonus range 0..25';
    probabilityChecks=81;saveCompatibilityChecks=11;paidSurrenderChecks=20;diplomacyChecks=748;browserChecks=24;browserErrors=0;
    webBuild='passed';androidArm64Compile='passed';compatibleFiles=54;newApk=$false;androidDeviceTest=$false;runtimeFiles=$runtimeFiles}
$validation | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'surrender-ratio-validation.json') -Encoding utf8
$preview = Get-CimInstance Win32_Process -Filter "ProcessId=$PreviewProcessId"
if (!$preview -or $preview.CommandLine.Replace('\','/') -notlike '*build/surrender-probability-preview-20261105/Ew4.Web.dll*' -or
    $preview.CommandLine.Replace('\','/') -notlike '*--save-root E:/Workplace/game/o4mod/rewrite/local/surrender-probability-preview-20261105*') { throw 'Unexpected preview identity' }
$session = Invoke-RestMethod -Uri 'http://127.0.0.1:56184/api/diplomacy' -Method Post -ContentType 'application/json' -Body '{}'
if ($session.ok -or $session.code -ne 'client.no_session') { throw 'Preview has a session; preserve it' }
$qa = Get-CimInstance Win32_Process -Filter "ProcessId=$QaProcessId"
if (!$qa -or $qa.CommandLine.Replace('\','/') -notlike '*build/surrender-ratio-qa-20261105/runtime/Ew4.Web.dll*' -or
    $qa.CommandLine.Replace('\','/') -notlike '*--save-root E:/Workplace/game/o4mod/build/surrender-ratio-qa-20261105/profile*') { throw 'Unexpected QA identity' }
foreach ($processId in @($QaProcessId, $PreviewProcessId)) {
    $running = Get-Process -Id $processId
    $running.Kill(); $running.WaitForExit()
}
foreach ($file in $runtimeFiles) { Copy-Item -LiteralPath (Join-Path $source $file.name) -Destination (Join-Path $runtime $file.name) -Force }
$arguments = @((Join-Path $runtime 'Ew4.Web.dll'),'--root',$ratioRoot,'--save-root',$profile,
    '--contentRoot',(Join-Path $ratioRoot 'rewrite/Ew4.Web'),'--urls','http://127.0.0.1:56184','--Logging:LogLevel:Default','Warning')
$hostProcess = Start-Process dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru
$ready = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    try { $catalog = Invoke-RestMethod -Uri 'http://127.0.0.1:56184/api/catalog' -Method Post -ContentType 'application/json' -Body '{}'; $ready = $catalog.ok; if ($ready) { break } }
    catch { Start-Sleep -Milliseconds 500 }
}
if (!$ready) { throw 'Updated preview did not start' }
$changed = @($protectedFiles | Where-Object { !(Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256 })
if ($changed.Count) { throw 'Preview profile changed during runtime update' }
foreach ($file in $runtimeFiles) {
    if ((Get-FileHash -LiteralPath (Join-Path $runtime $file.name)).Hash -ne $file.sha256) { throw 'Updated runtime hash mismatch' }
}
$relativeTargets = @('build/surrender-ratio-qa-20261105','build/diplomacy-20261028/check-profile',
    'build/diplomacy-20261028/homeland-client','build/diplomacy-20261028/browser-fixtures')
foreach ($project in @('Ew4.Android','Ew4.Client','Ew4.Core','Ew4.Legacy','Ew4.Mods','Ew4.Persistence','Ew4.Verify','Ew4.Web')) {
    $relativeTargets += "rewrite/$project/bin", "rewrite/$project/obj"
}
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine })
$plan = @()
foreach ($relative in $relativeTargets) {
    $target = [IO.Path]::GetFullPath((Join-Path $ratioRoot $relative))
    if (!$target.StartsWith($ratioRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw "Out of workspace: $target" }
    if (!(Test-Path -LiteralPath $target)) { continue }
    $items = @(Get-Item -LiteralPath $target) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Link in target: $target" }
    if (@($processes | Where-Object { $_.CommandLine.Replace('\','/').IndexOf($target.Replace('\','/'),[StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count) { throw "Active consumer: $target" }
    if (@(git -C $ratioRoot ls-files -- $relative).Count) { throw "Tracked file: $target" }
    $files = @($items | Where-Object { !$_.PSIsContainer })
    $plan += [pscustomobject]@{path=$target;files=$files.Count;bytes=[long]($files | Measure-Object Length -Sum).Sum}
}
$plan | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'surrender-ratio-cleanup-plan.json') -Encoding utf8
foreach ($item in $plan) { Remove-Item -LiteralPath $item.path -Recurse -Force }
$remaining = @($plan | Where-Object { Test-Path -LiteralPath $_.path })
$after = @(git -C $ratioRoot status --porcelain=v1)
$unchanged = ($before -join "`n") -ceq ($after -join "`n")
$receipt = [ordered]@{date='2026-11-05';targets=$plan.Count;files=($plan | Measure-Object files -Sum).Sum;bytes=($plan | Measure-Object bytes -Sum).Sum;
    remaining=$remaining.Count;errors=0;gitStatusUnchanged=$unchanged;protectedFiles=$protectedFiles.Count;protectedFilesChanged=$changed.Count;
    runtimeFiles=$runtimeFiles.Count;previewPid=$hostProcess.Id;previewUrl='http://127.0.0.1:56184';catalogOk=$catalog.ok;scenarios=@($catalog.data.scenarios).Count;hasSave=$catalog.data.hasSave}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $receiptDirectory 'surrender-ratio-cleanup.json') -Encoding utf8
if ($remaining.Count -or !$unchanged) { throw 'Cleanup post-check failed' }
$receipt | ConvertTo-Json -Compress
