using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

/// <summary>
/// Central quote cache and feed coordinator.
/// Bands and pages <see cref="Subscribe"/> while CmdPal is rendering them; only the union of
/// subscribed symbols is fetched, so an unpinned band costs nothing.
/// </summary>
internal sealed partial class MarketDataService : IDisposable
{
    private readonly ConcurrentDictionary<string, Quote> _quotes = new();
    private readonly Dictionary<object, MarketSymbol[]> _subscribers = [];
    private readonly Lock _lock = new();
    private readonly HttpClient _http;
    private readonly YahooFinanceClient _yahoo;
    private readonly BinanceStreamClient _binance;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _pollNow = new(0);
    private string[] _yahooSymbols = [];
    private Task? _pollLoop;

    public MarketDataService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // Yahoo rejects requests without a browser-like user agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MarketTicker/1.0");
        _yahoo = new YahooFinanceClient(_http);
        _binance = new BinanceStreamClient(Publish, (pair, error) => PublishError("crypto:" + pair, error));
    }

    /// <summary>Raised on a background thread whenever a quote changes.</summary>
    public event EventHandler<Quote>? QuoteUpdated;

    /// <summary>Gets or sets how often Yahoo-sourced symbols are polled.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public YahooFinanceClient Yahoo => _yahoo;

    public Quote GetQuote(string key) => _quotes.TryGetValue(key, out var q) ? q : Quote.Loading(key);

    public void Subscribe(object owner, IEnumerable<MarketSymbol> symbols)
    {
        lock (_lock)
        {
            _subscribers[owner] = symbols.ToArray();
            Reconcile();
        }
    }

    public void Unsubscribe(object owner)
    {
        lock (_lock)
        {
            if (_subscribers.Remove(owner))
            {
                Reconcile();
            }
        }
    }

    /// <summary>Forces an immediate refresh of polled symbols (e.g. after the interval setting changes).</summary>
    public void RefreshNow() => _pollNow.Release();

    private void Reconcile()
    {
        var all = _subscribers.Values.SelectMany(s => s).ToArray();
        _binance.SetPairs(all.Where(s => s.Source == MarketSource.Binance).Select(s => s.Symbol));

        var yahoo = all.Where(s => s.Source == MarketSource.Yahoo).Select(s => s.Symbol).Distinct().Order(StringComparer.Ordinal).ToArray();
        var added = yahoo.Except(_yahooSymbols).Any();
        _yahooSymbols = yahoo;

        if (yahoo.Length > 0)
        {
            _pollLoop ??= Task.Run(() => PollLoopAsync(_cts.Token));
            if (added)
            {
                RefreshNow();
            }
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string[] symbols;
            lock (_lock)
            {
                symbols = _yahooSymbols;
            }

            if (symbols.Length > 0)
            {
                try
                {
                    var quotes = await _yahoo.GetQuotesAsync(symbols, ct).ConfigureAwait(false);
                    foreach (var quote in quotes)
                    {
                        Publish(quote);
                    }

                    foreach (var missing in symbols.Except(quotes.Select(q => q.Key), StringComparer.OrdinalIgnoreCase))
                    {
                        PublishError(missing, "Symbol not found");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ExtensionHost.LogMessage(new LogMessage { Message = $"MarketTicker: Yahoo poll failed: {ex.Message}" });
                    foreach (var symbol in symbols)
                    {
                        PublishError(symbol, ex.Message);
                    }
                }
            }

            try
            {
                // Sleep until the next tick, or wake early when a new symbol is subscribed.
                await _pollNow.WaitAsync(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Publish(Quote quote)
    {
        _quotes[quote.Key] = quote;
        QuoteUpdated?.Invoke(this, quote);
    }

    private void PublishError(string key, string error)
    {
        _quotes.TryGetValue(key, out var previous);
        Publish(Quote.Failed(key, error, previous));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _binance.Dispose();
        _http.Dispose();
        _cts.Dispose();
        _pollNow.Dispose();
    }
}
