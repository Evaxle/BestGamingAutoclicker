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

## Build (Windows or Linux)

Requires the **.NET 10 SDK** (https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd src/BestAutoClicker
dotnet restore
dotnet build -c Release
```

## Publish a single-file exe (recommended)

```bash
cd src/BestAutoClicker
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

Output: `publish/BestAutoClicker.exe` — copy that single file to any Windows 10/11 PC.
It is fully self-contained (the .NET runtime and all native libraries are
bundled inside the exe), so no .NET installation is needed on the target PC.

The exe runs with **normal user privileges — no administrator / UAC prompt
required** (`asInvoker` in the manifest). One trade-off: because the app is not
elevated, its input hooks and clicks cannot reach games that themselves run
*elevated* (as administrator). Non-elevated games work normally.

> This single-file Windows build works from a **Linux** machine too — the .NET
> SDK downloads the `win-x64` runtime packs and embeds the icon + manifest
> cross-platform. No Windows build agent required.

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
