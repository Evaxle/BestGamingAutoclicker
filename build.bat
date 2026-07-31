@echo off
setlocal
rem Build AutoClicker.exe + ac_engine.dll into .\bin using MinGW g++.
rem Run this on a Windows PC that has MinGW (g++) on the PATH.

where g++ >nul 2>nul
if %errorlevel%==0 (
    set "CXX=g++"
) else (
    set "CXX=x86_64-w64-mingw32-g++"
)

if not exist bin mkdir bin

echo ==^> Building ac_engine.dll ...
%CXX% -O2 -Wall -std=c++17 -I. -DAC_BUILD_DLL -shared -o bin\ac_engine.dll ac_engine\ac_engine.cpp ac_engine\dllmain.cpp -static -static-libgcc -static-libstdc++
if errorlevel 1 exit /b 1

echo ==^> Building AutoClicker.exe ...
%CXX% -O2 -Wall -std=c++17 -I. -mwindows -static -static-libgcc -static-libstdc++ -o bin\AutoClicker.exe AutoClicker.cpp -lcomctl32
if errorlevel 1 exit /b 1

echo.
echo Build complete. Output in .\bin:
dir bin
endlocal

