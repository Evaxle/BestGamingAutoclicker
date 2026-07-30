# Fix: Replace pyautogui with DLL-based clicking

## Steps

- [x] 1. Analyze all files (gui.py, dll_wrapper.py, autoclicker.cpp/h, autoclicker_config.py)
- [x] 2. Fix `gui.py` `_emit_click()` — replace pyautogui with pynput.mouse.Controller.click()
- [x] 3. Fix `dll_wrapper.py` `argtypes` — use `POINTER(ClickerConfig)` / `POINTER(ClickerInputState)` for pointer params
- [x] 4. Remove all pyautogui references from gui.py (comments, error messages)
- [x] 5. Test the DLL loading path and error messages

