# 0.2.26 - City commander portraits, save export and display recovery

2026-10-29. Android versionCode 29; rule API 3.

This release includes the current updates since 0.2.25 on Android ARM64 and Windows x64 Web.

- Same-version replacement on 2026-10-29: keep version 0.2.26 and Android versionCode 29, and include the city commander portrait fix below. Download the current attachments again to receive it.
- Campaign shops now show the friendly commander stationed on that city tile, including scenario commanders and player-deployed commanders. Resource exchange also supports scenario commanders. The portrait shows the actual battle rank and title, opens commander details and returns to the service. Empty cities and ordinary garrisons have no commander portrait. Commercial ability above five stars shows an explicit count; existing purchase, sale and resource-exchange rules are preserved.
- Save export is a permanent feature on both platforms, available at the top of Settings from the main menu and during a paused battle. Android uses the system save dialog; Windows Web downloads a ZIP. Exports preserve the original account, save slots and backup bytes, including unreadable saves, with version, platform, frontend, Mod and SHA-256 information. Installed content and startup caches are excluded. ZIP import is not included.
- Mobile input: prevent browser page zoom and overscroll while retaining list scrolling and two-finger map zoom. Android WebView zoom controls are disabled.
- Diplomacy: match the action button artwork dimensions and alignment while preserving its touch target and Mod sizing options.
- Display recovery: redraw the battlefield once after graphics-context restoration or returning to the foreground, including when a turn dialog or pause menu covers the map.
- Defeated-faction regression coverage includes destroying the last enemy unit and capturing the last enemy city, followed by a complete AI round.

All earlier gameplay, story, Mod and extended commander-star features remain included. Player accounts, saves and installed Mods are not bundled.

Android: install the ARM64 APK over the existing app using the same signing identity. Do not uninstall or clear application data. Windows: extract the complete ZIP and run `Start-Web.cmd`; preserve accounts, saves and installed Mods when migrating an existing installation.

Validation covers automated integration, browser behavior, exact packaged content, signing continuity and 16 KiB alignment. Physical Android installation, the native save dialog and the reported Honor Play8T startup issue still require physical-device confirmation. The display fix does not establish that every reported mobile blank screen has the same cause.
