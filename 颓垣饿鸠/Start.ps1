param([int]$Port = 5206)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$runtime = Join-Path $taskRoot 'runtime'
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'Ew4.Web.exe'))) { throw 'Run Build.ps1 first' }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw "Port $Port is occupied; choose another -Port" }
$arguments = @('--urls', "http://127.0.0.1:$Port", '--data-root', (Join-Path $runtime 'data'), '--save-root', (Join-Path $runtime 'saves'), '--mod-root', (Join-Path $runtime 'mods'))
$process = Start-Process -FilePath (Join-Path $runtime 'Ew4.Web.exe') -WorkingDirectory $runtime -ArgumentList ($arguments | ForEach-Object { '"' + $_ + '"' }) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRoot 'reports/server.log') -RedirectStandardError (Join-Path $taskRoot 'reports/server-error.log')
[ordered]@{ pid = $process.Id; url = "http://127.0.0.1:$Port"; runtime = $runtime } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'reports/server.json') -Encoding utf8
Write-Output "http://127.0.0.1:$Port (PID $($process.Id))"
