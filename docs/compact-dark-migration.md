# Compact Dark redesign — implementation baseline

## Goal
Build **Compact Dark** with **all functions of DARKWAVE+**, using the supplied new Compact Dark reference screenshot as the visual/layout specification.

## Source artifacts
- DARKWAVE-Plus-Windows-x64.zip: contains a single compiled `DARKWAVE+.exe` (75,997,651 bytes; SHA-256 `f783f66f13af795423dd2566654f0dc334cd915970f27b633f67dadcc9bd3899`).
- Compact-Dark-Windows-x64.zip: contains a single compiled `Compact Dark.exe` (75,998,693 bytes; SHA-256 `6220d0f724a96158830d38629e90360f7a7fecda623a4daf9717a4f9b8981d73`).
- The uploaded ZIP archives contain executables, not original C# project sources. Executable internals and feature parity remain to be verified.

## Design requirements from the approved screenshot
- Black/graphite, compact car-stereo-inspired panels; warm orange highlights and illuminated outlines.
- Top track / branding / artwork area, title and timeline / progress.
- Five EQ controls: L, LM, M, HM, H.
- Transport controls and separate volume group.
- Folder, playlist and track library panes with associated action buttons.

## Mandatory functionality requirement
**Do not treat the screenshot as a complete feature inventory.** Every DARKWAVE+ feature, shortcut, playback behavior, audio-format capability, file/folder management function, playlist behavior and setting must be inventoried and maintained in Compact Dark, whether or not visibly represented in the screenshot.

## Implementation approach
1. Inventory the repository and recover editable application code from the packaged .NET executables where technically possible.
2. Establish a verified DARKWAVE+ feature and behavior checklist.
3. Implement the complete new Compact Dark interface against the original playback/library logic. Preserve behavior; redesign presentation only unless a functional correction is necessary.
4. Build Windows x64; test audio playback, seeking, EQ, volume, library operations, playlist save/load, and all other verified features.
5. Keep changes on `compact-dark-redesign`; do not overwrite `main` until verified.

## Current state
- Development branch created.
- No recovered source, code port, successful executable build, or feature-parity tests completed yet.
