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

## Publishing

Users install Command Palette extensions from the **Extension Gallery**, from **WinGet**, or from the **Microsoft Store**. Market Pulse uses the same Store-signed package for all three, so there is one package identity, and users need no certificate or Developer Mode:

```bash
winget install DarkAce.MarketPulse
```

Command Palette's **Search WinGet** also finds it, via the `windows-commandpalette-extension` tag.

**One-time setup**

1. Push this repository to GitHub.
2. In [Partner Center](https://partner.microsoft.com/dashboard) (free for individuals), reserve the app name **Market Pulse**. From **Product management → Product identity**, copy these values into repository **variables**:
   - `STORE_PRODUCT_ID`
   - `STORE_IDENTITY_NAME` (Package/Identity/Name)
   - `STORE_PUBLISHER` (Package/Identity/Publisher)
   - `STORE_PUBLISHER_DISPLAY_NAME`
3. Run **Actions → Release** with version `1.0.0` and leave *publish to Store* unticked. Download the `.msixupload` from the release, then create the first submission in Partner Center by hand: upload the package, fill in the listing using `gallery/darkace/market-pulse` for text and the icon, choose a free price, and submit. The Store requires the first submission to go through the website.
4. For automatic updates, add a Microsoft Entra app under Partner Center **Account settings → User management** and store these as **secrets**:
   - `PARTNER_CENTER_TENANT_ID`
   - `PARTNER_CENTER_SELLER_ID`
   - `PARTNER_CENTER_CLIENT_ID`
   - `PARTNER_CENTER_CLIENT_SECRET`

   Create a `store` environment and add yourself as a required reviewer if you want to approve each submission.
5. Add a `COMMUNITY_PAT` secret: a GitHub token that can fork repositories and open PRs (classic `public_repo`). Both the WinGet and gallery workflows use it.
6. Once the Store has published the app, publish it to WinGet (see *Each release*, steps 2–3). When the first WinGet PR is merged, run **Actions → Gallery listing** with *dry run* unticked and *include WinGet* ticked. A gallery maintainer then reviews and merges that PR.

**Each release**

1. Push a tag. The **Release** workflow then:
   - builds x64 and ARM64;
   - attaches the packages to a GitHub release;
   - submits to the Store.

   ```bash
   git tag v1.1.0
   git push origin v1.1.0
   ```

2. Wait for Store certification (usually a day or so) and for the Store to serve the update.
3. Run this, which needs the GitHub CLI signed in to this repo:

   ```powershell
   .\tools\Get-StorePackage.ps1 -Version 1.1.0
   ```

   It:
   - downloads the Store-signed package with `winget download`, which asks you to sign in with your Microsoft Entra ID work account (personal Microsoft accounts aren't accepted for this);
   - checks the package is signed by Microsoft Marketplace and is the right version;
   - attaches it to the `v1.1.0` release;
   - starts the **WinGet** workflow, which generates and validates the manifests and opens a PR on `microsoft/winget-pkgs`. WinGet's reviewers merge it, usually within a day.

The gallery follows the Store and WinGet automatically, so it needs no per-release step.

**Locally:**
- `.\build-package.ps1 -Version 1.1.0.0` produces the same `artifacts/`: an MSIX per platform, the `.msixbundle` and the `.msixupload`.
- `tools\New-WinGetManifest.ps1` writes WinGet manifests from a signed package. Check them with `winget validate --manifest <dir>`.
- `tools\Test-GalleryListing.ps1` checks the gallery listing.
- `powershell -File tools\New-Icons.ps1` regenerates all icons.

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
