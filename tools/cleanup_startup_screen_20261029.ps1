param([int]$TestProcessId)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets = @('build/startup-screen-check-20261029', 'build/apk-font-fix-20261021',
    'tools/__pycache__/build_startup_splash.cpython-310.pyc',
    'tools/__pycache__/build_startup_splash.cpython-311.pyc', 'tools/__pycache__/build_startup_splash.cpython-312.pyc',
    'tools/__pycache__/build_startup_splash.cpython-313.pyc', 'tools/__pycache__/build_startup_splash.cpython-314.pyc')
$before = @(git -C $root status --short)
if ($TestProcessId) {
    $testHost = Get-CimInstance Win32_Process -Filter "ProcessId=$TestProcessId"
    if ($testHost) {
        if ($testHost.Name -ne 'dotnet.exe' -or -not $testHost.CommandLine.Replace('/', '\').Contains('build\startup-screen-check-20261029\compile\bin\Ew4.Web')) { throw 'Not this task test host' }
        Stop-Process -Id $TestProcessId
        Wait-Process -Id $TestProcessId -ErrorAction SilentlyContinue
    }
}
$rows = @()
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolved path differs: $absolute" }
    $items = @((Get-Item -LiteralPath $absolute -Force))
    if ($items[0].PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $absolute" }
    $consumers = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($absolute) })
    if ($consumers.Count) { throw "Active consumer: $absolute" }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [ordered]@{target=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
    Remove-Item -LiteralPath $absolute -Force -Recurse
}
$receipt = [ordered]@{date='2026-10-29';targets=$rows;files=[int](($rows | ForEach-Object { $_.files } | Measure-Object -Sum).Sum);
    bytes=[long](($rows | ForEach-Object { $_.bytes } | Measure-Object -Sum).Sum);remaining=@($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) }).Count;
    errors=0;gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $root status --short)));
    preserved='Source artwork and checks, clean preview runtime, shared frontend, other services and player data, original input and releases'}
$directory = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'startup-screen-cleanup.json') -Encoding utf8
$receipt | ConvertTo-Json -Depth 5 -Compress
