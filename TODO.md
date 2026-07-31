# AutoClicker Debugging & Fixes — Task Tracker

## Build System
- [x] Install MinGW-w64 cross compiler
- [x] Fix root `Makefile` (`CXX ?=` -> `CXX =`, honor env override)
- [x] Fix `ac_engine/Makefile` (`CXX ?=` -> `CXX =`, honor env override)
- [x] Fix `build.bat` / `build.sh` robustness (verified both work)

## Engine (`ac_engine`)
- [x] Capture DLL module handle in `dllmain.cpp` (fixes low-level hook installation)
- [x] Use correct module handle in `SetWindowsHookExA`
- [x] Fix `IsRealMouseCurrentlyDown` reliability in Hold mode
- [x] Fix spam-mode stop "tail" (keeps clicking briefly after release)
- [x] Add safeguards (interval bounds, state resets)

## GUI (`AutoClicker.cpp`)
- [x] Fix `RelayoutWindow` so Enable button + status are never clipped
- [x] Verify every button handler (New/Delete/Save profile, Key select, Enable/Disable, Advanced toggle, radios)
- [x] Verify profile combo switching with unsaved-changes prompt
- [x] Verify key-capture timer logic
- [x] Clean `FARPROC` cast warnings with a typed helper

## Verification
- [x] Rebuild DLL + EXE cleanly
- [x] Verify DLL exports intact (`objdump`)
- [x] Review all button handlers for correctness

