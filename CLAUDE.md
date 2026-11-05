# Market Pulse: agent guide

PowerToys Command Palette extension that shows live market prices on the **Dock**. C# / .NET 10, out-of-process COM server (CsWinRT + Shmuelie.WinRTServer), packaged as MSIX. The user-facing name is **Market Pulse**; the project, namespace and folder are still `MarketTicker`.

## Commands

| Task | Command |
| --- | --- |
| Build and register locally (needs Developer Mode) | `.\deploy.ps1`, then run **Reload** in Command Palette |
| Release packages (x64 + ARM64, bundle, `.msixupload`) | `.\build-package.ps1 -Version 1.2.3.0` → `artifacts/` |
| Validate the gallery listing | `pwsh tools\Test-GalleryListing.ps1` |
| Regenerate all icons | `powershell -File tools\New-Icons.ps1` (Windows PowerShell 5.1: it uses System.Drawing) |
| Compile-only check | `dotnet build MarketTicker\MarketTicker.csproj -p:Platform=x64` |

There are no unit tests. To verify a change, deploy it, Reload, and read the diagnostics log (see Debugging).

## Layout

- `MarketTicker/MarketTickerCommandsProvider.cs`: top-level commands, plus `GetDockBands()`, which returns every band and rebuilds them when the watchlist changes.
- `Dock/`: `MarketBand` and `MarketBandPage`, one band each. `QuoteListItem` (`QuoteItemLayout.Dock` or `List`), `RotatingTickerItem`, and `OnLoadDynamicListPage` (copied from PowerToys TimeDate, MIT).
- `Services/MarketDataService.cs`: quote cache, plus `Subscribe(owner, symbols)` / `Unsubscribe(owner)`. Only the union of subscribed symbols is fetched: Yahoo is polled, Binance is a WebSocket.
- `Models/`: `MarketSymbol` (entry parsing: `crypto:` prefix, ` as Label`), `Watchlist` (multi-line `Group: a, b` text), `Quote` (formatting).
- `Helpers/SettingsManager.cs`: toolkit `JsonSettingsManager`. `Helpers/Diagnostics.cs` is the file log.
- `gallery/darkace/market-pulse/`: Extension Gallery listing (`extension.json` + `icon.png`, optional `screenshots/`).
- `.github/workflows/`: `ci.yml` (build + validate), `release.yml` (tag `v*.*.*` → GitHub release → Store), `gallery.yml` (PR to microsoft/CmdPal-Extensions).

## Rules that are easy to break

- **Never change these IDs.** Users' Dock pins and settings are keyed to them:
  - provider Id `com.darkace.marketticker`;
  - band command Ids `com.darkace.marketticker.band.*`;
  - the CLSID `4A6BF055-C515-4A17-9E7E-9937EE62297C`, which must match in `MarketTicker.cs` and in all three places in `Package.appxmanifest`;
  - the AppExtension Id.

  Display strings can change freely.
- **No destructive commands on Dock items.** The Dock's right-click menu puts an item's `MoreCommands` next to its own pin and unpin actions. A "Remove from watchlist" there once emptied a user's whole watchlist. Destructive actions belong only on `QuoteItemLayout.List` items, marked `IsCritical`.
- **Bands are load-aware.** The host attaches `ItemsChanged` when it renders a band and detaches it when it stops. `OnLoadDynamicListPage` turns that into `Loaded()` / `Unloaded()`, where we subscribe and unsubscribe feeds.
  - Don't start timers or network work in constructors.
  - Don't block inside `GetItems()`. The host initialises bands one after another on one thread, so a stall hides every later band.
- **Every band's command needs a non-empty `Id`.** `RaiseItemsChanged()` on the provider makes the host call `GetDockBands()` again.
- **Watchlist text is user data.** `SettingsManager.WatchlistChanged` only fires on real changes, and an empty watchlist means "no bands", not "use defaults".
- **Event handlers come from COM.** Invoke each one separately in its own try/catch (see `OnLoadDynamicListPage.RaiseItemsChanged`). One dead host proxy must not stop the others.
- **AOT/trim-safe code only** (`IsAotCompatible`, `PublishTrimmed` in Release). Use `System.Text.Json` with `JsonDocument` or source generators, not reflection serialisation.
- **Name clash.** `MarketTicker` is both the namespace and the `IExtension` class. Inside the namespace, `nameof(DockStyle.X)` resolves where `MarketTicker.DockStyle` would not.
- **Data sources need no keys.** Yahoo Finance (`query1` spark for quotes, `query2` search, 20 symbols per request) requires a browser-like User-Agent. Binance uses `wss://stream.binance.com:9443/stream?streams=<pair>@miniTicker`. Keep quotes from the previous fetch on errors (`Quote.Failed` keeps the last price).

## Build environment gotchas

- MSIX packaging needs the **Windows 11 SDK 10.0.26100**. Without it, C# still compiles, because the csproj points CsWinRT at the NuGet winmd, but packaging fails with `APPX3217`.
- `deploy.ps1` builds an unsigned MSIX, extracts it and runs `Add-AppxPackage -Register`. This requires Developer Mode. Don't try to enable Developer Mode or change other security settings for the user; ask them.
- After deploying, Command Palette keeps stale band view models until **Reload**. Tell the user to run Reload rather than assuming that deploying was enough.
- The Store requires versions in the form `X.Y.Z.0` (revision 0). `build-package.ps1` stamps the version and the Store identity into the manifest temporarily, then restores the file.

## Debugging

- Extension log: `%LOCALAPPDATA%\Packages\DarkAce.MarketTicker_71ssftxwxswq0\LocalState\diagnostics.log`. It records process start, `GetDockBands` counts, band loaded/unloaded/GetItems, subscriptions, and feed errors.
- Settings: `settings.json` in the same folder. Command Palette's own Dock layout (pinned band Ids per monitor) is in `%LOCALAPPDATA%\Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\settings.json`.
- Host log: `%LOCALAPPDATA%\Microsoft\PowerToys\CmdPal\Logs\<version>\`. "Skipping duplicate pinned dock band command" is harmless.

## Publishing

- The Extension Gallery doesn't host packages. It points at a Microsoft Store or WinGet ID, and we use the Store. The first Store submission is manual in Partner Center; later ones go through `release.yml`. The README's "Publishing" section lists the variables and secrets it needs.
- Keep `gallery/.../extension.json` valid against the gallery schema:
  - `id` must match the folder path;
  - `shortDescription` ≤ 200 characters;
  - `description` ≤ 3000;
  - ≤ 5 tags and ≤ 3 categories from the fixed list;
  - icon PNG ≤ 100 KB.

  The msstore id stays as the placeholder `STORE_PRODUCT_ID` in the repo; `gallery.yml` fills it in from `vars.STORE_PRODUCT_ID`.
- Pushing tags, running the Store or gallery workflows, and opening PRs on `microsoft/CmdPal-Extensions` are public actions. Do them only when the user asks.

## Conventions

- Commit after each logical step. Write commit messages that explain *why*.
- Match the surrounding style: file-scoped namespaces, `sealed partial` classes (CsWinRT needs `partial` on WinRT-exposed types), and brief XML doc comments on types.
