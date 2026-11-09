param(
    [ValidateSet('Check', 'Build', 'KitCheck')][string]$Action = 'Check',
    [string]$Project = '',
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
$kitRoot = $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$runRoot = $null
$gameVersion = ([IO.File]::ReadAllText((Join-Path $kitRoot 'author-kit.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json).gameVersion

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Read-Json([string]$Path) {
    return ([IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json)
}
function Get-Sha256([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
function Invoke-ModTool([string]$Step, [string[]]$Arguments) {
    Write-Host $Step
    $stdout = Join-Path $runRoot ($Step + '.stdout.txt')
    $stderr = Join-Path $runRoot ($Step + '.stderr.txt')
    $toolArguments = @($Arguments) + @(
        ('--base=' + (Join-Path $kitRoot 'data')),
        ('--mods=' + (Join-Path $kitRoot '_work/mods')),
        ('--saves=' + (Join-Path $kitRoot '_work/saves'))
    )
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell treats native stderr as ErrorRecord even for progress text.
        $ErrorActionPreference = 'Continue'
        & (Join-Path $kitRoot 'Ew4.ModTool.exe') @toolArguments 1> $stdout 2> $stderr
        $toolExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($toolExit -ne 0) {
        $details = if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Raw } else { '' }
        throw ('工具校验失败，请修正配置后重试。' + [Environment]::NewLine + $details)
    }
    return (Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json)
}

try {
    if ($Action -eq 'KitCheck') {
        $manifest = Read-Json (Join-Path $kitRoot 'files.sha256.json')
        $errors = New-Object 'System.Collections.Generic.List[string]'
        foreach ($entry in $manifest.files) {
            $file = Join-Path $kitRoot $entry.path
            if (!(Test-Path -LiteralPath $file -PathType Leaf)) { $errors.Add('缺少：' + $entry.path); continue }
            if ((Get-Sha256 $file) -ne $entry.sha256) {
                $errors.Add('已修改：' + $entry.path)
            }
        }
        if ($errors.Count) { throw ($errors -join [Environment]::NewLine) }
        Write-Host ('工具与基础资源完整，共 ' + $manifest.files.Count + ' 个文件。')
        Write-Host '工程与导出目录允许自行修改，不参与这项完整性检查。'
        exit 0
    }

    if ([string]::IsNullOrWhiteSpace($Project)) { $Project = Join-Path $kitRoot 'project' }
    $projectRoot = (Resolve-Path -LiteralPath $Project).Path
    $protectedRoots = @('data', '_work', 'exports', 'reference', 'source') | ForEach-Object { Join-Path $kitRoot $_ }
    foreach ($protected in $protectedRoots) {
        if ($projectRoot -eq $protected -or $projectRoot.StartsWith($protected + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw '请选择独立工程目录，不要在基础资源、工具缓存或导出目录上打包。'
        }
    }
    if ($projectRoot -eq $kitRoot) { throw '请选择 project 或其他独立工程目录。' }
    $configuration = Join-Path $projectRoot 'author-project.json'
    if (!(Test-Path -LiteralPath $configuration -PathType Leaf)) { throw '工程缺少 author-project.json。可从 project 文件夹复制该配置后修改。' }
    $metadata = Read-Json $configuration
    if ($metadata.id -isnot [string] -or $metadata.id -notmatch '^[a-z0-9][a-z0-9_.-]{0,63}$') { throw 'author-project.json 的 id 应为1至64位小写字母、数字、点、横线或下划线。' }
    if ($metadata.name -isnot [string] -or [string]::IsNullOrWhiteSpace($metadata.name) -or $metadata.name.Length -gt 80 -or $metadata.name -match '[\x00-\x1f]') { throw '请填写有效名称，最多80字。' }
    if ($metadata.version -isnot [string] -or $metadata.version -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}$') { throw '请用数字、英文字母、点或横线填写版本，例如 1.0.0。' }
    if (@(Get-ChildItem -LiteralPath $projectRoot -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw '工程不能包含目录链接或文件链接。' }

    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
    $runRoot = Join-Path $kitRoot ('_work/logs/' + $runId)
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    Write-Host ('目标游戏版本：' + $gameVersion + '；规则 API 3（兼容 API 1/2 配置）')
    Write-Host ('工程：' + $projectRoot)
    $issues = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Filter '*.json') {
        if ($file.FullName -eq (Join-Path $projectRoot 'mod.json')) { continue }
        try { $null = Read-Json $file.FullName }
        catch { $issues.Add($file.Name + '：JSON语法错误。' + $_.Exception.Message) }
    }
    $unitsPath = Join-Path $projectRoot 'legacy/units.json'
    if (Test-Path -LiteralPath $unitsPath) {
        try {
            $units = Read-Json $unitsPath
            foreach ($unit in $units.units) {
                if ($unit.PSObject.Properties.Name -contains 'damage_multipliers' -and
                    $unit.damage_multipliers -isnot [System.Management.Automation.PSCustomObject]) {
                    $issues.Add('legacy/units.json：' + $unit.id + ' 的 damage_multipliers 必须为类别键对象，不能用数组。')
                }
            }
        }
        catch { $issues.Add('legacy/units.json 无法读取：' + $_.Exception.Message) }
    }
    if ($issues.Count) {
        Write-Utf8 (Join-Path $runRoot 'validation-errors.txt') ($issues -join [Environment]::NewLine)
        foreach ($issue in $issues) { Write-Host $issue }
        throw ('预检查发现 ' + $issues.Count + ' 项错误，未生成清单或导出包。')
    }

    $existingManifest = Join-Path $projectRoot 'mod.json'
    if (Test-Path -LiteralPath $existingManifest) {
        Copy-Item -LiteralPath $existingManifest -Destination (Join-Path $runRoot 'previous-mod.json')
    }
    $packed = Invoke-ModTool '01-pack' @('pack', $projectRoot, ('--id=' + $metadata.id), ('--name=' + $metadata.name), ('--version=' + $metadata.version))
    $verified = Invoke-ModTool '02-verify' @('verify', $projectRoot)
    if ($packed.logicRevision -ne $verified.logicRevision) { throw '生成与校验的指纹不一致，请检查工程是否正在被其他程序修改。' }
    if ($Action -eq 'Check') {
        Write-Host '清单已生成，完整校验通过。'
        Write-Host ('规则指纹：' + $verified.logicRevision)
        Write-Utf8 (Join-Path $runRoot 'result.json') (@{ ok = $true; action = $Action; project = $projectRoot; logicRevision = $verified.logicRevision } | ConvertTo-Json)
        exit 0
    }

    $imported = Invoke-ModTool '03-import' @('import', $projectRoot)
    $outputRoot = Join-Path $kitRoot 'exports'
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $outputFile = Join-Path $outputRoot ($metadata.id + '-' + $metadata.version + '-' + $runId + '.ew4mod')
    $exported = Invoke-ModTool '04-export' @('export', $imported.key, $outputFile)
    if (!(Test-Path -LiteralPath $outputFile -PathType Leaf) -or $exported.exported -ne $outputFile) { throw '导出未完成。' }
    $sha256 = Get-Sha256 $outputFile
    $result = @{ ok = $true; action = $Action; project = $projectRoot; gameVersion = $gameVersion; file = $outputFile; sha256 = $sha256; logicRevision = $verified.logicRevision }
    Write-Utf8 ($outputFile + '.json') ($result | ConvertTo-Json)
    Write-Utf8 (Join-Path $runRoot 'result.json') ($result | ConvertTo-Json)
    Write-Host '打包、完整校验、导入和导出全部通过。'
    Write-Host ('成品：' + $outputFile)
    Write-Host ('SHA-256：' + $sha256)
    if (!$NonInteractive) { Start-Process explorer.exe -ArgumentList ('/select,"' + $outputFile + '"') }
    exit 0
}
catch {
    $message = $_.Exception.Message
    if ($runRoot) {
        Write-Utf8 (Join-Path $runRoot 'result.json') (@{ ok = $false; action = $Action; error = $message } | ConvertTo-Json)
        Write-Host ('详细记录：' + $runRoot)
    }
    Write-Host $message
    exit 1
}
