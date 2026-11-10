<div align="center">
  <img src="dist/icon.png" alt="Hide Any Apps" width="128">

  # Hide Any Apps

  Auto-hide any normal Windows app. No taskbar icon. No Alt-Tab entry.

  [![Latest release](https://img.shields.io/github/v/release/ziyyhhk/Hide-Any-Apps?style=flat-square)](https://github.com/ziyyhhk/Hide-Any-Apps/releases/latest)
  [![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square)](https://github.com/ziyyhhk/Hide-Any-Apps/releases/latest)
  [![License: MIT](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
</div>

---

## What it does

Pick an app. Toggle **Hide** on. Every time a window of that app appears it
vanishes immediately: gone from the screen, the taskbar, and Alt-Tab. Toggle
off and the window comes back.

The manager is only a control panel. The actual hiding is done by a small
background service, so closing the manager does **not** stop hiding. The
service can also start automatically when you sign in to Windows.

## Download and install

1. Download **`HideAnyApps-Setup.exe`** from
   [Releases](https://github.com/ziyyhhk/Hide-Any-Apps/releases/latest).
2. Run it. It installs **per user** into
   `%LOCALAPPDATA%\Programs\Hide Any Apps` by default. You can change the
   folder on the install page.
3. Read and accept the terms shown by the installer.
4. Keep **Start the background service when I sign in** checked if you want
   hiding to survive a reboot. You can change this later in Settings.
5. Open **Hide Any Apps** from the Start menu.

No administrator rights are needed. Nothing is installed system-wide.

> The installer is not code-signed, so SmartScreen may show an "unknown
> publisher" warning the first time. Click **More info → Run anyway**, or
> right-click the file → Properties → **Unblock**.

## Quick start

1. Open **Hide Any Apps**.
2. Click **+ Add**, choose an app, and click **Add**.
3. Toggle the new row on.
4. Open the app. Its window disappears.
5. Toggle the row off, or press its hotkey, when you want it back.

You can now close the manager. The service keeps running in the background
with no tray icon and no window.

## Hotkeys

| Action | Default |
|---|---|
| Hide/show **all** managed apps (pause auto-hide) | `Ctrl+Alt+H` |
| Hide the **active** window right now | `Ctrl+Alt+Shift+H` |
| Unhide the **last** hidden window | `Ctrl+Alt+Shift+U` |
| Toggle one app on/off | `Ctrl+Alt+1` … `Ctrl+Alt+9` |

- Per-app bindings are assigned automatically when you add an app. Click the
  shortcut shown in the row to record a different one, or to clear it.
- **Unhide last** also switches off the rule that hid that window so it does
  not vanish again the next time it opens.
- Change the three global shortcuts in **Settings → Hotkeys** (click a
  shortcut, press the new combination, `Backspace` clears it).
- Hotkeys are registered by the service with `RegisterHotKey`. They cost no
  CPU while idle and work while the manager is closed.
- Avoid plain shortcuts such as `Ctrl+F` for a global binding: Windows will
  send that key to Hide Any Apps instead of the focused app.

## Background and startup

- The service is a separate 1.3 MB executable with the script embedded.
- Closing the manager leaves the service running. Only the service stays
  resident, at roughly **11–15 MB of RAM** and effectively **0% idle CPU**.
- The installer can add a per-user startup entry (`HKCU\...\Run`) so the
  service starts at sign-in. The same switch is in **Settings → Start service
  at logon**. No Task Scheduler task and no UAC prompt are involved.
- Pressing **Stop** in the manager writes `serviceState = stopped`, and the
  service restores every hidden window and exits. It will stay stopped at the
  next logon until you press **Start** again.

## Settings

The gear icon opens:

- **Start service at logon** — per-user background startup.
- **Hotkeys** — rebind or clear the three global shortcuts.

## Windows compatibility

- Windows 10 (1809 or newer) and Windows 11, x64.
- Hides normal desktop windows, including apps that resist "minimize to tray"
  tools.
- The service runs at normal user integrity. Windows that belong to an
  elevated process, UIAccess software (for example Magnifier), or protected
  system surfaces cannot be hidden without the older administrator/UIAccess
  build. Hiding them is intentionally not attempted.
- Some games with kernel anti-cheat may block any external window styling.
  That is a limitation of the game, not a bug in Hide Any Apps.

## Privacy

No telemetry, analytics, or network access. Settings, logs, and hidden-window
recovery data stay in `%APPDATA%\HideAnyWindow` on your PC.

## Uninstall

Settings → Apps → Installed apps → **Hide Any Apps** → Uninstall, or use the
Start menu uninstall shortcut. The uninstaller asks the service to restore all
hidden windows and exit first. Your settings in `%APPDATA%\HideAnyWindow` are
kept so a reinstall keeps your rules; delete that folder to remove them.

## Build from source

Requirements: Windows 10/11, the .NET 8 SDK, AutoHotkey v2, and Inno Setup 6.
The release script uses local tools under `.tools\`:

```
.tools\dotnet\dotnet.exe
.tools\ahk\AutoHotkey64.exe
.tools\ahk2exe\Ahk2Exe.exe
.tools\innosetup\ISCC.exe
```

Build the manager and tests:

```powershell
.tools\dotnet\dotnet.exe test manager\test\HideAnyWindowManager.Tests\HideAnyWindowManager.Tests.csproj
```

Build the installer:

```powershell
powershell -ExecutionPolicy Bypass -File dist\build-release.ps1
```

Output: `dist\HideAnyApps-Setup.exe`.

The script compiles the service with `Ahk2Exe`, publishes the manager
self-contained for `win-x64`, restores the XAML resources that WinAppSDK
sometimes drops, and runs Inno Setup. Incremental builds reuse the NuGet cache
and take well under a minute after the first publish.

## How it works

Two processes share one config file:

- **Service** (`service/`): compiled AutoHotkey v2. Watches `EVENT_OBJECT_SHOW`
  with `SetWinEventHook`, filters to top-level windows, hides matches with
  `WinHide` plus `ITaskbarList::DeleteTab`, and owns the global hotkeys. A
  named mutex lets the manager detect whether it is alive.
- **Manager** (`manager/`): WinUI 3 / .NET 8 control panel. Reads and writes
  `%APPDATA%\HideAnyWindow\config.json`; it does not need to stay open.

Hotkey toggles persist immediately. Both processes serialize config writes
through a named semaphore, and the manager watches the file so its list updates
when a hotkey changes a rule.

### Performance notes

- Window events are filtered to top-level windows before any process lookup.
- exe lookups are cached per HWND and revalidated by PID.
- Closed-window state is pruned every 30 seconds.
- Hotkeys use `RegisterHotKey`, not a keyboard hook.
- The config file is checked once per second with a cheap mtime comparison.
- Closing the manager releases its WinUI memory; only the small service stays
  resident.

## Credits and license

Hide Any Apps is a fork of
[Hide Any Window](https://github.com/Maxaubert/Hide-Any-Window) by Max Aubert,
modernized with configurable hotkeys, background startup, a per-user installer,
and lower idle resource use. The original project and this fork are MIT
licensed — see [LICENSE](LICENSE). AutoHotkey, .NET, and Windows App SDK remain
under their own licenses.
