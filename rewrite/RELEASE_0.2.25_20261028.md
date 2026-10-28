# 0.2.25 - Diplomacy, save compatibility and turn performance

2026-10-28. Android versionCode 28; rule API 3.

This release includes the current updates since 0.2.24 on Android ARM64 and Windows x64 Web.

- Conquest diplomacy: declarations of war, surrender demands, configurable core regions and directed faction-pair modifiers. Failed demands require further military gains before retrying and cannot be rerolled by loading a save. Capital capture transfers a configurable share of the defeated faction's treasury once per faction identity. Campaign diplomacy remains disabled. Mod configuration and event documentation are included.
- Save compatibility and settlement: usable older accounts and battles can load after structural, checksum and referenced-content checks, with specific errors for missing content. Known equipment migrations are retained. Victory dialogue can finish after the battle ends, saving each step and awarding settlement once.
- Turn performance: reuse decision distance fields and immutable combat snapshots, reduce unnecessary path searches, and streamline faction production and client state installation. Large armies use cached, progressively smaller distant markers. Measured improvements depend on scenario and hardware; a complete 30,000-unit AI turn and physical-device performance have not been verified.
- Campaign and country menus: original full-width side panels, equal-size sliding tabs that still cover the screen edge, restored transitions and map year labels, stable descriptions and portraits, and mouse dragging of the scenario list.
- Turn feedback: confirmation before ending the turn and restored turn summaries, including army-capacity penalties, with duplicate prevention across automatic AI and save restoration.
- Military academy: sequential portrait reveal, synchronized refresh state, corrected selection and price placement, and restored candidate-slot decoration.
- Android startup: native stage and error feedback, retained failure checkpoints, and streaming resource checks to improve diagnosis and recovery. The reported Honor Play8T startup issue still requires physical-device confirmation.

All earlier gameplay, story, save-export, Mod and extended commander-star features remain included. Player accounts, saves and installed Mods are not bundled.

Android: install the ARM64 APK over the existing app using the same signing identity. Do not uninstall or clear application data. Windows: extract the complete ZIP and run `Start-Web.cmd`; preserve accounts, saves and installed Mods when migrating an existing installation.

Validation covers automated rules and integration, browser behavior, exact packaged content, signing continuity and 16 KiB alignment. Physical Android installation and execution of this release have not been verified.
