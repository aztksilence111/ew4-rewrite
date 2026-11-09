param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$RuntimeRoot = '',
    [string]$Destination = '',
    [string]$BuildRoot = '',
    [string]$ExamplePackage = '',
    [string]$SourceRef = 'fd6a6a4731166120d6803c63b347821d704269aa',
    [string]$GameVersion = '0.2.34',
    [string]$KitVersion = '1.0.0',
    [string]$Date = '20261109'
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath $Root).Path
if ($GameVersion -notmatch '^\d+\.\d+\.\d+$' -or $KitVersion -notmatch '^\d+\.\d+\.\d+$' -or $Date -notmatch '^\d{8}$') { throw 'Invalid version/date' }
if (!$RuntimeRoot) { $RuntimeRoot = Join-Path $Root "build/release-$GameVersion-$Date/web" }
if (!$Destination) { $Destination = Join-Path $Root "build/author-kit-$GameVersion-$Date/EW4-Mod-Author-$GameVersion-win-x64" }
if (!$BuildRoot) { $BuildRoot = Join-Path $Root "build/author-kit-build-$GameVersion-$Date" }
$RuntimeRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$Destination = [IO.Path]::GetFullPath($Destination)
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$archivePath = $Destination + '.zip'
foreach ($path in @($Destination, $BuildRoot, $archivePath)) {
    if (Test-Path -LiteralPath $path) { throw ('Preserved existing build/output: ' + $path) }
}
$utf8 = New-Object Text.UTF8Encoding($false)
$utf8Bom = New-Object Text.UTF8Encoding($true)
function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 30), $utf8)
}
function Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Copy-Tree([string]$Source, [string]$Target) {
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $Target -Recurse -Force
    }
}

# Pin the game libraries and CLI sources/documentation to one published release.
& git -C $Root diff --quiet $SourceRef -- 'rewrite/Ew4.ModTool/*.cs'
if ($LASTEXITCODE -ne 0) { throw 'CLI source differs from the requested release; use a matching checkout.' }
$releaseInfo = Get-Content -LiteralPath (Join-Path $RuntimeRoot 'release-info.json') -Raw | ConvertFrom-Json
if ($releaseInfo.version -ne $GameVersion -or $releaseInfo.source -ne (& git -C $Root rev-parse $SourceRef).Trim()) { throw 'Author package runtime version/source mismatch.' }
New-Item -ItemType Directory -Path $BuildRoot -Force | Out-Null
& dotnet publish (Join-Path $Root 'rewrite/Ew4.ModTool/Ew4.ModTool.csproj') `
    -c Release -r win-x64 --self-contained true `
    ('-p:Ew4RuntimeRoot=' + $RuntimeRoot) `
    --artifacts-path (Join-Path $BuildRoot 'compiler') `
    -o $Destination --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Author CLI publish failed.' }

$libraries = @('Ew4.Mods', 'Ew4.Client', 'Ew4.Core', 'Ew4.Legacy', 'Ew4.Persistence',
    'MoonSharp.Interpreter', 'AssetRipper.TextureDecoder', 'StbImageSharp', 'StbImageWriteSharp')
$runtimeHashes = @()
foreach ($library in $libraries) {
    $sourceLibrary = Join-Path $RuntimeRoot ($library + '.dll')
    $targetLibrary = Join-Path $Destination ($library + '.dll')
    if ((Sha256 $sourceLibrary) -ne (Sha256 $targetLibrary)) { throw ('Published library mismatch: ' + $library) }
    $runtimeHashes += [ordered]@{ file = $library + '.dll'; sha256 = Sha256 $targetLibrary }
}
Copy-Tree (Join-Path $RuntimeRoot 'data') (Join-Path $Destination 'data')
foreach ($name in @('conquest-pages.json', 'city-names.json')) {
    Copy-Item -LiteralPath (Join-Path $RuntimeRoot ('wwwroot/' + $name)) -Destination (Join-Path $Destination ('data/frontend/' + $name)) -Force
}

& git -C $Root archive --format=zip ('--output=' + (Join-Path $BuildRoot 'reference.zip')) $SourceRef rewrite/mod-kit
if ($LASTEXITCODE -ne 0) { throw 'Cannot snapshot matching documentation.' }
Expand-Archive -LiteralPath (Join-Path $BuildRoot 'reference.zip') -DestinationPath (Join-Path $BuildRoot 'reference-source')
Copy-Tree (Join-Path $BuildRoot 'reference-source/rewrite/mod-kit') (Join-Path $Destination 'reference')
Copy-Tree (Join-Path $Root 'tools/author-kit') $Destination
$readmePath = Join-Path $Destination 'README.zh-CN.md'
[IO.File]::WriteAllText($readmePath, ([IO.File]::ReadAllText($readmePath).Replace('0.2.34', $GameVersion)), $utf8)
$launcherSource = [IO.File]::ReadAllText((Join-Path $Destination 'author.ps1'), [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $Destination 'author.ps1'), ($launcherSource -replace "`r?`n", "`r`n"), $utf8Bom)
foreach ($cmd in Get-ChildItem -LiteralPath $Destination -File -Filter '*.cmd') {
    $cmdSource = [IO.File]::ReadAllText($cmd.FullName)
    [IO.File]::WriteAllText($cmd.FullName, ($cmdSource -replace "`r?`n", "`r`n"), [Text.Encoding]::ASCII)
}

$project = Join-Path $Destination 'project'
New-Item -ItemType Directory -Path (Join-Path $project 'rewrite') -Force | Out-Null
Write-Json (Join-Path $project 'author-project.json') ([ordered]@{ id = 'my-mod'; name = '我的Mod'; version = '1.0.0' })
Write-Json (Join-Path $project 'rewrite/extension.json') ([ordered]@{ schema = 1; apiVersion = 3 })
New-Item -ItemType Directory -Path (Join-Path $Destination 'exports') -Force | Out-Null
$sourceTarget = Join-Path $Destination 'source/Ew4.ModTool'
New-Item -ItemType Directory -Path $sourceTarget -Force | Out-Null
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $Root 'rewrite/Ew4.ModTool') -File -Filter '*.cs') {
    Copy-Item -LiteralPath $source.FullName -Destination $sourceTarget
}
Copy-Item -LiteralPath (Join-Path $Root 'rewrite/Ew4.ModTool/Ew4.ModTool.csproj') -Destination $sourceTarget

$exampleReceipt = $null
if ($ExamplePackage) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $ExamplePackage = (Resolve-Path -LiteralPath $ExamplePackage).Path
    $exampleRoot = Join-Path $Destination 'examples/needs-repair/optimized-2.0.2'
    $exampleProject = Join-Path $exampleRoot 'project'
    New-Item -ItemType Directory -Path $exampleProject -Force | Out-Null
    Copy-Item -LiteralPath $ExamplePackage -Destination (Join-Path $exampleRoot 'original.ew4mod')
    $zip = [IO.Compression.ZipFile]::OpenRead($ExamplePackage)
    try {
        $reader = New-Object IO.StreamReader($zip.GetEntry('mod.json').Open(), [Text.Encoding]::UTF8)
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        foreach ($entry in $manifest.files.PSObject.Properties) {
            $target = [IO.Path]::GetFullPath((Join-Path $exampleProject $entry.Name))
            if (!$target.StartsWith($exampleProject + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Example entry leaves project directory.' }
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($entry.Name), $target, $false)
            if ((Sha256 $target) -ne $entry.Value) { throw ('Example resource hash mismatch: ' + $entry.Name) }
        }
        Write-Json (Join-Path $exampleProject 'author-project.json') ([ordered]@{ id = $manifest.id; name = $manifest.name; version = $manifest.version })
        $known = Get-Content -LiteralPath (Join-Path $Root 'reverse/reports/mobile-optimized-mod-inspection-20261109.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Json (Join-Path $exampleRoot 'known-issues.json') ([ordered]@{
            expectedGameVersion = '0.2.34'; originalSha256 = Sha256 $ExamplePackage;
            originalLogicRevision = $manifest.logicRevision; recomputedLogicRevision034 = $known.logicRevisionActual034;
            invalidDamageMultiplierCount = $known.invalidDamageMultiplierCount;
            invalidDamageMultipliers = $known.invalidDamageMultipliers
        })
        $exampleText = @'
# 待作者修复的原始示例

original.ew4mod 为本次手机用户原包，保留原字节。project/ 为其中7个覆盖资源的可编辑副本，未改变数值，另附作者配置文件。

这个示例不是已修复成品。27条轻步兵 damage_multipliers 写成了数组，但接口要求以目标类别为键的对象。五个倍率对应何种类别，需由作者按原设计确认，不能直接猜顺序。

将 project 文件夹拖到作者包根目录的 01-check-project.cmd，即可看到27条具体定位。修好后再拖到 02-build-mod.cmd，让工具生成清单、完整校验并导出。known-issues.json 保存每个错误条目的索引、ID与原数组。

原始清单的规则指纹与0.2.34实际计算结果不一致；内容修好后由官方工具重生成，不要只手动替换哈希。第一位用户的游戏版本尚未确认；本作者包的目标版本固定为0.2.34。
'@
        [IO.File]::WriteAllText((Join-Path $exampleRoot 'README.zh-CN.md'), $exampleText, $utf8)
        $exampleReceipt = [ordered]@{ originalSha256 = Sha256 $ExamplePackage; resourceFiles = @($manifest.files.PSObject.Properties).Count; repaired = $false }
    }
    finally { $zip.Dispose() }
}

Write-Json (Join-Path $Destination 'author-kit.json') ([ordered]@{
    schema = 1; authorKitVersion = $KitVersion; gameVersion = $GameVersion; ruleApi = 3; platform = 'Windows x64'; selfContained = $true;
    updatePolicy = 'Check compatibility each game release; rebuild when base assets, rule fingerprint dependencies, Mod formats or interfaces change.';
    sourceCommit = $SourceRef; repository = 'https://github.com/aztksilence111/ew4-rewrite';
    baseRevision = '57c4aa3fb9627dcfb201bb168e5d1897679d0143439fa4d46e8ad58858b8426c';
    runtimeLibraries = $runtimeHashes; needsRepairExample = $exampleReceipt
})
$immutable = @(Get-ChildItem -LiteralPath $Destination -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($Destination.Length + 1).Replace('\', '/')
    $relative -notmatch '^(project|exports|_work)/' -and $relative -notmatch '^examples/needs-repair/[^/]+/project/'
} | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($Destination.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = Sha256 $_.FullName }
})
Write-Json (Join-Path $Destination 'files.sha256.json') ([ordered]@{ schema = 1; files = $immutable })
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($Destination, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $true)
$result = [ordered]@{ gameVersion = $GameVersion; authorKitVersion = $KitVersion; kit = $Destination; zip = $archivePath;
    zipBytes = (Get-Item -LiteralPath $archivePath).Length; zipSha256 = Sha256 $archivePath;
    immutableFiles = $immutable.Count; runtimeLibrariesMatched = $runtimeHashes.Count; example = $exampleReceipt }
Write-Json (Join-Path (Split-Path $Destination -Parent) 'build-summary.json') $result
[IO.File]::WriteAllText(($archivePath + '.sha256'), ((Sha256 $archivePath) + '  ' + [IO.Path]::GetFileName($archivePath) + "`n"), $utf8)
$result | ConvertTo-Json -Depth 5
