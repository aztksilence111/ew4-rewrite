$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$previewPath = Join-Path $rootPath 'build/r-city-exchange-preview-20261101'
$testPath = Join-Path $rootPath 'build/r-city-exchange-tests-20261101'
$receiptPath = Join-Path $rootPath 'build/maintenance-20261101'
$statusBefore = @(git -C $rootPath status --short)
$browser = Get-Content (Join-Path $testPath 'browser.json') -Raw | ConvertFrom-Json
if ($browser.checks -ne 35 -or $browser.errors.Count -or $browser.missing.Count) { throw 'Browser verification must pass first' }
$process = Get-CimInstance Win32_Process -Filter 'ProcessId=9972'
if (-not $process -or $process.ExecutablePath -ne (Join-Path $previewPath 'Ew4.Web.exe') -or -not $process.CommandLine.Contains($testPath)) { throw 'Unexpected test host identity' }
$targets = @($testPath)
foreach ($project in @('Ew4.Android','Ew4.Client','Ew4.Core','Ew4.Legacy','Ew4.ModChecks','Ew4.Mods','Ew4.Persistence','Ew4.Web')) {
    foreach ($part in @('bin','obj')) {
        $candidate = Join-Path $rootPath "rewrite/$project/$part"
        if (Test-Path -LiteralPath $candidate) { $targets += $candidate }
    }
}
$active = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like ($rootPath + '\*') })
$entries = foreach ($target in $targets) {
    $absolute = [IO.Path]::GetFullPath($target)
    if (-not $absolute.StartsWith($rootPath + '\',[StringComparison]::OrdinalIgnoreCase) -or (Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Out of scope: $absolute" }
    $children = @(Get-Item -LiteralPath $absolute -Force) + @(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
    if ($children | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Linked cleanup target: $absolute" }
    $relative = [IO.Path]::GetRelativePath($rootPath,$absolute).Replace('\','/')
    if (@(git -C $rootPath ls-files -- $relative).Count) { throw "Tracked cleanup target: $relative" }
    foreach ($consumer in $active | Where-Object ProcessId -ne 9972) {
        if ($consumer.ExecutablePath.StartsWith($absolute + '\',[StringComparison]::OrdinalIgnoreCase) -or $consumer.CommandLine.Contains($absolute)) { throw "Active consumer: $absolute" }
    }
    $files = @($children | Where-Object { -not $_.PSIsContainer })
    [pscustomobject]@{path=$absolute;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
}
$protectedPaths = @('mingguo-mod/preview-c15/FullMap/generated/profile/saves','mingguo-mod/preview-c15/FullMap/generated/profile-revised/saves')
$protected = foreach ($relative in $protectedPaths) {
    $folder = Join-Path $rootPath $relative
    if (Test-Path -LiteralPath $folder) { Get-ChildItem -LiteralPath $folder -Recurse -File | ForEach-Object { [pscustomobject]@{path=$_.FullName;hash=(Get-FileHash -LiteralPath $_.FullName).Hash} } }
}
New-Item -ItemType Directory -Path $receiptPath -Force | Out-Null
$validation = [ordered]@{buildingPresentationChecks=19;nodeTests=10;browserChecks=$browser.checks;errors=0;missing=0;webCompile='passed';androidArm64Compile='passed';frontendTarget='chrome70';frontendFiles=54;runs=@($browser.runs | Select-Object version,requests,refreshes,rebuilds,medianMs,maxMs);scope='Desktop Edge; no Android device or late-game measurement; no release package'}
$validation | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $receiptPath 'r-city-exchange-verification.json') -Encoding utf8
Stop-Process -Id 9972
Wait-Process -Id 9972 -ErrorAction SilentlyContinue
foreach ($entry in $entries) { Remove-Item -LiteralPath $entry.path -Recurse -Force }
foreach ($entry in $protected) {
    if (-not (Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.hash) { throw 'Protected player file changed' }
}
$statusAfter = @(git -C $rootPath status --short)
if (Compare-Object $statusBefore $statusAfter) { throw 'Cleanup changed Git status' }
$remaining = @($entries | Where-Object { Test-Path -LiteralPath $_.path })
$receipt = [ordered]@{targets=$entries;files=($entries | Measure-Object files -Sum).Sum;bytes=($entries | Measure-Object bytes -Sum).Sum;remaining=$remaining.Count;protectedFiles=@($protected).Count;protectedChanges=0;gitStatusPreserved=$true;stoppedTestProcess=9972;errors=0}
$receipt | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $receiptPath 'r-city-exchange-cleanup.json') -Encoding utf8
$previewProcess = Start-Process -FilePath (Join-Path $previewPath 'Ew4.Web.exe') -ArgumentList @('--urls','http://127.0.0.1:56163','--contentRoot',$previewPath,'--save-root',(Join-Path $previewPath 'saves')) -WorkingDirectory $previewPath -WindowStyle Hidden -PassThru
[ordered]@{files=$receipt.files;bytes=$receipt.bytes;remaining=$remaining.Count;protectedFiles=@($protected).Count;protectedChanges=0;errors=0;previewPid=$previewProcess.Id;url='http://127.0.0.1:56163'} | ConvertTo-Json -Compress
