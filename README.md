# PricePin

A fast, good-looking crypto price widget for Windows. It floats on your desktop, stays on top if you want it to, and shows live prices that update the moment they change.

> **Status:** v1.2. Portfolio tracking with a privacy mode. Coin detail charts and a market overview are next. See the [roadmap](#roadmap).

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
- Price alerts: when a coin goes above or below a price, or moves a set percentage in 24h. Alerts appear as Windows notifications with an optional sound, and can fire once or every time
- Portfolio: enter what you hold (and optionally what you paid) to see each holding's value under its price, a live total with today's change, and profit/loss. Stored only on your PC
- Privacy mode (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>H</kbd>) blurs and masks every amount for screen sharing and streaming, while prices stay visible
- Live price in the tray icon for the coin you pick, green or red with the 24h move
- Installer with automatic background updates from GitHub Releases (or a portable zip if you'd rather not install)
- English and Turkish (Türkçe), following your Windows language or picked in Settings
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
- Coin detail charts and market overview (v1.3)

## Download

Get it from the [latest release](../../releases/latest):

- **PricePin-win-x64-Setup.exe**: installs for your user only (no admin rights), adds a Start menu shortcut and keeps itself up to date. Use **PricePin-win-arm64-Setup.exe** on ARM PCs.
- **PricePin-win-x64-Portable.zip**: unzip anywhere and run `PricePin.exe`. The portable copy doesn't update itself.

Or with the Windows Package Manager, once it's listed there:

```powershell
winget install PricePin
```

Uninstall from *Settings → Apps* like any other app. Your settings stay in `%AppData%\PricePin` in case you come back; delete that folder to remove them too.

The installer isn't code-signed yet, so Windows SmartScreen may warn the first time. Click **More info → Run anyway**.

Want to try unreleased changes? Every build on the [Actions tab](../../actions/workflows/build.yml) has a test installer and a single-file exe under *Artifacts*.

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
5. **v1.0 Launch**: installer, auto-update, Turkish and English ✅ (winget listing and code signing are set up and switch on after the first release; see [RELEASING.md](docs/RELEASING.md))
6. **v1.1**: price alerts, live price on the tray icon ✅
7. **v1.2**: portfolio and privacy blur ✅
8. **v1.3**: coin detail charts, market overview (Fear & Greed, BTC dominance)

## Translating

PricePin's text lives in [`src/PricePin/Strings`](src/PricePin/Strings). To add a language, copy `Strings.en.xaml` to `Strings.<code>.xaml` (for example `Strings.de.xaml`), translate the text between the tags, keep `{0}`-style placeholders, and add the language to `Loc.Languages` in [`Services/Loc.cs`](src/PricePin/Services/Loc.cs). The tests check that every translation has the same keys and placeholders as English.

## Releasing

See [docs/RELEASING.md](docs/RELEASING.md).

## Tech

C# · .NET 10 · WPF · [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) · [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) · [Velopack](https://velopack.io) (installer and updates)

## License

[MIT](LICENSE)
