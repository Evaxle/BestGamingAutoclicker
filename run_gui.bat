@echo off
setlocal
cd /d "%~dp0"

echo.
echo ============================================
echo    Best Gaming AutoClicker - Purple Edition
echo    Profile-based autoclicking toolkit
echo ============================================
echo.

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

echo [ERROR] Python was not found. Install Python 3.10+ from https://www.python.org/downloads/
echo        Make sure "python" or "py" is available on your PATH.
pause
exit /b 1

:check_deps
echo [INFO] Checking dependencies...
%PY_EXE% %PY_ARGS% -c "import customtkinter, pynput, pyautogui" >nul 2>&1
if errorlevel 1 (
    echo [INFO] Installing Python requirements...
    %PY_EXE% %PY_ARGS% -m pip install --disable-pip-version-check -r requirements.txt
    if errorlevel 1 (
        echo [ERROR] Failed to install requirements.
        echo        Try running: %PY_EXE% %PY_ARGS% -m pip install -r requirements.txt
        pause
        exit /b 1
    )
    echo [INFO] Dependencies installed successfully.
)

echo [INFO] Launching AutoClicker...
echo.

where py >nul 2>nul
if not errorlevel 1 (
    py -3w gui.py
    if errorlevel 1 (
        py -3 gui.py
    )
) else (
    python gui.py
)

if errorlevel 1 (
    echo.
    echo [WARNING] The GUI closed with an error. Check the output above.
    pause
)

exit /b %errorlevel%
