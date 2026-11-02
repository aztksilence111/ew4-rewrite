# 0.2.31 - R runtime and Mod interfaces

Android ARM64 (versionCode 34) and standalone Windows x64 Web. Rule API 3.

- Correct recruitment animation timing, morale presentation and input handling.
- Support per-sprite pixel ratios and downsampling for high resolution Mod models and flags.
- Add optional air/surface unit layers, units occupying both layers, composite animations and target-dependent attack visuals. Existing content keeps its original behavior.
- Add configurable engineer construction, custom buildings, costs, recruitment, income, defense and upgrades.
- Add scenario-specific building income and faction/site income events, including persistent site ownership behavior.
- Complete passive skill interfaces: apply/remove statuses, maximum health and range bonuses, food exemption, terrain movement and morale operations. JSON conditions cover unit types, terrain, buildings and distance; Lua remains optional for complex logic.
- Add configurable commander ability rows, names, icons, ordering across original and new abilities, initial values and teaching rules. Unit profiles can use a custom ability for combat stars. Account and battle saves retain full values, with scrollable layouts on desktop and mobile.
- Preserve effective attributes across combat, retaliation, AI, healing, regrouping and saves. Initial passive turn events run once; loading does not replay them.
- Include the updated Mod documentation, minimal examples and regression tools. Critical damage and red-number behavior were audited against the original implementation.

Both platforms retain save export in main-menu and paused-game settings. Exports preserve raw account, save-slot and backup bytes with version/platform/Mod/checksum information, excluding installed content and startup caches.

Use the APK to update the existing Android application, or extract the Windows ZIP and run Start-Web.cmd. Existing signing identity and application ID are retained. SHA256SUMS.txt and release-manifest.json accompany both packages.

Android package contents, signing, ARM64 libraries and 16 KiB alignment are checked. Android physical-device installation and gameplay have not been tested.
