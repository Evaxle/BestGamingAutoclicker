@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if not errorlevel 1 (
    set "PY_EXE=py"
    set "PY_ARGS=-3"
    goto check_deps
)

where python >nul 2>nul
if not errorlevel 1 (
    set "PY_EXE=python"
    set "PY_ARGS="
    goto check_deps
)

echo Python was not found. Install Python 3.10+ and make sure python or py is on PATH.
exit /b 1

:check_deps
%PY_EXE% %PY_ARGS% -c "import customtkinter, pynput, pyautogui" >nul 2>&1
if errorlevel 1 (
    echo Installing Python requirements...
    %PY_EXE% %PY_ARGS% -m pip install --disable-pip-version-check -r requirements.txt
    if errorlevel 1 (
        echo Failed to install requirements. Please run: %PY_EXE% %PY_ARGS% -m pip install -r requirements.txt
        exit /b 1
    )
)

where py >nul 2>nul
if not errorlevel 1 (
    py -3w gui.py
    if errorlevel 1 (
        py -3 gui.py
    )
) else (
    python gui.py
)

exit /b %errorlevel%