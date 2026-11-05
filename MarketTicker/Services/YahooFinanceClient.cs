using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MarketTicker;

/// <summary>A search hit from Yahoo Finance's symbol lookup.</summary>
internal sealed record SymbolSearchResult(string Symbol, string Name, string Type, string Exchange);

/// <summary>
/// Minimal client for Yahoo Finance's public, key-less endpoints.
/// Prices are near-real-time for most US listings and delayed for some exchanges.
/// </summary>
internal sealed class YahooFinanceClient(HttpClient http)
{
    // The spark endpoint accepts a batch of symbols; larger batches get truncated.
    private const int BatchSize = 20;

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken ct)
    {
        var quotes = new List<Quote>(symbols.Count);
        foreach (var batch in symbols.Chunk(BatchSize))
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/spark?symbols={string.Join(",", batch.Select(Uri.EscapeDataString))}&range=1d&interval=5m";
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            var now = DateTimeOffset.Now;
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                if (TryReadSpark(entry.Name, entry.Value, now, out var quote))
                {
                    quotes.Add(quote);
                }
            }
        }

        return quotes;
    }

    public async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        var url = $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=12&newsCount=0&listsCount=0";
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var results = new List<SymbolSearchResult>();
        if (doc.RootElement.TryGetProperty("quotes", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                var symbol = GetString(hit, "symbol");
                if (string.IsNullOrEmpty(symbol))
                {
                    continue;
                }

                results.Add(new SymbolSearchResult(
                    symbol,
                    GetString(hit, "shortname") ?? GetString(hit, "longname") ?? symbol,
                    GetString(hit, "quoteType") ?? string.Empty,
                    GetString(hit, "exchDisp") ?? GetString(hit, "exchange") ?? string.Empty));
            }
        }

        return results;
    }

    private static bool TryReadSpark(string symbol, JsonElement e, DateTimeOffset now, out Quote quote)
    {
        quote = null!;
        if (e.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var price = GetDouble(e, "fulldayPrice");
        if (double.IsNaN(price) && e.TryGetProperty("close", out var closes) && closes.ValueKind == JsonValueKind.Array)
        {
            // Intraday closes can contain nulls for gaps; take the latest real one.
            foreach (var c in closes.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.Number)
                {
                    price = c.GetDouble();
                }
            }
        }

        if (double.IsNaN(price))
        {
            return false;
        }

        var change = GetDouble(e, "fulldayChangePercent");
        if (double.IsNaN(change))
        {
            var previous = GetDouble(e, "chartPreviousClose");
            change = previous > 0 ? (price - previous) / previous * 100 : double.NaN;
        }

        quote = new Quote(symbol, price, change, string.Empty, now);
        return true;
    }

    private static double GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
