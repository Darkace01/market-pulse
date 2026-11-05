using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

/// <summary>A single Dock button that cycles through every watched market — the most space-efficient band.</summary>
internal sealed partial class RotatingTickerItem : ListItem, IDisposable
{
    private readonly IReadOnlyList<MarketSymbol> _symbols;
    private readonly MarketDataService _data;
    private readonly SettingsManager _settings;
    private readonly Lock _lock = new();
    private Timer? _timer;
    private int _index;

    public RotatingTickerItem(IReadOnlyList<MarketSymbol> symbols, MarketDataService data, SettingsManager settings)
    {
        _symbols = symbols;
        _data = data;
        _settings = settings;
        Command = new OpenDynamicUrlCommand(() => Current?.WebUrl);
        _data.QuoteUpdated += OnQuoteUpdated;
        _settings.DisplayChanged += OnDisplayChanged;
        Render();
    }

    private MarketSymbol? Current => _symbols.Count == 0 ? null : _symbols[_index % _symbols.Count];

    public void Start()
    {
        lock (_lock)
        {
            _timer ??= new Timer(_ => Advance(), null, _settings.RotateInterval, _settings.RotateInterval);
        }

        Render();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Advance()
    {
        Interlocked.Increment(ref _index);
        Render();
    }

    private void OnQuoteUpdated(object? sender, Quote quote)
    {
        if (quote.Key == Current?.Key)
        {
            Render();
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            _timer?.Change(_settings.RotateInterval, _settings.RotateInterval);
        }

        Render();
    }

    private void Render()
    {
        if (Current is not { } symbol)
        {
            Title = "No markets";
            Subtitle = "Add some in settings";
            return;
        }

        var quote = _data.GetQuote(symbol.Key);
        Icon = _settings.ShowTrendIcons ? QuoteListItem.TrendIcon(quote) : null;
        (Title, Subtitle) = QuoteListItem.FormatDock(symbol, quote, _settings.DockStyle);
    }

    public void Dispose()
    {
        Stop();
        _data.QuoteUpdated -= OnQuoteUpdated;
        _settings.DisplayChanged -= OnDisplayChanged;
    }
}
