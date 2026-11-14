# PricePin

A fast, good-looking crypto price widget for Windows. It floats on your desktop, stays on top if you want it to, and shows live prices that update the moment they change.

> **Status:** early development. Live prices work; the settings window, coin picker and ticker bar layout are next. See the [roadmap](#roadmap).

## Features

**Now**
- Live prices over WebSocket from Binance, Coinbase or Kraken, updating on every trade
- Automatic failover: if an exchange is down or blocked in your region, PricePin switches to the next one, and falls back to REST polling if no stream works
- Reconnects on its own with growing waits, and notices silent connections within 20 seconds
- Live status pill (LIVE, RECONNECTING, DELAYED, OFFLINE); hover it for details
- Real coin logos, 24h sparklines, green/red flashes on every price move, and 24h change that moves with the price
- Hover a coin for its 24h high, low and volume
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
- Any coin, searchable, with drag to reorder, and a choice of quote currency
- Horizontal scrolling ticker bar layout, docked to the top or bottom of the screen
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

## Data sources

| Source | Used for | Notes |
| --- | --- | --- |
| Binance | Live prices (WebSocket), 24h history | USD is shown using USDT pairs. Not available in some regions; PricePin moves on automatically. |
| Coinbase | Live prices (WebSocket), REST fallback, 24h history | |
| Kraken | Live prices (WebSocket), 24h history | |
| [cryptocurrency-icons](https://github.com/spothq/cryptocurrency-icons) (CC0) | Coin logos | Popular coins are bundled; others download once and are cached. |
| CoinGecko | Logos for newer coins | Only asked when a logo isn't in the set above. |

All of these are public endpoints. No account or API key is needed.

To check which sources work from your network:

```powershell
dotnet run --project tools/FeedProbe -- 15 BTC ETH SOL
```

## Where settings live

`%AppData%\PricePin\settings.json`. Delete it to reset everything. Unexpected errors are written to `crash.log` in the same folder. Downloaded logos are cached in `%LocalAppData%\PricePin\icons`.

## Roadmap

1. **Foundation**: widget window, stay on top, opacity, click-through, tray, saved position ✅
2. **Live data**: Binance, Coinbase and Kraken feeds, failover, REST fallback, logos, sparklines, design refresh ✅
3. **Layouts**: horizontal ticker bar, compact mode, more themes
4. **Settings**: settings window, coin picker, quote currency
5. **Release**: installer, auto-update, GitHub Releases
6. **Version 2**: alerts, charts, portfolio, themes

## Tech

C# · .NET 10 · WPF · [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) · [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)

## License

[MIT](LICENSE)
