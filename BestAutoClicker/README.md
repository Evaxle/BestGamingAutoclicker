# BestAutoClicker (.NET 10 / Avalonia)

A modern rewrite of the original C++ auto clicker. Built with **.NET 10** and
**Avalonia 12** (Fluent dark theme, custom animations), targeting **Windows**
for in-game use.

## Features

- **Spam mode** — click fast in-game and the clicker takes over at your speed,
  then keeps clicking while you keep clicking (grace-window stop check).
- **Hold mode** with three sub-modes:
  - **Immediate** — clicks while you hold the mouse button.
  - **Double Click** — a real double click arms it, then it clicks while held.
  - **Wait For Key** — hold a chosen key + mouse button to activate.
- **Stop-check safety** — the engine samples several times per window that you
  are *still* holding / clicking before it ever stops, so it can never keep
  clicking when you let go (and it ignores its own synthetic clicks).
- CPS + delay sliders, advanced stop-check tuning, trigger-key capture.
- **Profiles** — save/load/create/delete named settings, auto-saved on switch,
  on start, and on exit.
- Modern UI: acrylic blur window, entrance / card / panel animations, pulsing
  status indicators, gradient controls.

## Build (Windows)

Requires the **.NET 10 SDK** (https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd BestAutoClicker/src/BestAutoClicker
dotnet restore
dotnet build -c Release
```

## Publish a single-file exe (recommended)

```bash
cd BestAutoClicker/src/BestAutoClicker
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

Output: `publish/BestAutoClicker.exe` — copy that single file to any Windows 10/11 PC.

The exe is built with the manifest set to **run as administrator** (UAC prompt
on launch). This is deliberate: it guarantees the global input hooks and
`SendInput` clicks reach games that run elevated.

> Note: building the single-file Windows publish must be done on Windows (or a
> Windows build agent). On a Linux machine you can still `dotnet build` the
> project to validate compilation, but the final `win-x64` self-contained
> publish should be produced on Windows.

## Data / profiles location

Profiles are stored as JSON under:

```
%LOCALAPPDATA%\BestAutoClicker\profiles\<profile>.json
```

with `%LOCALAPPDATA%\BestAutoClicker\_meta.json` remembering the last-used
profile. A default profile is created automatically on first run.

## How the engine works

- `Core/Win32.cs` — P/Invoke for `SetWindowsHookEx`, `SendInput`,
  `GetAsyncKeyState`, etc.
- `Core/HookManager.cs` — global low-level mouse + keyboard hooks on a
  dedicated message-pump thread. Synthetic clicks carry `0xACAC1234` in
  `dwExtraInfo` and are ignored, so the app only ever sees *real* input.
- `Core/ClickerEngine.cs` — the state machine (faithful port of the C++
  engine): tracks real clicks/holds, arms on your activation, sends synthetic
  clicks via `SendInput`, and uses the stop-check sampling to idle the moment
  you release.
- `Services/ProfileService.cs` — JSON profile persistence.

## Original C++ version

The legacy `AutoClicker.cpp` / `ac_engine/` Win32 build is still in the repo
root. This .NET version is a full feature-equivalent replacement.
