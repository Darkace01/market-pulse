using System;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

/// <summary>
/// One Dock band. Each item on its page renders as a button in the band.
/// Prices are only fetched while CmdPal is actually rendering the band.
/// </summary>
internal sealed partial class MarketBand : CommandItem, IDisposable
{
    private readonly string _bandTitle;
    private readonly MarketBandPage _page;

    public MarketBand(string id, string bandTitle, IListItem[] items, MarketSymbol[] symbols, MarketDataService data, Action? onLoaded = null, Action? onUnloaded = null)
    {
        _bandTitle = bandTitle;
        _page = new MarketBandPage(id, bandTitle, items, symbols, data, onLoaded, onUnloaded);
        Icon = QuoteListItem.NeutralIcon;
    }

    public override string Title => _bandTitle;

    public override ICommand? Command => _page;

    public void Dispose()
    {
        foreach (var item in _page.GetItems().OfType<IDisposable>())
        {
            item.Dispose();
        }

        _page.Dispose();
    }
}

#pragma warning disable SA1402 // The page is an implementation detail of the band.

internal sealed partial class MarketBandPage : OnLoadDynamicListPage, IDisposable
{
    private readonly IListItem[] _items;
    private readonly MarketSymbol[] _symbols;
    private readonly MarketDataService _data;
    private readonly Action? _onLoaded;
    private readonly Action? _onUnloaded;

    public MarketBandPage(string id, string name, IListItem[] items, MarketSymbol[] symbols, MarketDataService data, Action? onLoaded, Action? onUnloaded)
    {
        Id = id;
        Name = name;
        Title = name;
        _items = items;
        _symbols = symbols;
        _data = data;
        _onLoaded = onLoaded;
        _onUnloaded = onUnloaded;
    }

    public override IListItem[] GetItems()
    {
        Diagnostics.Log($"band {Id}: GetItems -> {_items.Length} item(s)");
        return _items;
    }

    // A band has no search box, so its contents never depend on the query.
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
    }

    protected override void Loaded()
    {
        Diagnostics.Log($"band {Id}: loaded");
        _data.Subscribe(this, _symbols);
        _onLoaded?.Invoke();
    }

    protected override void Unloaded()
    {
        Diagnostics.Log($"band {Id}: unloaded");
        _data.Unsubscribe(this);
        _onUnloaded?.Invoke();
    }

    public void Dispose() => _data.Unsubscribe(this);
}

#pragma warning restore SA1402
