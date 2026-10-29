# 0.2.27 - AI planning, headquarters layouts and presentation updates

2026-10-29. Android versionCode 30; rule API 3.

Available for Android ARM64 and Windows x64 Web.

- AI planning reuses validated world snapshots and neighborhood queries, expands production choices on demand, and resumes planning across bounded requests. Pause and refresh invalidate pending work. A 30-faction, 15,000-unit development audit completed a full AI round with matching session and save results. This does not establish a faster complete round; large armies can still take substantial time.
- Mod authors can configure headquarters roster, academy and appointment layouts, custom pages, images and button commands through API 3. The author guide and a minimal headquarters-layout example are included in the Windows package.
- Startup uses the original loading artwork with the Rewrite/noncommercial watermark. Normal startup hides diagnostics; failures and interrupted startup expose device and error information. Initialization errors and late timeout responses are handled explicitly.
- Menu transitions and five original interface sounds now cover navigation, selection, return actions and dialogs.
- Turn-entry portraits show commanders actually deployed from the current player's headquarters. Resource totals fit the HUD while unit and round labels remain centered.
- Unit details show the effective attack range after skills and formations, including supported Mod parameters and reloaded saves.
- Artillery damage releases the next command without waiting for the reload/finish animation tail. Counterattacks, death, splash damage and fast-forward retain their existing synchronization.
- Permanent save export remains available in Settings from both the main menu and a paused battle on Android and Windows Web. It preserves raw account, slot and backup bytes, including unreadable saves, with version/platform/Mod/checksum metadata. Installation content and startup caches are excluded.

All earlier gameplay, extended commander-star and Mod features remain included. Player accounts, saves, installed Mods and signing keys are excluded from the downloads. Republic-era general documents in the source repository remain design proposals and do not change the shipped roster.

Android: install the ARM64 APK over the existing app; do not uninstall or clear its data. Windows: extract the complete ZIP and run `Start-Web.cmd`; preserve existing accounts, saves and installed Mods when migrating.

Validation includes automated integration and browser checks, exact packaged bytes, signing continuity and 16 KiB alignment. Physical Android installation, device-specific startup behavior, native save dialogs and long-turn performance still require device testing.
