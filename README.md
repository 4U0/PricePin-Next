# PricePin

A fast, good-looking crypto price widget for Windows. It floats on your desktop, stays on top if you want it to, and shows live prices that update the moment they change.

> **Status:** early development (v0.4). Live prices, settings, the coin picker, the ticker bar and multiple widgets work; the installer and auto-update are next. See the [roadmap](#roadmap).

## Features

**Now**
- Live prices over WebSocket from Binance, Coinbase or Kraken, updating on every trade
- Automatic failover: if an exchange is down or blocked in your region, PricePin switches to the next one, and falls back to REST polling if no stream works
- Reconnects on its own with growing waits, and notices silent connections within 20 seconds
- Live status pill (LIVE, RECONNECTING, DELAYED, OFFLINE); hover it for details
- Real coin logos, 24h sparklines, green/red flashes on every price move, and 24h change that moves with the price
- Hover a coin for its 24h high, low and volume
- Settings window: pick any coin from Binance, Coinbase and Kraken's lists (search by name or symbol), drag to reorder, and show prices in USD, USDT, EUR, TRY or GBP
- Dark, light or follow-Windows theme, eight accent colours, three sizes, and options to hide names, sparklines or headings
- Two layouts: a vertical card list, or a horizontal scrolling ticker bar docked to the top or bottom of the screen (optionally reserving the space so maximised windows don't cover it) or floating anywhere
- Run several widgets at once (up to 8), each with its own coins, layout, size, opacity and position
- Global hotkeys: <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>P</kbd> shows or hides all widgets, <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> toggles click-through (both can be changed in Settings)
- The ticker bar pauses while you hover it, and steps out of the way of full-screen apps
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
- Installer, auto-update, winget, Turkish and English (v1.0)
- Price alerts and live price on the tray icon (v1.1)
- Portfolio with privacy blur (v1.2)
- Coin detail charts and market overview (v1.3)

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

`%AppData%\PricePin\settings.json`. Delete it to reset everything. Unexpected errors are written to `crash.log` in the same folder. Downloaded logos and the coin list are cached in `%LocalAppData%\PricePin`.

## Roadmap

1. **Foundation**: widget window, stay on top, opacity, click-through, tray, saved position ✅
2. **Live data**: Binance, Coinbase and Kraken feeds, failover, REST fallback, logos, sparklines, design refresh ✅
3. **v0.3 Settings**: settings window, coin picker, display currency, themes, sizes ✅
4. **v0.4 Layouts**: ticker bar, multiple widgets, hotkeys ✅
5. **v1.0 Launch**: installer, auto-update, winget, code signing, Turkish and English
6. **v1.1**: price alerts, live price on the tray icon
7. **v1.2**: portfolio and privacy blur
8. **v1.3**: coin detail charts, market overview (Fear & Greed, BTC dominance)

## Tech

C# · .NET 10 · WPF · [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) · [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)

## License

[MIT](LICENSE)
