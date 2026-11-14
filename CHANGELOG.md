# Changelog

Each release's notes on GitHub come from its section here.

## 1.1.0

- **Price alerts.** Get a Windows notification when a coin goes above or below a price, or rises or falls by a percentage over 24h. An alert can fire once or every time (it waits for the price to move back before repeating, so a price sitting on the line doesn't spam you). Set them up in Settings → Alerts, or from the widget and tray menus. Alerts work for any coin, even one that isn't in a widget.
- **Live price in the tray icon.** Pick a coin in Settings → Appearance and its price shows in the notification area, green when it's up over 24h and red when it's down.
- Typing prices accepts both `70,000.50` and `70.000,50`.

## 1.0.0

The first public release.

- **Installer and automatic updates.** `PricePin-win-x64-Setup.exe` installs PricePin for your user (no admin rights needed) and keeps it up to date in the background. A portable zip is there if you'd rather not install.
- **English and Turkish (Türkçe).** PricePin follows your Windows language, or you can pick one in Settings → General.
- **Ticker bar.** Prices scroll across a bar you can dock to the top or bottom of the screen (optionally reserving the space) or leave floating.
- **Several widgets at once**, up to eight, each with its own coins, layout, size, opacity and position.
- **Global hotkeys**: Ctrl+Alt+P shows or hides every widget and Ctrl+Alt+T toggles click-through. You can change both.
- **Settings window**: pick any coin from Binance, Coinbase and Kraken, drag to reorder, and show prices in USD, USDT, EUR, TRY or GBP. Includes a dark, light or follow-Windows theme, accent colours and sizes.
- **Live prices** from Binance, Coinbase or Kraken over WebSocket, with automatic failover if an exchange is down or blocked where you are, and REST polling as a last resort.
- Real coin logos, 24h sparklines and price flashes; stay on top, opacity, click-through, snap to edges, lock position, tray icon and start with Windows.
