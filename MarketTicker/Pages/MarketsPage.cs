using System;
using System.Linq;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

/// <summary>Top-level page listing every watched market, grouped into sections, with live prices.</summary>
internal sealed partial class MarketsPage : OnLoadDynamicListPage, IDisposable
{
    private readonly MarketDataService _data;
    private readonly SettingsManager _settings;
    private readonly ListItem _searchItem;
    private readonly ListItem _settingsItem;
    private readonly Lock _lock = new();
    private QuoteListItem[] _quotes = [];
    private bool _loaded;

    public MarketsPage(MarketDataService data, SettingsManager settings, SearchMarketsPage searchPage)
    {
        _data = data;
        _settings = settings;
        Id = "com.darkace.marketticker.markets";
        Name = "Open";
        Title = "Markets";
        Icon = QuoteListItem.UpIcon;
        PlaceholderText = "Filter markets…";

        _searchItem = new ListItem(searchPage) { Title = "Search & add markets", Subtitle = "Stocks, indices, FX, commodities, crypto", Icon = new IconInfo("") };
        _settingsItem = new ListItem(settings.Settings.SettingsPage) { Title = "Edit watchlists & settings", Icon = new IconInfo("") };
        EmptyContent = new CommandItem(searchPage) { Title = "No markets yet", Subtitle = "Search for a market to add it to a watchlist" };

        Rebuild();
        _settings.WatchlistChanged += OnWatchlistChanged;
    }

    public override IListItem[] GetItems()
    {
        QuoteListItem[] quotes;
        lock (_lock)
        {
            quotes = _quotes;
        }

        var filter = SearchText.Trim();
        var matches = filter.Length == 0
            ? quotes
            : quotes.Where(q =>
                q.Symbol.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                q.Symbol.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                q.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

        return [.. matches, _searchItem, _settingsItem];
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    protected override void Loaded()
    {
        lock (_lock)
        {
            _loaded = true;
            _data.Subscribe(this, _quotes.Select(q => q.Symbol));
        }
    }

    protected override void Unloaded()
    {
        lock (_lock)
        {
            _loaded = false;
            _data.Unsubscribe(this);
        }
    }

    private void OnWatchlistChanged(object? sender, EventArgs e)
    {
        Rebuild();
        RaiseItemsChanged();
    }

    private void Rebuild()
    {
        var quotes = _settings.Groups
            .SelectMany(g => g.Symbols.Select(s => new QuoteListItem(s, g.Name, _data, _settings, QuoteItemLayout.List)))
            .ToArray();

        QuoteListItem[] old;
        lock (_lock)
        {
            old = _quotes;
            _quotes = quotes;
            if (_loaded)
            {
                _data.Subscribe(this, quotes.Select(q => q.Symbol));
            }
        }

        foreach (var item in old)
        {
            item.Dispose();
        }
    }

    public void Dispose()
    {
        _settings.WatchlistChanged -= OnWatchlistChanged;
        _data.Unsubscribe(this);
        foreach (var item in _quotes)
        {
            item.Dispose();
        }
    }
}
