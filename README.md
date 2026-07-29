# Best Gaming AutoClicker

A modern profile-based autoclicker built entirely in Python with a polished CustomTkinter interface and JSON-backed profile storage.

## What changed

- Replaced the old AutoHotkey-driven workflow with a Python-native autoclicker loop.
- Added a dropdown profile selector with create/delete actions.
- Moved profile and runtime state handling into Python JSON files under the data folder.
- Added validation, automatic default profile bootstrap, and safer profile discovery.

## Requirements

### Windows
- Install Python 3.10+ from https://www.python.org/downloads/windows/
- Open Command Prompt in the project folder and run:

```bat
py -3 -m pip install --upgrade pip
py -3 -m pip install -r requirements.txt
```

## Run the GUI

From the project folder run:

```bat
py -3 gui.py
```

The GUI saves settings into the data folder as JSON profile files and maintains runtime state there as well.

## Validation

The Python config layer includes regression tests.

```bash
python -m unittest discover -s tests -q
```

## Notes

- The default profile is protected from deletion.
- The autoclicker runs through Python using the configured profile and timing values.
- On non-Windows systems, the GUI exits gracefully if no desktop display is available.
