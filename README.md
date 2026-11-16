# Windows Live

![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D4?logo=windows11&logoColor=white)
![Language](https://img.shields.io/badge/Language-C%23%2014-512BD4?logo=sharp&logoColor=white)
![Framework](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![UI](https://img.shields.io/badge/UI-WPF-0078D4)
![WPF--UI](https://img.shields.io/badge/WPF--UI-4.3.0-4B6EAF)
![System.Management](https://img.shields.io/badge/System.Management-9.0.2-4B6EAF)
![License](https://img.shields.io/badge/License-Apache--2.0-blue)

Windows Live is a lightweight Windows desktop companion inspired by the Dynamic Island. It places a compact, animated dock at the top of the screen and surfaces media, timers, recordings, notifications, and other live system activity.

The app is built with WPF and .NET 10 for Windows. It uses Windows media controls and system APIs to read activity and provide controls where Windows makes them available.

## Technology details

| Area | Technology | Used for |
| --- | --- | --- |
| Language and runtime | C# 14, .NET 10 | Application logic; C# 14 is the SDK default for this target framework. |
| Desktop UI | WPF, XAML | Dock, compact and expanded activity views, animation, and controls. |
| UI components | WPF-UI 4.3.0 | Fluent-styled icons and interface components. |
| Media | Windows Runtime `GlobalSystemMediaTransportControlsSessionManager` | Discover media sessions and read track, artwork, playback, and timeline state. |
| Windows integrations | Windows Runtime APIs and .NET networking APIs | Battery and power status, Bluetooth discovery, Wi-Fi connection and hotspot adapter status, notifications, clipboard, and native Share UI. |
| Native Windows interop | Win32 P/Invoke and COM | Window positioning and hit testing, topmost behavior, Core Audio endpoint volume, and selected system signals. |
| Brightness | `System.Management` 9.0.2 and WMI | Read and change monitor brightness where supported by the display and driver. |
| Clock timers | Windows UI Automation and Windows Clock app settings | Read active timer state and remaining time. |
| Startup | Current-user registry, Startup folder, Task Scheduler | Launch Windows Live when the user signs in. |
| License | Apache License 2.0 | See [`LICENSE`](LICENSE). |

The project targets `net10.0-windows10.0.22621.0` and uses the .NET SDK's default C# language version. It does not explicitly pin a C# version in the project file. Windows API features can depend on the Windows build, driver, hardware, permissions, and the source app exposing the relevant information.

## What it can do

- **Media playback:** show current track information and artwork, with playback controls and an expanded player.
- **Live timers:** show the app's focus timer and active Windows Clock timers. When several activities are active, the notch prioritizes the main activity and can show a timer in a separate compact pill.
- **Recording indicators:** show voice or screen recording status, with a compact red indicator alongside media when activities overlap.
- **Notifications:** surface recent Windows notifications in the compact dock and expanded notification view.
- **Clipboard and sharing:** preview copied content, handle dropped files, and open the native Windows Share UI for supported items.
- **System activity:** display battery and charging state, volume and brightness controls, power mode, Bluetooth changes, Focus Assist, Night Light, alarms, and camera/microphone privacy activity.
- **Window snapping:** provide a dock entry point for window snapping actions.
- **Adaptive presentation:** use a top-attached notch or floating pill, with compact and expanded layouts, system theme and accent colors, and foreground-window behavior.
- **Startup support:** register the app to launch at sign-in. The installer and the app's startup service manage the startup entries.

Windows and application versions expose different information. Media artwork, notification access, Clock timer details, and some recording or privacy signals depend on the source app and Windows permissions/APIs. Wi-Fi status reflects this computer's network adapters; the app does not scan the local network or enumerate devices connected to the same router.

## Requirements

- Windows 11 (the project targets `net10.0-windows10.0.22621.0`).
- .NET 10 SDK with the Windows Desktop components.

## Build and run

From the repository directory, run:

```powershell
dotnet restore .\WindowsLive.csproj
dotnet run --project .\WindowsLive.csproj
```

To create the Release files used by `install.bat`:

```powershell
dotnet publish .\WindowsLive.csproj -c Release -o .\publish --no-self-contained
```

The publish output is framework-dependent; install the .NET 10 Windows Desktop Runtime on machines that do not have it already.

## Install and uninstall

1. Build the publish directory using the command above.
2. Run `install.bat` from the repository. It copies `publish\` to `%LOCALAPPDATA%\WindowsLive`, configures sign-in startup, and launches the app.
3. To remove the installed app and its startup entries, run `uninstall.bat`.

You can also launch a published build directly from `publish\WindowsLive.exe`.

## Project layout

```text
WindowsLive.csproj       WPF project and package references
App.xaml(.cs)            Application startup, single-instance guard, logging
MainWindow.xaml(.cs)     Dock layout, compact activity selection, interactions
Controls/                Reusable notch, icon, progress, and equalizer controls
Models/                  Activity and media data models
Services/                Windows integration and activity watchers
Resources/               Styles and small bundled assets
install.bat              Install a published build and configure startup
uninstall.bat            Remove the installed build and startup entries
```

## Configuration and permissions

Windows may ask for access to notifications or other system information the first time a related feature is used. Grant only the access you want Windows Live to use. The app writes its diagnostic log beside the running executable as `app_debug.log`; the log may include detected application names and activity transitions, so review it before sharing.

## Contributing

Bug reports and pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a change. For security concerns, follow [SECURITY.md](SECURITY.md). Everyone participating in the project is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

Windows Live is distributed under the [Apache License 2.0](LICENSE). See the license file for permissions, conditions, and limitations.
