using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

/// <summary>Searches Yahoo Finance for instruments and adds them to a watchlist group.</summary>
internal sealed partial class SearchMarketsPage : DynamicListPage, IDisposable
{
    private readonly MarketDataService _data;
    private readonly SettingsManager _settings;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _searchCts;
    private IListItem[] _items = [];

    public SearchMarketsPage(MarketDataService data, SettingsManager settings)
    {
        _data = data;
        _settings = settings;
        Id = "com.darkace.marketticker.search";
        Name = "Search";
        Title = "Search markets";
        Icon = new IconInfo("");
        PlaceholderText = "Search a company, index, currency pair, commodity or crypto…";
        EmptyContent = new CommandItem(new NoOpCommand()) { Title = "Type to search", Subtitle = "e.g. apple, ftse, gbpusd, gold, crypto:pepe" };
    }

    public override IListItem[] GetItems()
    {
        lock (_lock)
        {
            return _items;
        }
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = cts = new CancellationTokenSource();
        }

        _ = SearchAsync(newSearch.Trim(), cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        if (query.Length == 0)
        {
            SetItems([]);
            return;
        }

        // Anything typed is offered verbatim first, so unlisted or crypto symbols can still be added.
        var typed = MarketSymbol.TryParse(query, out var typedSymbol) && !query.Contains(' ', StringComparison.Ordinal)
            ? new[] { CreateItem(typedSymbol!, $"Add \"{typedSymbol!.Key}\" as typed", typedSymbol.Source == MarketSource.Binance ? "Binance live stream" : "Yahoo Finance symbol", DefaultGroup(typedSymbol, null)) }
            : [];

        try
        {
            await Task.Delay(250, ct).ConfigureAwait(false);
            IsLoading = true;
            var results = query.StartsWith("crypto:", StringComparison.OrdinalIgnoreCase)
                ? []
                : await _data.Yahoo.SearchAsync(query, ct).ConfigureAwait(false);

            var items = results.Select(r =>
            {
                var symbol = ToMarketSymbol(r);
                return CreateItem(symbol, r.Name, $"{symbol.Key} · {r.Type} · {r.Exchange}", DefaultGroup(symbol, r.Type));
            });

            SetItems([.. typed, .. items]);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke.
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage(new LogMessage { Message = $"MarketTicker: search failed: {ex.Message}" });
            SetItems(typed);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void SetItems(IListItem[] items)
    {
        lock (_lock)
        {
            _items = items;
        }

        RaiseItemsChanged(items.Length);
    }

    private ListItem CreateItem(MarketSymbol symbol, string title, string subtitle, string defaultGroup)
    {
        var groups = _settings.Groups.Select(g => g.Name).Where(n => !n.Equals(defaultGroup, StringComparison.OrdinalIgnoreCase));
        return new ListItem(new AddToWatchlistCommand(_settings, defaultGroup, symbol))
        {
            Title = title,
            Subtitle = subtitle,
            Icon = symbol.Source == MarketSource.Binance ? new IconInfo("₿") : new IconInfo(""),
            Tags = [new Tag(defaultGroup)],
            MoreCommands = [.. groups.Select(g => new CommandContextItem(new AddToWatchlistCommand(_settings, g, symbol))), new CommandContextItem(new OpenUrlCommand(symbol.WebUrl) { Name = "Open chart" })],
        };
    }

    /// <summary>Routes Yahoo crypto hits ("BTC-USD") to the live Binance stream instead of polling.</summary>
    private static MarketSymbol ToMarketSymbol(SymbolSearchResult result)
    {
        if (result.Type.Equals("CRYPTOCURRENCY", StringComparison.OrdinalIgnoreCase) &&
            result.Symbol.EndsWith("-USD", StringComparison.OrdinalIgnoreCase) &&
            MarketSymbol.TryParse("crypto:" + result.Symbol[..^4], out var crypto))
        {
            return crypto!;
        }

        // Yahoo symbols never contain spaces, so this only fails on an empty symbol, which SearchAsync filters out.
        return MarketSymbol.TryParse(result.Symbol, out var symbol) ? symbol! : new MarketSymbol(MarketSource.Yahoo, result.Symbol, result.Symbol);
    }

    private string DefaultGroup(MarketSymbol symbol, string? quoteType)
    {
        var name = symbol.Source == MarketSource.Binance ? "Crypto" : quoteType?.ToUpperInvariant() switch
        {
            "INDEX" => "Indices",
            "CURRENCY" => "FX",
            "FUTURE" => "Commodities",
            "CRYPTOCURRENCY" => "Crypto",
            "ETF" or "MUTUALFUND" => "Funds",
            _ => "Stocks",
        };

        // Reuse an existing group's exact casing when one matches.
        return _settings.Groups.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name ?? name;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }
}
