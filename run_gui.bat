@echo off
cd /d "%~dp0"

where py >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    py -3 -m pip install --upgrade pip
    py -3 -m pip install -r requirements.txt
    py -3 gui.py
) else (
    python -m pip install --upgrade pip
    python -m pip install -r requirements.txt
    python gui.py
)
