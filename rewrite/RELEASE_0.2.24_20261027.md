# 0.2.24 - Story playback, save export and gameplay fixes

2026-10-27. Android versionCode 27; rule API 3.

This release includes all current source updates since 0.2.23 on Android ARM64 and Windows x64 Web.

- Story presentation: opening, event and ending sequences; layered characters, expressions and animation; dialogue paging, history, auto playback, speed controls, branching choices and save restoration. Choices reserve their effects while the battlefield is frozen, then apply them once playback finishes. Victory and special defeat sequences finish before settlement and rewards. The authoring interface and minimal story example are included in the Windows package; stories are enabled by scenario or Mod configuration.
- Android save export: Settings can export accounts, automatic/manual saves, backups and Mod save namespaces to a ZIP through the system file picker. Original save bytes are preserved, with per-file checksums in the export manifest.
- Map rendering: fixes queued Pixi textures being released too early and retained layers losing replacement textures, addressing the reported disappearing conquest map and reload error.
- Music: bounded recovery after media errors or timeouts, fetch/Blob fallback loading, volume-triggered recovery and foreground/background request handling.
- Campaign menus: restore the original country-choice dialog, fit chapter titles and align the load label. Selected campaign tabs slide left at their original texture size while still reaching the right screen edge. Full legacy map packages can opt out of inherited built-in map extensions.
- Mod commanders: unit, movement and commerce abilities may exceed five stars and remain intact through import and saves. The details view shows exact high-star counts. Item trading continues the original 4% per commerce star beyond five: purchase prices have a zero floor; sale recovery starts at 60% and gains four percentage points per star. Resource exchange retains its separate original formula.
- Army capacity: regular player recruitment may exceed dynamic capacity. Strictly exceeding it halves production of all three resources and doubles food consumption; the army icon and count turn red. The round indicator keeps its hourglass.
- AI emergency disbanding: conquest AI that exceeds capacity and loses morale from food shortage may disband at most one remote rear unit per faction per turn, keeping at least eight units and protecting commanders, objectives, front lines and building garrisons. Health-scaled 50% refunds match player disbanding, with no same-turn replacement recruitment.

All earlier updates, including player AI tasks, the in-game task dropdown, startup caching and the built-in East Asia/Japan/HRE base maps, remain included. Standalone review Mods and player data are not bundled. Story and other Mod examples are source examples, not automatically installed gameplay changes.

Android: install the ARM64 APK over the existing app using the same signing identity. Do not uninstall or clear application data. Windows: extract the complete ZIP and run `Start-Web.cmd`; preserve accounts, saves and installed Mods when migrating an existing installation.

Validation covers automated rules, client/Mod integration, browser behavior, exact packaged content, Android signing and 16 KiB alignment. Physical Android installation and execution of this release have not been verified.
