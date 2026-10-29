param([int]$TestProcessId)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targets = @('build/attack-bounds-check-20261029',
    'rewrite/reports/legacy-skill-parameters-20261021/checks.json',
    'rewrite/reports/skill-descriptions-20261023/0720145803704fa397d268e0f9c41f20',
    'rewrite/reports/skill-descriptions-20261023/684f5adff1854675bf0e0c8aa823779f',
    'rewrite/reports/skill-descriptions-20261023/899eb101a04946f0839d633cf0c8ef9a',
    'rewrite/reports/skill-descriptions-20261023/e061a5c853664b0c9de1cd2e61d791e6')
$before = @(git -C $root status --short)
if ($TestProcessId) {
    $testHost = Get-CimInstance Win32_Process -Filter "ProcessId=$TestProcessId"
    if ($testHost) {
        if ($testHost.Name -ne 'dotnet.exe' -or -not $testHost.CommandLine.Contains('attack-bounds-preview/runtime/Ew4.Web.dll') -or -not $testHost.CommandLine.Contains('attack-bounds-check-20261029/web-profile')) { throw 'Not this task test host' }
        Stop-Process -Id $TestProcessId
        Wait-Process -Id $TestProcessId -ErrorAction SilentlyContinue
    }
}
$processes = @(Get-CimInstance Win32_Process)
$rows = @()
foreach ($relative in $targets) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $absolute.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw "Resolved path differs: $absolute" }
    $item = Get-Item -LiteralPath $absolute -Force
    $items = @($item)
    if ($item.PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if ($items | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw "Reparse point: $absolute" }
    if (@(git -C $root ls-files -- $relative).Count) { throw "Tracked input: $relative" }
    if ($processes | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.Replace('/', '\').Contains($absolute) }) { throw "Active consumer: $absolute" }
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    $rows += [pscustomobject]@{path=$relative;files=$files.Count;bytes=[long](($files | Measure-Object Length -Sum).Sum)}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
$receipt = [ordered]@{date='2026-10-29';targets=$rows;files=($rows | Measure-Object files -Sum).Sum;bytes=($rows | Measure-Object bytes -Sum).Sum;
    remaining=@($targets | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) }).Count;errors=0;
    gitStatusUnchanged=[bool](-not (Compare-Object $before @(git -C $root status --short)));preserved='12 preview runtime files, source checks, other services, player data, mods, releases and original input'}
$directory = Join-Path $root 'build/maintenance-20261029'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'attack-bounds-cleanup.json') -Encoding utf8
[pscustomobject]@{files=$receipt.files;bytes=$receipt.bytes;remaining=$receipt.remaining;errors=0;gitStatusUnchanged=$receipt.gitStatusUnchanged} | ConvertTo-Json -Compress
