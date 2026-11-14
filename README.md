# PricePin

A fast, good-looking crypto price widget for Windows. It floats on your desktop, stays on top if you want it to, and shows live prices that update the moment they change.

> **Status:** early development. Phase 1 (the widget window itself) is in place and shows sample prices. Live exchange data comes next. See the [roadmap](#roadmap).

## Features

**Now**
- Borderless dark "glass" widget you can drag anywhere
- Snaps to screen edges (hold <kbd>Shift</kbd> while dragging to place it freely)
- Stay on top, opacity slider (20–100%), and <kbd>Ctrl</kbd> + mouse wheel to change opacity
- Click-through mode so the mouse passes through the widget
- Lock position
- Tray icon: left-click to show or hide, right-click for the menu
- Start with Windows
- Remembers its position and settings, and moves back on screen if a monitor is unplugged
- Sharp on high-DPI and mixed-scaling multi-monitor setups
- Only one copy runs at a time; launching it again shows the existing widget

**Planned**
- Live prices over WebSocket from Binance, Coinbase and Kraken, with automatic failover when an exchange is unreachable or blocked in your region
- Any coin, searchable, with drag to reorder, and a choice of quote currency
- Horizontal scrolling ticker bar layout, docked to the top or bottom of the screen
- 24h sparklines and price-change flashes
- Settings window, installer and auto-update
- Later: price alerts, charts, portfolio view, themes

## Download

Grab `PricePin.exe` from the latest build:

1. Open the [Actions tab](../../actions/workflows/build.yml) and pick the newest successful run.
2. Download **PricePin-win-x64** (or **PricePin-win-arm64** for ARM PCs) under *Artifacts*.
3. Unzip and run `PricePin.exe`. No install needed.

The exe isn't code-signed yet, so Windows SmartScreen may warn the first time. Click **More info → Run anyway**.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows 10 or 11.

```powershell
dotnet run --project src/PricePin
```

Single-file release build:

```powershell
dotnet publish src/PricePin -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Where settings live

`%AppData%\PricePin\settings.json`. Delete it to reset everything. Unexpected errors are written to `crash.log` in the same folder.

## Roadmap

1. **Foundation**: widget window, stay on top, opacity, click-through, tray, saved position ✅
2. **Live data**: Binance WebSocket feed, reconnects, connection status
3. **Polish**: tick flashes, sparklines, coin icons, ticker bar layout
4. **Settings**: settings window, coin picker, quote currency
5. **Resilience**: Coinbase and Kraken feeds, failover, REST fallback
6. **Release**: installer, auto-update, GitHub Releases
7. **Version 2**: alerts, charts, portfolio, themes

## Tech

C# · .NET 10 · WPF · [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) · [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)

## License

[MIT](LICENSE)
