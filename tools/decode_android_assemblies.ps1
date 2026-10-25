param([Parameter(Mandatory)][string]$Codec, [Parameter(Mandatory)][string]$Folder)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Codec
foreach ($file in Get-ChildItem -LiteralPath $Folder -Filter '*.lz4') {
    $inputBytes = [IO.File]::ReadAllBytes($file.FullName)
    $size = [BitConverter]::ToInt32($inputBytes, 8)
    if ($size -lt 1 -or $size -gt 134217728) { throw 'Invalid assembly size' }
    $outputBytes = [byte[]]::new($size)
    $decoded = [K4os.Compression.LZ4.LZ4Codec]::Decode($inputBytes, 12, $inputBytes.Length - 12, $outputBytes, 0, $size)
    if ($decoded -ne $size) { throw "Assembly decompression failed: $($file.Name)" }
    [IO.File]::WriteAllBytes([IO.Path]::ChangeExtension($file.FullName, '.dll'), $outputBytes)
}
