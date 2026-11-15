# Changelog

Each release's notes on GitHub come from its section here.

## 1.5.0

- **News.** Open **News, whales & fees** from the widget or tray menu to read the latest headlines from CoinDesk, Cointelegraph, Decrypt, The Block and Bitcoin Magazine. Each one is tagged with the coins it mentions, and a click opens it in your browser. Turn on **Show headlines** for a widget to mix them into a ticker bar or show one at a time under a list. You can keep only news about your coins, and get a notification when there's news about a coin you hold. Headlines refresh every five minutes.
- **Whale and liquidation radar.** Watch big spot trades and futures liquidations as they happen, with totals for the last hour: longs and shorts liquidated, whale buys and sells. Trades come from Binance (or Coinbase where Binance isn't available) and liquidations from Binance and Bybit futures together. Choose the smallest trade and liquidation that count, keep it to your coins, and get notified about very large events.
- **Network fees.** See Bitcoin fees for the next block, 30 minutes, an hour and economy, and Ethereum gas with its base fee and tip, plus what a payment costs in your currency. Turn on **Show network fees** to keep them in a widget, and set an alert for when gas or the Bitcoin fee drops to your level. An alert fires once and again only after fees have gone back up.
- Nothing runs in the background unless something uses it: the window, a widget that shows it, or a notification you turned on.

## 1.4.0

- **Any token, on any chain.** Search by name or paste a contract address in Settings → Coins or in quick search, and PricePin finds tokens on decentralised exchanges too: Solana, Base, BNB Chain, Ethereum, Arbitrum and dozens more, including brand-new meme coins. Their prices come from DexScreener and update every few seconds, charts and sparklines come from GeckoTerminal, and hovering one shows its chain, market cap and liquidity. Results with very little liquidity are left out of name searches to keep spam away. DEX tokens work in alerts, the portfolio and the tray icon like any other coin, and their chart has a link to the pool on DexScreener.
- **Quick search** (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>K</kbd>, or from the widget and tray menus): type any coin to see its live price, 24h change and sparkline, convert amounts both ways, and open its chart, add it to a widget, set an alert or add it to your portfolio. Arrow keys choose, Enter opens the chart, Ctrl+Enter adds it to your widget.
- **1h, 24h or 7d change.** Each widget can show the change over the last hour, day or week (Settings → Widgets, or right-click → Show change for). The sparkline follows the same span.
- Sparklines from Coinbase now load when Binance isn't available where you are.

## 1.3.0

The first public release of PricePin, a live crypto price widget for Windows. It brings together everything built for versions 1.0 to 1.3; the full list is in [CHANGELOG.md](https://github.com/4U0/PricePin-Next/blob/main/CHANGELOG.md).

**Which file do I need?** `PricePin-win-x64-Setup.exe` for most PCs, `PricePin-win-arm64-Setup.exe` for ARM PCs, or a `Portable.zip` to run without installing. The installer isn't code-signed yet, so if Windows SmartScreen warns you, click **More info → Run anyway**.

**Highlights**

- Live prices over WebSocket from Binance, Coinbase or Kraken, with automatic failover if an exchange is blocked where you are
- Any coin from those exchanges, in USD, USDT, EUR, TRY or GBP
- A card list or a scrolling ticker bar, up to 8 widgets, stay on top, opacity and click-through
- Coin charts, a market overview with the Fear & Greed Index, price alerts, and a portfolio with privacy mode
- Automatic updates, English and Türkçe

**New in 1.3**

- **Coin charts.** Double-click a coin in any widget or the ticker bar to open its chart. Choose 1 day, 1 week, 1 month or 1 year. The chart follows the live price, shows the time and price under the mouse, and lists the range's high, low, change and volume. **Set alert** and **Add to portfolio** buttons are right there. The chart data comes from Binance, Coinbase or Kraken, whichever works where you are.
- **Market overview** (widget or tray menu): total crypto market cap and its 24h change, BTC and ETH dominance, the Crypto Fear & Greed Index as a gauge, and your top gainers and losers today. It refreshes every five minutes. Data comes from CoinGecko and alternative.me, with no account or key needed.

## 1.2.0

- **Portfolio.** Add what you hold in Settings → Portfolio, with the average price you paid if you like. List widgets show each holding's amount and value under the coin, plus a portfolio total with today's change at the bottom. The Portfolio page shows the total value, today's change and profit/loss for each coin and overall. Buying more of a coin you already hold averages the cost for you. Holdings are kept only in your settings file on this PC.
- **Privacy mode.** One click (widget or tray menu) or <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>H</kbd> hides every amount and value behind a blur, everywhere, including the tray tooltip. Prices stay visible, so it's safe for screen sharing and streaming.
- Coins you hold stream live even if no widget shows them.

## 1.1.0

- **Price alerts.** Get a Windows notification when a coin goes above or below a price, or rises or falls by a percentage over 24h. An alert can fire once or every time (it waits for the price to move back before repeating, so a price sitting on the line doesn't spam you). Set them up in Settings → Alerts, or from the widget and tray menus. Alerts work for any coin, even one that isn't in a widget.
- **Live price in the tray icon.** Pick a coin in Settings → Appearance and its price shows in the notification area, green when it's up over 24h and red when it's down.
- Typing prices accepts both `70,000.50` and `70.000,50`.

## 1.0.0

- **Installer and automatic updates.** `PricePin-win-x64-Setup.exe` installs PricePin for your user (no admin rights needed) and keeps it up to date in the background. A portable zip is there if you'd rather not install.
- **English and Turkish (Türkçe).** PricePin follows your Windows language, or you can pick one in Settings → General.
- **Ticker bar.** Prices scroll across a bar you can dock to the top or bottom of the screen (optionally reserving the space) or leave floating.
- **Several widgets at once**, up to eight, each with its own coins, layout, size, opacity and position.
- **Global hotkeys**: Ctrl+Alt+P shows or hides every widget and Ctrl+Alt+T toggles click-through. You can change both.
- **Settings window**: pick any coin from Binance, Coinbase and Kraken, drag to reorder, and show prices in USD, USDT, EUR, TRY or GBP. Includes a dark, light or follow-Windows theme, accent colours and sizes.
- **Live prices** from Binance, Coinbase or Kraken over WebSocket, with automatic failover if an exchange is down or blocked where you are, and REST polling as a last resort.
- Real coin logos, 24h sparklines and price flashes; stay on top, opacity, click-through, snap to edges, lock position, tray icon and start with Windows.
