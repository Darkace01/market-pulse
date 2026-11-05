using System;
using System.Linq;

namespace MarketTicker;

internal enum MarketSource
{
    /// <summary>Stocks, indices, FX, futures and ETFs via Yahoo Finance (polled).</summary>
    Yahoo,

    /// <summary>Crypto pairs via Binance public WebSocket streams (pushed).</summary>
    Binance,
}

/// <summary>
/// A single instrument from a watchlist entry.
/// Entry syntax: <c>SYMBOL</c>, <c>SYMBOL as Label</c>, <c>crypto:BTC</c> or <c>crypto:ETHBTC as Ether/BTC</c>.
/// </summary>
internal sealed record MarketSymbol(MarketSource Source, string Symbol, string Label)
{
    private const string CryptoPrefix = "crypto:";

    // Quote assets Binance lists pairs against, longest first so "FDUSD" wins over "USD".
    private static readonly string[] CryptoQuotes = ["FDUSD", "USDT", "USDC", "TUSD", "BUSD", "EUR", "GBP", "TRY", "BRL", "JPY", "BTC", "ETH", "BNB"];

    /// <summary>Gets the key used to address this instrument across the app.</summary>
    public string Key => Source == MarketSource.Binance ? CryptoPrefix + Symbol : Symbol;

    /// <summary>Gets the entry text that round-trips through <see cref="TryParse"/>.</summary>
    public string ToEntry()
    {
        // Write USDT pairs back in the short form users type ("crypto:BTC", not "crypto:BTCUSDT").
        var key = Source == MarketSource.Binance && SplitPair(Symbol) is (var baseAsset, "USDT") ? CryptoPrefix + baseAsset : Key;
        return Label == DefaultLabel(Source, Symbol) ? key : $"{key} as {Label}";
    }

    public string WebUrl => Source == MarketSource.Binance
        ? $"https://www.binance.com/en/trade/{BaseAsset}_{QuoteAsset}"
        : $"https://finance.yahoo.com/quote/{Uri.EscapeDataString(Symbol)}";

    /// <summary>Gets a value indicating whether prices for this symbol need FX-style precision.</summary>
    public bool IsCurrencyPair => Source == MarketSource.Yahoo && Symbol.EndsWith("=X", StringComparison.OrdinalIgnoreCase);

    private string QuoteAsset => SplitPair(Symbol).Quote;

    private string BaseAsset => SplitPair(Symbol).Base;

    public static bool TryParse(string entry, out MarketSymbol? symbol)
    {
        symbol = null;
        var text = entry.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        string? label = null;
        var asIndex = text.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        if (asIndex > 0)
        {
            label = text[(asIndex + 4)..].Trim();
            text = text[..asIndex].Trim();
        }

        MarketSource source;
        if (text.StartsWith(CryptoPrefix, StringComparison.OrdinalIgnoreCase))
        {
            source = MarketSource.Binance;
            text = NormalizeCryptoPair(text[CryptoPrefix.Length..]);
            if (!text.All(char.IsAsciiLetterOrDigit))
            {
                return false;
            }
        }
        else
        {
            source = MarketSource.Yahoo;
            text = text.ToUpperInvariant();
        }

        if (text.Length == 0 || text.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        symbol = new MarketSymbol(source, text, string.IsNullOrWhiteSpace(label) ? DefaultLabel(source, text) : label);
        return true;
    }

    /// <summary>"btc" → "BTCUSDT", "eth-btc" → "ETHBTC", "SOLUSDC" stays as is.</summary>
    private static string NormalizeCryptoPair(string raw)
    {
        var pair = raw.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("/", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        foreach (var quote in CryptoQuotes)
        {
            if (pair.Length > quote.Length && pair.EndsWith(quote, StringComparison.Ordinal))
            {
                return pair;
            }
        }

        return pair.Length == 0 ? pair : pair + "USDT";
    }

    private static (string Base, string Quote) SplitPair(string pair)
    {
        foreach (var quote in CryptoQuotes)
        {
            if (pair.Length > quote.Length && pair.EndsWith(quote, StringComparison.Ordinal))
            {
                return (pair[..^quote.Length], quote);
            }
        }

        return (pair, string.Empty);
    }

    /// <summary>Compact dock-friendly label: "^GSPC" → "GSPC", "GBPUSD=X" → "GBPUSD", "BTCUSDT" → "BTC".</summary>
    private static string DefaultLabel(MarketSource source, string symbol)
    {
        if (source == MarketSource.Binance)
        {
            var (baseAsset, quote) = SplitPair(symbol);
            return quote is "USDT" or "USDC" or "FDUSD" ? baseAsset : $"{baseAsset}/{quote}";
        }

        var label = symbol.TrimStart('^');
        foreach (var suffix in new[] { "=X", "=F", "-USD" })
        {
            if (label.EndsWith(suffix, StringComparison.Ordinal))
            {
                label = label[..^suffix.Length];
            }
        }

        return label;
    }
}
