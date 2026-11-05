// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

internal enum DockStyle
{
    /// <summary>Price on the title row, change on the subtitle row.</summary>
    TwoLine,

    /// <summary>Price and change on one row; best for the compact Dock size.</summary>
    OneLine,

    /// <summary>Price only.</summary>
    PriceOnly,
}

internal sealed partial class SettingsManager : JsonSettingsManager
{
    private static readonly string _namespace = "MarketTicker";

    private readonly TextSetting _watchlist = new(
        Namespaced(nameof(WatchlistText)),
        "Watchlists",
        "One group per line as 'Group: SYMBOL, SYMBOL as Label'. Stocks, indices, FX and futures use Yahoo Finance symbols (AAPL, ^GSPC, GBPUSD=X, GC=F). Prefix crypto with 'crypto:' for live Binance prices (crypto:BTC, crypto:ETHBTC). Each group becomes a Dock band.",
        Watchlist.DefaultText)
    {
        Multiline = true,
    };

    private readonly ChoiceSetSetting _refreshSeconds = new(
        Namespaced(nameof(PollInterval)),
        "Stock refresh interval",
        "How often stocks, indices, FX and commodities are refreshed. Crypto always streams live.",
        [
            new ChoiceSetSetting.Choice("5 seconds", "5"),
            new ChoiceSetSetting.Choice("10 seconds", "10"),
            new ChoiceSetSetting.Choice("15 seconds", "15"),
            new ChoiceSetSetting.Choice("30 seconds", "30"),
            new ChoiceSetSetting.Choice("1 minute", "60"),
        ])
    {
        Value = "15",
    };

    private readonly ChoiceSetSetting _dockStyle = new(
        Namespaced(nameof(DockStyle)),
        "Dock layout",
        "How each price is laid out on the Dock",
        [
            new ChoiceSetSetting.Choice("Price + change below", nameof(DockStyle.TwoLine)),
            new ChoiceSetSetting.Choice("Price + change on one line (compact)", nameof(DockStyle.OneLine)),
            new ChoiceSetSetting.Choice("Price only", nameof(DockStyle.PriceOnly)),
        ]);

    private readonly ToggleSetting _trendIcons = new(
        Namespaced(nameof(ShowTrendIcons)),
        "Show trend icons on the Dock",
        "Show a 📈 / 📉 icon next to each price",
        true);

    private readonly ChoiceSetSetting _rotateSeconds = new(
        Namespaced(nameof(RotateInterval)),
        "Rotating ticker speed",
        "How long the 'Rotating ticker' band shows each market",
        [
            new ChoiceSetSetting.Choice("3 seconds", "3"),
            new ChoiceSetSetting.Choice("5 seconds", "5"),
            new ChoiceSetSetting.Choice("10 seconds", "10"),
        ])
    {
        Value = "5",
    };

    private string _lastWatchlist;

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_watchlist);
        Settings.Add(_refreshSeconds);
        Settings.Add(_dockStyle);
        Settings.Add(_trendIcons);
        Settings.Add(_rotateSeconds);

        // Load settings from file upon initialization
        LoadSettings();
        _lastWatchlist = WatchlistText;

        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            RaiseChanged();
        };
    }

    /// <summary>Raised when the set of groups or symbols changes.</summary>
    public event EventHandler? WatchlistChanged;

    /// <summary>Raised when any presentation or refresh setting changes.</summary>
    public event EventHandler? DisplayChanged;

    public string WatchlistText => _watchlist.Value ?? string.Empty;

    public IReadOnlyList<WatchlistGroup> Groups => Watchlist.Parse(WatchlistText);

    public TimeSpan PollInterval => TimeSpan.FromSeconds(ParseInt(_refreshSeconds.Value, 15, 5));

    public TimeSpan RotateInterval => TimeSpan.FromSeconds(ParseInt(_rotateSeconds.Value, 5, 2));

    public DockStyle DockStyle => Enum.TryParse<DockStyle>(_dockStyle.Value, out var style) ? style : DockStyle.TwoLine;

    public bool ShowTrendIcons => _trendIcons.Value;

    public void AddSymbol(string groupName, MarketSymbol symbol) => SetWatchlist(Watchlist.Add(WatchlistText, groupName, symbol));

    public void RemoveSymbol(string groupName, string symbolKey) => SetWatchlist(Watchlist.Remove(WatchlistText, groupName, symbolKey));

    internal static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("MarketTicker");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "settings.json");
    }

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private static int ParseInt(string? value, int fallback, int min) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(n, min) : fallback;

    private void SetWatchlist(string text)
    {
        _watchlist.Value = text;
        SaveSettings();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var current = WatchlistText;
        if (!string.Equals(current, _lastWatchlist, StringComparison.Ordinal))
        {
            _lastWatchlist = current;
            WatchlistChanged?.Invoke(this, EventArgs.Empty);
        }

        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }
}
