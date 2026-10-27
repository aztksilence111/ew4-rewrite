# 0.2.23 - In-game AI task dropdown

2026-10-27. Android versionCode 26; rule API 3.

The AI task type selector now opens a dropdown directly below the control on Android and Windows. Choose a task type to apply it and close the list, or tap outside to dismiss it. Tapping the control again, Escape or Android Back also closes the list. Keyboard navigation and touch selection are supported, and opening the list does not move the sidebar controls.

This fixes the Android-specific behavior of the system select control reported in 0.2.22. Gameplay rules, the original six conquest entries and the five built-in base maps are unchanged. Story playback under development is excluded, and no review Mod is distributed.

Android: install the ARM64 APK over the existing app. Windows: extract the complete ZIP and run `Start-Web.cmd`. Preserve your accounts and saves when migrating Windows installations. Both packages come from the same committed source; the Android signing identity is retained.

The dropdown is verified with mouse, keyboard and emulated mobile touch, with exact frontend checks in the signed APK. Physical Android installation and execution have not been verified.
