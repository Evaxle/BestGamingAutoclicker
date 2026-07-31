#!/usr/bin/env bash
# Build AutoClicker.exe + ac_engine.dll into ./bin using the MinGW-w64
# cross compiler (run this on a Linux PC that has g++-mingw-w64 installed).
#
#   ./build.sh                # uses x86_64-w64-mingw32-g++
#   CXX=g++ ./build.sh        # override compiler (e.g. on Windows with MinGW)

set -euo pipefail

CXX="${CXX:-x86_64-w64-mingw32-g++}"
FLAGS="-O2 -Wall -std=c++17 -I."

mkdir -p bin

echo "==> Building ac_engine.dll ..."
"$CXX" $FLAGS -DAC_BUILD_DLL -shared -o bin/ac_engine.dll \
    ac_engine/ac_engine.cpp ac_engine/dllmain.cpp \
    -static -static-libgcc -static-libstdc++

echo "==> Building AutoClicker.exe ..."
"$CXX" $FLAGS -mwindows -static -static-libgcc -static-libstdc++ \
    -o bin/AutoClicker.exe AutoClicker.cpp -lcomctl32

echo ""
echo "Build complete. Output files:"
ls -la bin/

