# Makefile for AutoClicker (Windows GUI) + ac_engine.dll
#
# Default compiler is the MinGW-w64 cross compiler (used on Linux).
# Override on Windows with:  make CXX=g++  or  make CXX=mingw32-g++
#
# Usage:
#   make            -> builds bin/AutoClicker.exe and bin/ac_engine.dll
#   make clean      -> removes the bin/ output folder

# The built-in Make variables (CXX, CXXFLAGS) are already defined, so we use
# "=" (not "?=") so this project's defaults take effect. Use "make CXX=g++"
# to override, e.g. when building natively on Windows with MinGW.
CXX = x86_64-w64-mingw32-g++
CXXFLAGS = -O2 -Wall -std=c++17 -I.
MKDIR_P := mkdir -p

BIN_DIR := bin
EXE := $(BIN_DIR)/AutoClicker.exe
DLL := $(BIN_DIR)/ac_engine.dll

all: $(EXE) $(DLL)

$(BIN_DIR):
	$(MKDIR_P) $(BIN_DIR)

$(EXE): AutoClicker.cpp ac_engine/ac_engine.h | $(BIN_DIR)
	$(CXX) $(CXXFLAGS) -mwindows -static -static-libgcc -static-libstdc++ -o $@ AutoClicker.cpp -lcomctl32

$(DLL): ac_engine/ac_engine.cpp ac_engine/dllmain.cpp ac_engine/ac_engine.h | $(BIN_DIR)
	$(CXX) $(CXXFLAGS) -DAC_BUILD_DLL -shared -o $@ ac_engine/ac_engine.cpp ac_engine/dllmain.cpp -static -static-libgcc -static-libstdc++

clean:
	rm -rf $(BIN_DIR)

.PHONY: all clean

