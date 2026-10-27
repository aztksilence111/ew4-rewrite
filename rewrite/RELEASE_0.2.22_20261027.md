# 0.2.22 - AI multi-task control and reviewed map resources

2026-10-27. Android versionCode 25; rule API 3.

- Player AI supports independently enabled numbered tasks, persistent point defense, line attack/defense, deployment priority and saved unit assignments. Save format 8 accepts versions 4 through 7.
- Android reuses verified startup resource metadata for the same installation. Windows resource validation uses pooled streaming hashes.
- Repeated selection now cancels; dialogue waits for fonts and portraits; recruitment and pause/map controls avoid unnecessary rebuilding and brightness changes.
- Updated East Asia coastline rendering. Added Japan and Holy Roman Empire runtime map resources, full CELL indices, native terrain, city tiers, farms, ports, detailed water and curved geographic labels.
- Import the optional `three-map-review.ew4mod` attachment to enable the three reviewed maps on the second conquest page. These scenarios use placeholder factions and neutral buildings, and are not historical campaigns. The original six conquests remain available.
- Story playback under development is excluded.

Android: install the ARM64 APK over the existing app. Windows: extract the complete ZIP and run `Start-Web.cmd`. Import the optional map Mod through the in-game Mod manager on either platform. Do not copy files over a running game; preserve existing accounts and saves when migrating Windows installations.

Both packages are built from the same committed source. SHA-256 checksums cover the APK, Windows ZIP and optional Mod. Android signing identity is retained. Physical Android installation and performance testing are not part of this release verification.
