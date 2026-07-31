# Building AutoClicker

The project is split into two parts:

- `AutoClicker.exe` — Windows GUI frontend (loads `ac_engine.dll` at runtime)
- `ac_engine.dll` — clicking engine (mouse/keyboard hooks + clicker loops)

## Output

Both files are produced into the `bin/` folder:

```
bin/AutoClicker.exe
bin/ac_engine.dll
```

Copy the **entire `bin/` folder** to a Windows PC. Both files must stay in the
same folder (the exe loads the dll via `LoadLibrary` at startup).

---

## Option 1 — Build on a Linux PC (cross-compile with MinGW-w64)

1. Install the MinGW-w64 cross compiler (Debian/Ubuntu):

   ```bash
   sudo apt install g++-mingw-w64-x86-64
   ```

2. Build:

   ```bash
   chmod +x build.sh
   ./build.sh
   ```

   or:

   ```bash
   make
   ```

3. The `bin/` folder now contains `AutoClicker.exe` + `ac_engine.dll`.
   Copy `bin/` to a Windows PC and run `AutoClicker.exe`.

## Option 2 — Build on a Windows PC with MinGW

Requires MinGW (with `g++` on the PATH, e.g. from mingw-w64 or MSYS2).

```bat
build.bat
```

or:

```
mingw32-make
```

## Manual commands (Linux cross-compile)

```bash
mkdir -p bin

# 1) Build the engine DLL
x86_64-w64-mingw32-g++ -O2 -Wall -std=c++17 -I. -DAC_BUILD_DLL -shared \
    -o bin/ac_engine.dll ac_engine/ac_engine.cpp ac_engine/dllmain.cpp \
    -static -static-libgcc -static-libstdc++

# 2) Build the GUI exe
x86_64-w64-mingw32-g++ -O2 -Wall -std=c++17 -I. -mwindows \
    -static -static-libgcc -static-libstdc++ \
    -o bin/AutoClicker.exe AutoClicker.cpp -lcomctl32
```

## Notes

- `-static-libgcc -static-libstdc++` are used so the resulting exe/dll do not
  need `libgcc_s_seh-1.dll` / `libstdc++-6.dll` on the target Windows PC.
- The exe never links directly against the dll at build time — it resolves the
  exported functions (`AC_Init`, `AC_Enable`, `AC_Disable`, `AC_SetSettings`,
  `AC_IsEnabled`, `AC_GetRunState`, `AC_Shutdown`) at runtime, so no import
  library is needed.

