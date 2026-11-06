param([switch]$NoRuntimeCopy)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$workspace = Split-Path $taskRoot -Parent
function Under([string]$relative) {
    $target = [IO.Path]::GetFullPath((Join-Path $taskRoot $relative))
    if (-not $target.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output escapes isolated mod directory' }
    return $target
}
$reportRoot = Under 'reports'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
function ProtectedInventory {
    $roots = @((Join-Path $workspace 'rewrite'), (Join-Path $workspace 'apk_work'))
    $files = @($roots | ForEach-Object { Get-ChildItem -LiteralPath $_ -File -Recurse })
    $files += @(Get-ChildItem -LiteralPath $workspace -File | Where-Object { $_.Extension -match '^\.(apk|zip)$' })
    return @($files | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($workspace, $_.FullName); length = $_.Length; modified = $_.LastWriteTimeUtc.Ticks }
    })
}
$before = ProtectedInventory
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Under 'reports/protected-before.json') -Encoding utf8
try {
    $libs = Under 'build/tool-libs'
    New-Item -ItemType Directory -Path $libs -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $workspace 'build/ew4-mod-kit') -File -Filter '*.dll' |
        Where-Object { $_.Name -match '^(Ew4\.|MoonSharp|StbImage|AssetRipper)' } |
        Copy-Item -Destination $libs -Force
    $runtime = Under 'runtime'
    if (-not $NoRuntimeCopy -or -not (Test-Path -LiteralPath (Join-Path $runtime 'Ew4.Web.exe'))) {
        New-Item -ItemType Directory -Path $runtime -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $workspace 'build/release-0.2.4-20261020/web') -File |
            Copy-Item -Destination $runtime -Force
        Copy-Item -LiteralPath (Join-Path $workspace 'rewrite/Ew4.Web/wwwroot') -Destination $runtime -Recurse -Force
        $data = Under 'runtime/data'
        New-Item -ItemType Directory -Path $data -Force | Out-Null
        foreach ($part in @('original','legacy','client')) {
            Copy-Item -LiteralPath (Join-Path $workspace ('rewrite/data/' + $part)) -Destination $data -Recurse -Force
        }
    }
    dotnet run --project (Under 'tools/Mingguo.Build.csproj') -- $taskRoot $workspace
    if ($LASTEXITCODE -ne 0) { throw 'Build or content checks failed' }
} finally {
    $after = ProtectedInventory
    $changes = @(Compare-Object ($before | ForEach-Object { $_ | ConvertTo-Json -Compress }) ($after | ForEach-Object { $_ | ConvertTo-Json -Compress }))
    [ordered]@{ protectedFiles = $before.Count; changes = $changes.Count; comparison = 'full protected-tree path/length/mtime; consumed files also SHA256 verified by builder'; details = $changes } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Under 'reports/isolation.json') -Encoding utf8
    if ($changes.Count -gt 0) { throw 'Protected inputs changed. See reports/isolation.json; no automatic rollback was performed.' }
}
