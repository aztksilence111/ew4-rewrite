param(
    [string]$Apk = '',
    [string]$OutputDirectory = '',
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
$kitRoot = $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding($false)
$temporaryFiles = @()
function Hash-File([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $hasher.Dispose() }
}
function Invoke-SigningTool([string]$Tool, [string[]]$ToolArguments) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $Tool @ToolArguments 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { throw ("签名工具退出码 $code`n" + ($output -join "`n")) }
    return ($output -join "`n")
}
function Payload-Hashes([string]$Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    $result = @{}
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/') -or $entry.FullName -match '(?i)^META-INF/([^/]+\.(SF|RSA|DSA|EC)|MANIFEST\.MF)$') { continue }
            if ($result.ContainsKey($entry.FullName)) { throw ('APK 含重复条目：' + $entry.FullName) }
            $stream = $entry.Open()
            $hasher = [Security.Cryptography.SHA256]::Create()
            try { $result[$entry.FullName] = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose(); $hasher.Dispose() }
        }
    } finally { $archive.Dispose() }
    return $result
}
try {
    if ([string]::IsNullOrWhiteSpace($Apk)) {
        if ($NonInteractive) { throw '请把修改后的 APK 拖到 03-sign-apk.cmd 上，或使用 -Apk 指定文件。' }
        $Apk = (Read-Host '修改后的 APK 完整路径').Trim('"')
    }
    $inputApk = (Resolve-Path -LiteralPath $Apk).Path
    if ([IO.Path]::GetExtension($inputApk) -ine '.apk') { throw '输入必须是 APK 文件。' }
    $settings = Get-Content -LiteralPath (Join-Path $kitRoot 'signing/signing-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $keyFile = Join-Path $kitRoot 'signing/ew4-release-debug.keystore'
    if ((Hash-File $keyFile) -ne $settings.keystoreSha256) { throw '密钥文件与作者包登记摘要不一致，请恢复作者包原文件。' }
    $java = Join-Path $kitRoot 'signing/java/bin/java.exe'
    $zipalign = Join-Path $kitRoot 'signing/build-tools/zipalign.exe'
    $jar = Join-Path $kitRoot 'signing/build-tools/lib/apksigner.jar'
    foreach ($file in @($java, $zipalign, $jar)) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw ('签名组件缺失，请完整解压作者包：' + $file) }
    }
    $inputHash = Hash-File $inputApk
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $before = Payload-Hashes $inputApk
    if (!$before.ContainsKey('AndroidManifest.xml')) { throw '输入缺少 AndroidManifest.xml。' }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $kitRoot 'exports' }
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
    $runRoot = Join-Path $outputRoot ('apk-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $runRoot -ErrorAction Stop | Out-Null
    $aligned = Join-Path $runRoot 'aligned.tmp.apk'
    $signed = Join-Path $runRoot 'signed.tmp.apk'
    $temporaryFiles = @($aligned, $signed)
    Write-Host '正在对齐并使用 R 版原发行密钥签名……'
    $null = Invoke-SigningTool $zipalign @('-f', '-P', '16', '4', $inputApk, $aligned)
    $null = Invoke-SigningTool $java @('-jar', $jar, 'sign', '--ks', $keyFile,
        '--ks-key-alias', $settings.alias, '--ks-pass', ('pass:' + $settings.storePassword),
        '--key-pass', ('pass:' + $settings.keyPassword), '--v4-signing-enabled', 'false', '--out', $signed, $aligned)
    $verification = Invoke-SigningTool $java @('-jar', $jar, 'verify', '--verbose', '--print-certs', $signed)
    $matches = [regex]::Matches($verification, 'certificate SHA-256 digest:\s*([0-9a-fA-F]{64})')
    if ($matches.Count -ne 1 -or $matches[0].Groups[1].Value.ToLowerInvariant() -ne $settings.certificateSha256) {
        throw '输出证书不是 R 版原发行证书，未交付 APK。'
    }
    $null = Invoke-SigningTool $zipalign @('-c', '-P', '16', '4', $signed)
    $after = Payload-Hashes $signed
    if ($before.Count -ne $after.Count) { throw '重签前后有效载荷条目数不一致。' }
    foreach ($name in $before.Keys) {
        if (!$after.ContainsKey($name) -or $before[$name] -ne $after[$name]) { throw ('重签改变了内容：' + $name) }
    }
    if ((Hash-File $inputApk) -ne $inputHash) { throw '签名期间输入发生变化，请重试。' }
    $outputApk = Join-Path $runRoot ([IO.Path]::GetFileNameWithoutExtension($inputApk) + '-signed.apk')
    Move-Item -LiteralPath $signed -Destination $outputApk -ErrorAction Stop
    [IO.File]::WriteAllText((Join-Path $runRoot 'signature.txt'), $verification, $utf8)
    $receipt = [ordered]@{ schema = 1; input = $inputApk; inputSha256 = $inputHash; output = $outputApk;
        sha256 = Hash-File $outputApk; certificateSha256 = $settings.certificateSha256;
        payloadFiles = $before.Count; payloadUnchanged = $true; pageAlignmentKiB = 16; signatureVerified = $true;
        deviceInstallTested = $false }
    [IO.File]::WriteAllText((Join-Path $runRoot 'signing-result.json'), ($receipt | ConvertTo-Json -Depth 4), $utf8)
    Write-Host ('签名与 16 KiB 对齐验证通过，' + $before.Count + ' 个内容条目保持原字节。')
    Write-Host ('成品：' + $outputApk)
    Write-Host '覆盖安装还需保持包名，且 versionCode 不低于手机已安装版本。'
} catch {
    Write-Host ('失败：' + $_.Exception.Message)
    exit 1
} finally {
    foreach ($file in $temporaryFiles) { if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force } }
}
