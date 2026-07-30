@echo off
REM ============================================================
REM  Build autoclicker.dll for Windows
REM  Requires: MinGW-w64 (g++) or MSVC (cl) on PATH
REM ============================================================
setlocal

echo.
echo Building autoclicker.dll for Windows...
echo.

REM Try MSVC first
where cl >nul 2>nul
if not errorlevel 1 (
    echo [INFO] Using MSVC compiler...
    cl /LD /O2 /Fe:autoclicker.dll autoclicker.cpp /link user32.lib
    if errorlevel 1 (
        echo [ERROR] MSVC build failed.
        goto :try_mingw
    )
    echo [SUCCESS] Built autoclicker.dll with MSVC
    goto :done
)

:try_mingw
where g++ >nul 2>nul
if not errorlevel 1 (
    echo [INFO] Using MinGW-w64 compiler...
    g++ -shared -o autoclicker.dll autoclicker.cpp -static-libgcc -static-libstdc++ -O2
    if errorlevel 1 (
        echo [ERROR] MinGW build failed.
        exit /b 1
    )
    echo [SUCCESS] Built autoclicker.dll with MinGW
    goto :done
)

echo [ERROR] No compiler found. Install MSVC Build Tools or MinGW-w64.
exit /b 1

:done
echo.
dir /b autoclicker.dll
echo.
endlocal
