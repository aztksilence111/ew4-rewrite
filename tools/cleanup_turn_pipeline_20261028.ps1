$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root 'build/turn-feedback-20261028'
$removedLinks = 0
$server = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine.Replace('\','/') -match 'turn-feedback-20261028/host/Ew4.Web.dll' -and $_.CommandLine -match ':5602' }
foreach ($item in $server) { $process = Get-Process -Id $item.ProcessId; $process.Kill(); $process.WaitForExit() }
$links = @(@('rewrite/data','rewrite/data'),@('rewrite/mod-kit','rewrite/mod-kit'),@('rewrite/Ew4.Web','rewrite/Ew4.Web'),@('reverse/reports','reverse/reports'))
foreach ($link in $links) {
    $path = Join-Path $testRoot ('verify-root/' + $link[0])
    $expected = [IO.Path]::GetFullPath((Join-Path $root $link[1]))
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path -Force
        if (-not $item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or [IO.Path]::GetFullPath($item.Target) -ne $expected) { throw "Unexpected input link: $path" }
        Remove-Item -LiteralPath $path -Force
        $removedLinks++
        if (-not (Test-Path -LiteralPath $expected)) { throw 'Input directory disappeared' }
    }
}
$targets = @('build/turn-feedback-20261028','tools/TurnPipelineAudit/bin','tools/TurnPipelineAudit/obj','rewrite/Ew4.Verify/bin','rewrite/Ew4.Verify/obj',
    'rewrite/reports/client-slot-test/run-6dd8399ae40b40949df70d849a14dae4')
$clients = @('029bf832bd034facbeea5e07d8d892a4','1a8608f9abbf45bda5b032ac32489821','1acd6481946a443f836b5f1bef7b9f9a',
    '2bc53555113c443fb060d24a187d528d','4091ae6925ab4f66a58fb3ec6506397d','4e9a1274ccfc4021b73322bee6c4ef55',
    '61bc6bfb12a84414950d931efb3e7389','648be4f84d074504bfe9aeed47f8d7ab','8106210ab0c746b496fe2fade29b3ef0',
    '95c9568c76bd40298f19db07f9f9a0bb','b326d0f0103d489c83f03bb2cc43a77a','bd2cc1b8d747492a97df859171c262f4',
    'd3f357eb60eb4a7a8f9a76b19e1a6a25','dc71d38dbd3641cb85dd4c389a1e6ac3','e6dda56286294419936d6d5fda9b714c','f4cbed1016734ef6951d7188d946e259')
$targets += $clients | ForEach-Object { 'rewrite/reports/fort-construction-20261021/clients/' + $_ }
$processes = @(Get-CimInstance Win32_Process)
$gitState = @(git -C $root status --short)
$rows = @()
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolved path differs: $absolute" }
    $items = @((Get-Item -LiteralPath $absolute -Force))
    if ($items[0].PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($absolute) }) { throw "Active consumer: $relative" }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
$remaining = @($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) })
$directory = Join-Path $root 'build/maintenance-20261028'
$receiptPath = Join-Path $directory 'turn-pipeline-cleanup.json'
$previous = if (Test-Path -LiteralPath $receiptPath) { Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json } else { $null }
if ($previous) { $rows = @($previous.targets) + $rows; $removedLinks += $previous.inputLinksRemoved }
$stoppedIds = @(@($previous.stoppedServer) + @($server.ProcessId)) | Where-Object { $null -ne $_ }
$receipt = [ordered]@{date='2026-10-28';targets=$rows;files=($rows | Measure-Object files -Sum).Sum;bytes=($rows | Measure-Object bytes -Sum).Sum;
    remaining=$remaining.Count;errors=0;stoppedServer=@($stoppedIds);inputLinksRemoved=$removedLinks;
    gitStatusUnchanged=[bool](-not (Compare-Object $gitState @(git -C $root status --short)));
    preserved='Original map/video/screenshot, player services/accounts/saves/mods, shared builds, earlier reports and concurrent work'}
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding utf8
[pscustomobject]@{targets=$rows.Count;files=$receipt.files;bytes=$receipt.bytes;remaining=$remaining.Count;errors=0;gitStatusUnchanged=$receipt.gitStatusUnchanged} | ConvertTo-Json -Compress
