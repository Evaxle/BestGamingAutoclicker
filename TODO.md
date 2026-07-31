# AutoClicker Debugging & Fixes — Task Tracker

## Build System
- [x] Install MinGW-w64 cross compiler
- [x] Fix root `Makefile` (`CXX ?=` -> `CXX =`, honor env override)
- [x] Fix `ac_engine/Makefile` (`CXX ?=` -> `CXX =`, honor env override)
- [x] Fix `build.bat` / `build.sh` robustness (verified both work)

## Engine (`ac_engine`)
- [x] Capture DLL module handle in `dllmain.cpp` (fixes low-level hook installation)
- [x] Use correct module handle in `SetWindowsHookExA`
- [x] Fix `IsRealMouseCurrentlyDown` reliability in Hold mode (+ GetAsyncKeyState fallback)
- [x] **Fix spam mode instantly stopping**: release check now uses last-real-click-time grace period instead of the decaying CPS rate (the clicker kept stopping ~150ms after starting because the user stops clicking once the autoclicker takes over, dropping the real-click CPS to ~0)
- [x] Add safeguards (interval bounds, state resets)

## GUI (`AutoClicker.cpp`)
- [x] Fix `RelayoutWindow` so Enable button + status are never clipped
- [x] **Fix "New Profile" dialog OK button doing nothing**: BN_CLICKED is delivered via SendMessage directly to the dialog proc (never in the message queue), so the old queue-scan loop swallowed it. Rewrote as a subclassed dialog (PromptDlgProc) that handles WM_COMMAND/IDOK/IDCANCEL/WM_CLOSE properly, plus DM_SETDEFID so Enter activates OK.
- [x] **Fix submode-specific fields not hiding**: added `SyncModeRadiosAndVisibility()` which force-syncs all radio states from settings and refreshes visibility, so switching Immediate/Double Click/Wait For Button correctly shows/hides the Double Click interval and Trigger Key fields.
- [x] Verify every button handler (New/Delete/Save profile, Key select, Enable/Disable, Advanced toggle, radios)
- [x] Verify profile combo switching with unsaved-changes prompt
- [x] Verify key-capture timer logic
- [x] Clean `FARPROC` cast warnings with a typed helper

## Verification
- [x] Rebuild DLL + EXE cleanly
- [x] Verify DLL exports intact (`objdump`)
- [x] Strict `-Wall -Wextra` compile produces zero warnings
- [x] Review all button handlers for correctness

