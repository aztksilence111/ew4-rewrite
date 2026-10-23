# Mate 30 Pro / Chromium 70 compatibility

## Recovered Plan

The relevant chat is `规划设备显示兼容方案`, id
`01a16776-1172-7072-bbdc-29cbc375c6fc`, dated 2026-10-23.
Its user supplied device/effect screenshots end in
`90dc5125e1b5ee5e297b5cdf90b445d0.jpg` and
`baf0cef1479d7dd163cf0797488e9cdb.jpg`.
The device is Huawei Mate 30 Pro, Android 10, Kirin 990, 8 GB RAM;
the game diagnostic identifies Chromium 70. Android version alone is not the
WebView version. The earlier `ANDROID10_STARTUP_20261021.md` did not yet have
this confirmed engine version, and its desktop API-removal tests were not an
actual Chromium 70 run.

That chat requested analysis only and left no implementation report. Its plan:
shared source + Chrome 70 transpilation; the same outputs in Web and APK;
runtime API, event-listener and CSS fallbacks; keep Canvas independent from
Pixi; validate the old engine and eventually the actual phone. This task
implements that plan against the current shared worktree, which already has
PixiJS 7.4.3 and concurrent screen/automatic-turn/commander fixes.

## Implementation

- `tools/frontend/` pins esbuild, Acorn, PostCSS and the CDP test dependency.
  `npm --prefix tools/frontend ci --no-audit --no-fund` installs the tools.
  `rewrite/Frontend.Build.targets` invokes the build from Web/Android builds,
  including direct Android Compile. Missing tools fail with the install command.
- Every root/vendor JS is lowered to Chrome 70 and ES2018; Acorn checks the
  output. CSS inset expands to physical sides, and image URLs account for the
  generated `wwwroot/compat/` directory. Source/output SHA-256 pairs go into
  `compat/manifest.json`. Generated files are ignored by Git.
- ModStore maps HTML JS/CSS references to these outputs for both hosts and keeps
  content revisions. Web middleware also transforms nested compat CSS/JS.
  Android assets are mapped explicitly from source names at project evaluation,
  avoiding wildcard metadata batching that the initial package audit caught.
- The generated startup declares target 70; only this build lowers the startup
  threshold. Missing globalThis/Object.fromEntries/Array.at/replaceChildren get
  fallbacks. Object.fromEntries safely creates an own `__proto__` property.
  Old-engine Image.decode rejection is accepted only when the image actually
  loaded (complete + naturalWidth); genuine resource failures still fail.
- Tooltips explicitly remove their document event listeners; no reliance on
  old engines honoring addEventListener signal. CSS feature detection handles
  flex gap, unsupported selectors, popup stacking, min/max sizing, portrait
  menus, and the old engine's stretched text canvas. The text renderer records
  its intended CSS height for the fallback.
- Pixi remains the default when WebGL initializes. Unsupported/failed WebGL
  uses the existing Canvas draw path with begin/end adapters. A failed GPU
  initialization that acquired the original context replaces the canvas and
  rebinds map input. `?renderer=canvas` is a reproducible comparison path.
  Rules, RNG, saves, camera and scene evaluation remain shared.

## Validation

Evidence is under `build/android70-20261023/`.

- The official Chromium Windows snapshot r579032 actually reports
  **HeadlessChrome/70.0.3508.0**. Archive SHA-256:
  `7510fd93fc3674253505f3609eb63e2a2117b78341b32b4731882b8924a46158`.
  Source: `https://storage.googleapis.com/chromium-browser-snapshots/Win_x64/579032/chrome-win32.zip`.
  This is an actual old engine, without User-Agent substitution.
- Published-directory Chromium 70: **79 checks**, **0 uncaught errors** in
  `published-chromium70/results.json`. Five cases cover landscape, portrait
  Canvas, desktop, absent WebGL, and GPU initialization failure after context
  acquisition. Each checks button text containment, settings, Mod manager,
  headquarters, conquest, nonblank rendered pixels, zoom, CDP touch drag,
  save/load and pause. Screenshots were visually reviewed; the first review
  caught displaced menu labels and the corrected screenshots were rechecked.
- Current Chromium/Edge 155: **79 checks**, errors 0 in the same five cases
  (`published-modern/results.json`).
- Existing Pixi regression: three viewports, near/far views, WebGL 1 case,
  movement, cache budgets, tint/blend comparison and context recovery passed,
  errors 0 (`pixi-regression/results.json`).
- Startup/API-removal/fault diagnostics: 16 checks in five current-build cases
  passed (`startup-diagnostics/browser-results.json`). The optional old 0.2.6
  baseline is skipped because its old font-index contract no longer matches
  current content; it is not counted as a successful baseline reproduction.
- Mod checks: **106**, errors 0. Web publish and full ARM64 Android build passed
  with 0 warnings/errors. JS/Python syntax checks and diff whitespace checks pass.
- APK CRC, all 31 generated assets and their source hashes, raw frontend,
  runtime content and assembly blob match (`android-package.json`, errors 0).
  v2/v3 signature and 16 KiB alignment pass. Certificate SHA-256 is unchanged:
  `82e8ba69b415d50193f7a6c2fd95d9d67f39d396bc321c1f0b03cf4bfb7c9df1`.

## Local Package And Remaining Work

Local test APK: `build/android70-20261023/ew4-rewrite-0.2.16-chromium70-test-arm64.apk`.
Size: 144151959 bytes. SHA-256:
`524fff0da308e5fe408981cd5479f0571eabeb4450f8dada7e91995f5b06faaa`.
The final package explicitly sets `RuntimeIdentifiers=android-arm64` and its
manifest reports only `arm64-v8a`; the intermediate dual-ABI build is not the
delivered test package.
The project version remains 0.2.16/code19; this is a local compatibility test
build, not a new release. It includes the current shared worktree changes.
No installation, remote upload, player save manipulation or Git commit occurred.
Clean published-directory preview: `http://127.0.0.1:5485`, with its separate
`build/android70-20261023/preview-profile`. Test servers 5483/5484 were stopped;
other chats' previews were preserved.

The Mate 30 Pro itself has not been connected or tested. Desktop Chromium 70
does not certify Huawei's WebView integration, GPU/driver behavior, physical
touch, audio, background recovery or sustained memory/thermal behavior.
Next: cover-install the same-signature test APK on the phone, check startup,
menus, conquest, touch/zoom, battle/effects, save/load and background recovery;
record the selected renderer and startup diagnostics for any failure. Keep
Canvas fallback available during future Pixi work.
