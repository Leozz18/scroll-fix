# Changelog

All notable changes to Scroll Fix. Format follows [Keep a Changelog](https://keepachangelog.com/).

## [1.1.1] - 2026-09-11

### Fixed
- **System-wide input stutter and unresponsive tray menu in v1.1.0.** The reversal replay called `SendInput` from inside the low-level hook callback. With real mouse input this deadlocks inside win32k: the raw input thread waits for the hook callback while the callback waits for the raw input thread. Every mouse event then hit the hook timeout (jerky cursor, stutter), the UI thread hung (tray menu would not open), any process calling `SendInput` hung, and the process could not be killed until reboot.
  - The hook now runs on a dedicated high-priority thread with its own message loop, so UI work can never delay it.
  - Replays are queued and sent from a separate worker thread, in order. The hook callback never calls `SendInput`.
- If you ran v1.1.0: quit it, **reboot** (a stuck v1.1.0 thread survives until then), then start v1.1.1.

## [1.1.0] - 2026-09-11

### Added
- **Balanced mode** (new default): the first opposite notch is held; if the next notch confirms the reversal, it passes and the held notch is replayed. Rapid up/down/up/down scrolling now works, and no intentional notch is lost. Fixes the main complaint from the first release.
- **Presets** in Settings: Balanced, Quick reverse (160 ms), Worn encoder (Strict, 260 ms).
- **Pause for 10 minutes** in the tray menu, auto-resumes.
- Tray menu shows version and links to GitHub for bug reports.
- Application icon; the exe now has a proper icon in Explorer and the taskbar.
- Two release assets: small framework-dependent `ScrollFix.exe` and `ScrollFix-standalone.exe` (no .NET needed), plus `SHA256SUMS.txt`.
- GitHub Actions workflow: tests on every push, release assets built and attached on `v*` tags.

### Changed
- The old *Aggressive* behaviour is now called **Strict** and is opt-in. Existing settings files are migrated to Balanced.
- Settings window rebuilt with DPI-aware layout, mode descriptions and inline validation.
- Ghost counter and settings persistence moved off the hook thread to a UI timer, so the low-level hook can never stall or be dropped by Windows.
- Settings are written atomically (temp file + move).
- Self-contained build is compressed (~70 MB instead of ~154 MB).

### Removed
- `MaxGhostNotches` setting (unused by the new algorithm; ignored if present in old files).

### Fixed
- Intentional reversals immediately after fast scrolling were blocked until the user paused.
- Replayed notches are tagged and never re-filtered, so no feedback loop is possible.

## [1.0.1] - 2026-09-07

### Changed
- Aggressive filtering distinguishes tightly clustered ghost pulses from reversals after a brief pause.

## [1.0.0] - 2026-08-10

### Added
- Initial release: global wheel hook, aggressive ghost filter, tray icon, settings, autostart.
