$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot 'build/morale-animation-20261101'))
$runtimeRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot 'build/r-morale-animation-preview-20261101/runtime'))
if($testRoot -ne 'E:\Workplace\game\o4mod\build\morale-animation-20261101'){throw 'Unexpected cleanup target'}
if(!(Test-Path -LiteralPath $testRoot -PathType Container)){throw 'Test directory missing'}
if(@(git -C $repoRoot ls-files -- 'build/morale-animation-20261101').Count){throw 'Tracked cleanup target'}
$beforeGit=@(git -C $repoRoot status --porcelain=v1) -join "`n"
$entries=@(Get-Item -LiteralPath $testRoot)+@(Get-ChildItem -LiteralPath $testRoot -Recurse -Force)
if($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }){throw 'Reparse point in cleanup target'}
$files=@($entries | Where-Object {!$_.PSIsContainer})
$bytes=($files | Measure-Object -Property Length -Sum).Sum
$runtimeHashes=@(Get-ChildItem -LiteralPath $runtimeRoot -File | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
$validation=@{date='2026-11-01';moraleUnitChecks=83;nativeFrontendChecks=14308;browserMoraleRecruitChecks=33;stableMoraleBrowserChecks=13;combatBrowserChecks=42;errors=0;missingResources=0;webBuild='Release, 0 warnings, 0 errors';androidBuild='ARM64 Compile, 0 warnings, 0 errors';frontend='Chrome 70, 54 files';recruitFrames=@(29,28,30,29);recruitWaitMs=480;devices='Browser desktop/mobile emulation; no Android physical-device test';runtimeHashes=$runtimeHashes}
$receiptRoot=Join-Path $repoRoot 'build/maintenance-20261101'
New-Item -ItemType Directory -Path $receiptRoot -Force | Out-Null
$validation | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $receiptRoot 'morale-recruit-validation.json') -Encoding UTF8
$hostProcess=Get-CimInstance Win32_Process -Filter 'ProcessId=46368' -ErrorAction SilentlyContinue
if($hostProcess){
  if($hostProcess.ExecutablePath -ne (Join-Path $runtimeRoot 'Ew4.Web.exe') -or $hostProcess.CommandLine -notlike '*--urls http://127.0.0.1:57943*' -or $hostProcess.CommandLine -notlike '*--save-root E:/Workplace/game/o4mod/build/morale-animation-20261101/profile*'){throw 'Unexpected process identity'}
  Stop-Process -Id $hostProcess.ProcessId
  Wait-Process -Id $hostProcess.ProcessId -Timeout 15 -ErrorAction SilentlyContinue
}
$consumers=@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath.StartsWith($testRoot,[StringComparison]::OrdinalIgnoreCase) -or $_.CommandLine -like '*--save-root E:/Workplace/game/o4mod/build/morale-animation-20261101/*') })
if($consumers.Count){throw 'Cleanup directory is still in use'}
Remove-Item -LiteralPath $testRoot -Recurse -Force
if(Test-Path -LiteralPath $testRoot){throw 'Cleanup incomplete'}
$afterGit=@(git -C $repoRoot status --porcelain=v1) -join "`n"
$receipt=@{date='2026-11-01';target=$testRoot;files=$files.Count;bytes=$bytes;remaining=0;errors=0;gitUnchanged=($beforeGit -eq $afterGit);stoppedTestProcess=46368;preservedRuntimeFiles=$runtimeHashes.Count;scope='Only isolated morale/recruit test data, screenshots, logs and duplicate build output. Player services, saves, Mods, source tests and release packages retained.'}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $receiptRoot 'morale-recruit-cleanup.json') -Encoding UTF8
$receipt | ConvertTo-Json -Compress
