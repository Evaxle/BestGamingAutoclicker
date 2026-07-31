# Standalone Makefile for building just ac_engine.dll (MinGW / MinGW-w64).
#
#   make            -> builds ac_engine.dll in this folder
#   make clean      -> removes the dll and object files
#
# On Linux override with:  make CXX=x86_64-w64-mingw32-g++

# The built-in Make variables (CXX, CXXFLAGS) are already defined, so we use
# "=" (not "?=") so this project's defaults take effect. Use "make CXX=g++"
# to override, e.g. when building natively on Windows with MinGW.
CXX = x86_64-w64-mingw32-g++
CXXFLAGS = -O2 -Wall -std=c++17 -I.. -DAC_BUILD_DLL

TARGET = ac_engine.dll
SRCS = ac_engine.cpp dllmain.cpp

$(TARGET): $(SRCS) ac_engine.h
	$(CXX) $(CXXFLAGS) -shared -o $@ $(SRCS) -static -static-libgcc -static-libstdc++

clean:
	rm -f $(TARGET) *.o

.PHONY: clean

