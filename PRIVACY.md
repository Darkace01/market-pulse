# Market Pulse Privacy Policy

_Effective date: 5 November 2026_

Market Pulse is a PowerToys Command Palette extension that shows market prices. This policy explains what information it handles. In short: **Market Pulse has no accounts, no analytics, no ads and no servers of its own. It collects nothing about you, and your watchlist never leaves your PC.** The only things that leave your PC are the requests needed to fetch prices, described below.

## Information stored on your device

Market Pulse keeps the following only on your PC, in its app data folder (`%LOCALAPPDATA%\Packages\DarkAce.MarketTicker_…\LocalState`):

- **Settings**: your watchlists (the markets and group names you choose), refresh interval, Dock layout and display preferences.
- **Diagnostics log** (`diagnostics.log`): technical events such as when Dock bands load, which symbols are being fetched, and connection errors. It contains no personal information, is capped at 512 KB, and is never sent anywhere.

Uninstalling Market Pulse deletes this folder.

## Information sent to third-party price providers

To show prices, Market Pulse connects directly from your PC to these public services. No API key or account is used.

| Service | When | What is sent |
| --- | --- | --- |
| **Yahoo Finance** (`query1.finance.yahoo.com`, `query2.finance.yahoo.com`) | While a stock, index, FX or commodity band or page is visible, and when you use **Search markets** | The ticker symbols being displayed, and the text you type into Search markets |
| **Binance** (`stream.binance.com`) | While a crypto band or page is visible | The crypto trading pairs being displayed (for example BTCUSDT) |

Like any internet request, these connections also reveal your **IP address** and a generic client identifier (`MarketTicker/1.0`) to the provider. Market Pulse sends no other information: no name, email, device ID, location or usage history.

Each provider handles this data under its own privacy policy:

- Yahoo: https://legal.yahoo.com/us/en/yahoo/privacy/index.html
- Binance: https://www.binance.com/en/privacy

When you click a price, Market Pulse opens that market's page on finance.yahoo.com or binance.com in your web browser. From then on, that website's own privacy policy applies.

## What Market Pulse does not do

- It does not collect, store or transmit personal information to the developer.
- It does not use analytics, telemetry, crash reporting, tracking or advertising.
- It does not sell or share data with anyone.
- It does not read your files, contacts, location, camera or microphone.

## Distribution platforms

If you install Market Pulse from the Microsoft Store, WinGet or GitHub, those platforms may collect information under their own policies. For example, Microsoft may collect install and crash data through Windows. Market Pulse's developer receives only the aggregate statistics those platforms provide.

## Children

Market Pulse is not directed at children and does not knowingly collect information from anyone.

## Changes

If this policy changes, the updated version will be published at https://github.com/Darkace01/market-pulse/blob/main/PRIVACY.md with a new effective date. The file's history shows every past version.

## Contact

Questions or concerns: open an issue at https://github.com/Darkace01/market-pulse/issues.
