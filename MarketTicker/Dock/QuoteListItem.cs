using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

internal enum QuoteItemLayout
{
    /// <summary>Rendered as a Dock button.</summary>
    Dock,

    /// <summary>Rendered as a row in the Markets list page.</summary>
    List,
}

/// <summary>One instrument, kept in sync with <see cref="MarketDataService"/>.</summary>
internal sealed partial class QuoteListItem : ListItem, IDisposable
{
    internal static readonly IconInfo UpIcon = new("📈");
    internal static readonly IconInfo DownIcon = new("📉");
    internal static readonly IconInfo NeutralIcon = new("");

    private static readonly OptionalColor UpColor = ColorHelpers.FromRgb(0x16, 0xA3, 0x4A);
    private static readonly OptionalColor DownColor = ColorHelpers.FromRgb(0xDC, 0x26, 0x26);

    private readonly MarketSymbol _symbol;
    private readonly string _group;
    private readonly MarketDataService _data;
    private readonly SettingsManager _settings;
    private readonly QuoteItemLayout _layout;
    private readonly CopyTextCommand _copyPrice = new(string.Empty) { Name = "Copy price" };
    private readonly Tag _priceTag = new();
    private readonly Tag _changeTag = new();
    private bool _disposed;

    public QuoteListItem(MarketSymbol symbol, string group, MarketDataService data, SettingsManager settings, QuoteItemLayout layout)
        : base(new OpenUrlCommand(symbol.WebUrl) { Name = "Open chart", Result = CommandResult.Dismiss() })
    {
        _symbol = symbol;
        _group = group;
        _data = data;
        _settings = settings;
        _layout = layout;

        MoreCommands =
        [
            new CommandContextItem(_copyPrice),
            new CommandContextItem(new RemoveFromWatchlistCommand(settings, group, symbol)),
        ];

        if (layout == QuoteItemLayout.List)
        {
            Section = group;
            Tags = [_priceTag, _changeTag];
        }

        _data.QuoteUpdated += OnQuoteUpdated;
        _settings.DisplayChanged += OnDisplayChanged;
        Render(_data.GetQuote(symbol.Key));
    }

    public MarketSymbol Symbol => _symbol;

    public string Group => _group;

    private void OnQuoteUpdated(object? sender, Quote quote)
    {
        if (quote.Key == _symbol.Key)
        {
            Render(quote);
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Render(_data.GetQuote(_symbol.Key));

    private void Render(Quote quote)
    {
        if (_disposed)
        {
            return;
        }

        var price = quote.FormatPrice(_symbol);
        var change = quote.FormatChange();
        _copyPrice.Text = quote.HasPrice ? price : string.Empty;

        if (_layout == QuoteItemLayout.List)
        {
            Title = _symbol.Label;
            Subtitle = quote.State == QuoteState.Error ? $"{_symbol.Key} · {quote.Error}" : _symbol.Key;
            Icon = TrendIcon(quote);
            _priceTag.Text = price;
            _changeTag.Text = change;
            _changeTag.Foreground = !quote.HasPrice || double.IsNaN(quote.ChangePercent) ? ColorHelpers.NoColor() : quote.IsUp ? UpColor : DownColor;
            return;
        }

        Icon = _settings.ShowTrendIcons ? TrendIcon(quote) : null;
        (Title, Subtitle) = FormatDock(_symbol, quote, _settings.DockStyle);
    }

    internal static (string Title, string Subtitle) FormatDock(MarketSymbol symbol, Quote quote, DockStyle style)
    {
        var price = $"{symbol.Label} {quote.FormatPrice(symbol)}";
        return style switch
        {
            DockStyle.OneLine => ($"{price} {quote.FormatChange()}", string.Empty),
            DockStyle.PriceOnly => (price, string.Empty),
            _ => (price, quote.FormatChange()),
        };
    }

    internal static IconInfo TrendIcon(Quote quote) =>
        !quote.HasPrice || double.IsNaN(quote.ChangePercent) ? NeutralIcon : quote.IsUp ? UpIcon : DownIcon;

    public void Dispose()
    {
        _disposed = true;
        _data.QuoteUpdated -= OnQuoteUpdated;
        _settings.DisplayChanged -= OnDisplayChanged;
    }
}
