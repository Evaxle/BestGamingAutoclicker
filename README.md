# Best Gaming AutoClicker

A modern profile-based autoclicker project with a polished Python GUI and a lightweight AutoHotkey core for actual clicking behavior.

## What changed

- Added a Python-based GUI built with CustomTkinter for a modern desktop experience.
- Moved profile and settings handling into Python so the GUI manages data consistently.
- Kept the clicking logic in the AutoHotkey script, with no GUI built into the AHK file.
- Added validation for settings and profile loading.

## Requirements

### Windows
- Install Python 3.10+ from https://www.python.org/downloads/windows/
- Install AutoHotkey v2 from https://www.autohotkey.com/
- Open Command Prompt in the project folder and run:

```bat
py -3 -m pip install --upgrade pip
py -3 -m pip install -r requirements.txt
```

## Run the GUI

From the project folder you can use either of these:

```bat
py -3 gui.py
```

or simply double-click:

```bat
run_gui.bat
```

The GUI will save settings into the `data/` folder as profile files and write an active profile marker for the AHK script.

## Run the AutoHotkey script directly

Double-click the `.ahk` file or launch it from AutoHotkey on Windows.

## Validation

The Python config layer includes regression tests.

```bash
python -m unittest discover -s tests -q
```

## Notes

- The default profile is protected from deletion.
- The AHK script does not create or display a GUI; it only performs the click automation based on the Python-managed profile data.
- On non-Windows systems, launching the AHK script is simulated because AutoHotkey is Windows-only.
