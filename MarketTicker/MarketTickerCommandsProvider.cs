// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

public partial class MarketTickerCommandsProvider : CommandProvider
{
    private const string BandIdPrefix = "com.darkace.marketticker.band.";

    private readonly ICommandItem[] _commands;
    private readonly SettingsManager _settingsManager = new();
    private readonly MarketDataService _data = new();
    private readonly MarketsPage _marketsPage;
    private readonly SearchMarketsPage _searchPage;
    private readonly Lock _bandsLock = new();
    private MarketBand[] _bands = [];
    private bool _disposed;

    public MarketTickerCommandsProvider()
    {
        DisplayName = "Market Pulse";
        Id = "com.darkace.marketticker";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _searchPage = new SearchMarketsPage(_data, _settingsManager);
        _marketsPage = new MarketsPage(_data, _settingsManager, _searchPage);
        _commands = [
            new CommandItem(_marketsPage)
            {
                Title = "Markets",
                Subtitle = "Live stock, index, FX, commodity and crypto prices",
                MoreCommands = [new CommandContextItem(_searchPage), new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
            new CommandItem(_searchPage)
            {
                Title = "Search markets",
                Subtitle = "Find a symbol and add it to a watchlist",
            },
        ];

        Settings = _settingsManager.Settings;

        _data.PollInterval = _settingsManager.PollInterval;
        _settingsManager.DisplayChanged += OnDisplayChanged;
        _settingsManager.WatchlistChanged += OnWatchlistChanged;
        RebuildBands();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    /// <summary>
    /// Offered bands: a rotating ticker, every market in one strip, one band per watchlist group,
    /// and one band per individual market. Users pick any of them from the Dock's edit mode.
    /// IDs derive from names/symbols so pins survive restarts and watchlist edits.
    /// </summary>
    public override ICommandItem[] GetDockBands()
    {
        lock (_bandsLock)
        {
            Diagnostics.Log($"GetDockBands -> {_bands.Length} band(s)");
            return _bands;
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        var interval = _settingsManager.PollInterval;
        if (_data.PollInterval != interval)
        {
            _data.PollInterval = interval;
            _data.RefreshNow();
        }
    }

    private void OnWatchlistChanged(object? sender, EventArgs e)
    {
        if (RebuildBands())
        {
            RaiseItemsChanged();
        }
    }

    private bool RebuildBands()
    {
        var groups = _settingsManager.Groups;
        var all = groups.SelectMany(g => g.Symbols).DistinctBy(s => s.Key).ToArray();
        var bands = new List<MarketBand>();

        var rotator = new RotatingTickerItem(all, _data, _settingsManager);
        bands.Add(new MarketBand(BandIdPrefix + "rotating", "Market Pulse: rotating", [rotator], all, _data, rotator.Start, rotator.Stop));

        bands.Add(CreateBand("all", "Market Pulse: all markets", groups.SelectMany(g => g.Symbols.Select(s => (s, g.Name))).DistinctBy(p => p.s.Key)));

        foreach (var group in groups)
        {
            bands.Add(CreateBand("group." + group.Slug, $"Market Pulse: {group.Name}", group.Symbols.Select(s => (s, group.Name))));
        }

        foreach (var group in groups)
        {
            foreach (var symbol in group.Symbols)
            {
                if (!bands.Any(b => b.Command?.Id == BandIdPrefix + "symbol." + symbol.Key.ToLowerInvariant()))
                {
                    bands.Add(CreateBand("symbol." + symbol.Key.ToLowerInvariant(), $"Market Pulse: {symbol.Label}", [(symbol, group.Name)]));
                }
            }
        }

        MarketBand[] old;
        lock (_bandsLock)
        {
            if (_disposed)
            {
                foreach (var band in bands)
                {
                    band.Dispose();
                }

                return false;
            }

            old = _bands;
            _bands = [.. bands];
        }

        foreach (var band in old)
        {
            band.Dispose();
        }

        return true;
    }

    private MarketBand CreateBand(string id, string title, IEnumerable<(MarketSymbol Symbol, string Group)> entries)
    {
        var list = entries.ToArray();
        var items = list.Select(e => (IListItem)new QuoteListItem(e.Symbol, e.Group, _data, _settingsManager, QuoteItemLayout.Dock)).ToArray();
        return new MarketBand(BandIdPrefix + id, title, items, list.Select(e => e.Symbol).ToArray(), _data);
    }

    public override void Dispose()
    {
        lock (_bandsLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var band in _bands)
            {
                band.Dispose();
            }

            _bands = [];
        }

        _settingsManager.DisplayChanged -= OnDisplayChanged;
        _settingsManager.WatchlistChanged -= OnWatchlistChanged;
        _marketsPage.Dispose();
        _searchPage.Dispose();
        _data.Dispose();
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}
