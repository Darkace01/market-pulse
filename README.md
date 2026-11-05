# Market Pulse for the PowerToys Command Palette Dock

Live market prices on the [Command Palette Dock](https://learn.microsoft.com/windows/powertoys/command-palette/dock): stocks, indices, FX, commodities and crypto. No API keys needed.

| Asset class | Source | Update style |
| --- | --- | --- |
| Stocks, ETFs, indices, FX, futures | Yahoo Finance (public endpoints) | Polled every 5–60 s (configurable). Real time for most US listings, delayed for some exchanges. |
| Crypto (`crypto:` prefix) | Binance public WebSocket mini-ticker | Pushed about every second |

Feeds only run for symbols on a band or page that Command Palette is currently showing, so unpinned bands cost nothing.

## Dock bands

Open Dock edit mode (right-click the Dock, then **Edit Dock**), select **+**, and choose any of these:

- **Market Pulse: rotating**: a single button that cycles through every watched market. It takes up the least space.
- **Market Pulse: all markets**: every watched market in one strip.
- **Market Pulse: _group_**: one band per watchlist group, such as Indices, Tech or Crypto.
- **Market Pulse: _symbol_**: one band per individual market.

Click a price to open its chart. Right-click it to copy the price. To remove a market, use the **Markets** page in Command Palette (right-click → Remove), so a Dock click can never delete one by accident.

## Choosing markets

Use either of these:

1. **Search markets** (a top-level command): type a company, index, pair, commodity or coin, and press Enter to add it to the suggested group. More actions let you add it to any other group. Crypto results are routed to the live Binance stream automatically.
2. **Settings → Watchlists**: one group per line.

```text
Indices: ^GSPC as S&P 500, ^IXIC as Nasdaq, ^FTSE as FTSE 100
Tech: AAPL, MSFT, NVDA
Crypto: crypto:BTC, crypto:ETH, crypto:SOL, crypto:ETHBTC as Ether/BTC
FX: GBPUSD=X as GBP/USD, EURUSD=X as EUR/USD
Commodities: GC=F as Gold, CL=F as Oil
```

- Symbols use Yahoo Finance tickers: `^` for indices, `=X` for FX, `=F` for futures, and exchange suffixes such as `VOD.L` or `SAP.DE`.
- Prefix crypto symbols with `crypto:`. A bare coin such as `crypto:BTC` means its USDT pair. Full Binance pairs also work, for example `crypto:ETHBTC` or `crypto:SOLEUR`.
- Add ` as Label` to rename an entry on the Dock.
- Lines starting with `#` are ignored.

Other settings: stock refresh interval, Dock layout (two-line, one-line compact, price only), trend icons (📈/📉) and rotating ticker speed.

## Build and deploy

Prerequisites: Windows 11, PowerToys with Command Palette enabled, Developer Mode on, and Visual Studio with the **Windows 11 SDK (10.0.26100)** component. `.vsconfig` lists the workloads; open the solution and Visual Studio offers to install anything missing.

1. Open `MarketTicker.sln` and pick the **x64** (or ARM64) platform.
2. **Build → Deploy MarketTicker**. Building alone does not register the package.
3. In Command Palette, run **Reload** (the one with subtitle *Reload Command Palette Extension*).
4. Add the bands from Dock edit mode as described above.

After code changes, deploy again and run **Reload**.

**Without Visual Studio:** `.\deploy.ps1` (or `.\deploy.ps1 -Configuration Release`) builds an unsigned package and registers it in place. This is the same as Deploy, so it also needs Developer Mode.

> Without the Windows SDK installed, the C# still compiles (the project feeds CsWinRT the metadata from the restored SDK projection package), but MSIX packaging fails with `APPX3217`. Install the SDK component to deploy.

## Project layout

```text
MarketTicker/
  Models/      MarketSymbol (entry parsing), Watchlist (groups), Quote (formatting)
  Services/    YahooFinanceClient, BinanceStreamClient, MarketDataService (subscriptions, cache)
  Dock/        MarketBand (load-aware band), QuoteListItem, RotatingTickerItem
  Pages/       MarketsPage (live list), SearchMarketsPage (add symbols)
  Commands/    add / remove watchlist commands
  Helpers/     SettingsManager
  MarketTickerCommandsProvider.cs   top-level commands and GetDockBands()
```

Data is provided as-is by third-party public endpoints. It is not investment advice, and the endpoints may change or rate-limit without notice.
