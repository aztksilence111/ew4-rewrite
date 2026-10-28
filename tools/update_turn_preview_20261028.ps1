$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$preview = Join-Path $root 'rewrite/local/performance-preview'
$profile = Join-Path $preview 'profile'
$runtime = Join-Path $preview 'runtime'
$source = Join-Path $root 'rewrite/Ew4.Web/bin/Debug/net10.0'
$listener = Get-NetTCPConnection -LocalPort 5597 -State Listen | Select-Object -First 1
$previousPid = $listener.OwningProcess
$server = Get-CimInstance Win32_Process -Filter "ProcessId=$previousPid"
if (-not $server -or $server.CommandLine.Replace('\','/') -notmatch 'performance-preview/runtime/Ew4.Web.dll' -or $server.CommandLine -notmatch ':5597') { throw 'Unexpected preview process' }
$state = Invoke-RestMethod 'http://127.0.0.1:5597/api/action?q=%7B%22op%22%3A%22state%22%7D'
if ($state.ok) {
    $slot = Get-Content -LiteralPath (Join-Path $profile 'saves/mode-2/slot-0.json') -Raw | ConvertFrom-Json
    $saved = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($slot.session)) | ConvertFrom-Json
    if ($saved.payload.revision -ne $state.data.revision) { throw 'Preview has unsaved actions; preserve them before restarting' }
}
$protected = @(Get-ChildItem -LiteralPath $profile -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/]startup-assets[\\/]' })
$hashes = @($protected | ForEach-Object { [pscustomobject]@{path=$_.FullName;hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
$process = Get-Process -Id $previousPid
$process.Kill(); $process.WaitForExit()
$files = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in '.dll','.json' })
foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $runtime $file.Name) -Force }
$mismatch = @($files | Where-Object { (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $runtime $_.Name)).Hash })
if ($mismatch.Count) { throw 'Runtime copy differs' }
$arguments = @((Join-Path $runtime 'Ew4.Web.dll'), '--root', $root, '--contentRoot', (Join-Path $root 'rewrite/Ew4.Web'), '--save-root', $profile, '--urls', 'http://127.0.0.1:5597', '--Logging:LogLevel:Default', 'Warning')
$next = Start-Process dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtime 'live-out.log') -RedirectStandardError (Join-Path $runtime 'live-error.log')
$ready = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    try { $ready = (Invoke-WebRequest 'http://127.0.0.1:5597' -UseBasicParsing).StatusCode -eq 200; if ($ready) { break } } catch { Start-Sleep -Milliseconds 500 }
}
if (-not $ready) { throw 'Preview did not start' }
$changed = @($hashes | Where-Object { (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.hash })
if ($changed.Count) { throw 'Protected data changed' }
$receipt = [ordered]@{ date='2026-10-28'; port=5597; previousPid=$previousPid; pid=$next.Id; runtimeFiles=$files.Count; runtimeMismatch=$mismatch.Count; protectedFiles=$hashes.Count; protectedChanges=$changed.Count; savedRevision=$saved.payload.revision; http=200 }
$directory = Join-Path $root 'build/maintenance-20261028'
New-Item -ItemType Directory -Force $directory | Out-Null
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'turn-feedback-preview.json') -Encoding utf8
$receipt | ConvertTo-Json -Compress
