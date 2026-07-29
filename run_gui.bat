@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if errorlevel 1 (
    echo Python not found. Install Python 3.10+ and ensure 'py' is on PATH.
    exit /b 1
)

py -3 -m pip install -r requirements.txt
if errorlevel 1 exit /b %errorlevel%

py -3 gui.py
exit /b %errorlevel%
