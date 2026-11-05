using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MarketTicker;

/// <summary>A named group of instruments. Each group becomes its own Dock band.</summary>
internal sealed record WatchlistGroup(string Name, IReadOnlyList<MarketSymbol> Symbols)
{
    /// <summary>Gets a stable identifier derived from the group name, so Dock pins survive edits to the symbols.</summary>
    public string Slug
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var c in Name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }
            }

            return sb.ToString().Trim('-');
        }
    }
}

/// <summary>
/// Parses and edits the watchlist text from settings. One group per line:
/// <code>
/// Indices: ^GSPC as S&amp;P 500, ^FTSE as FTSE 100
/// Crypto: crypto:BTC, crypto:ETH
/// </code>
/// Lines without a "Name:" prefix go into a group called "Watchlist". Lines starting with # are ignored.
/// </summary>
internal static class Watchlist
{
    public const string DefaultGroupName = "Watchlist";

    public const string DefaultText =
        """
        Indices: ^GSPC as S&P 500, ^IXIC as Nasdaq, ^FTSE as FTSE 100
        Tech: AAPL, MSFT, NVDA
        Crypto: crypto:BTC, crypto:ETH, crypto:SOL
        FX: GBPUSD=X as GBP/USD, EURUSD=X as EUR/USD
        Commodities: GC=F as Gold, CL=F as Oil
        """;

    public static IReadOnlyList<WatchlistGroup> Parse(string? text)
    {
        var groups = new List<(string Name, List<MarketSymbol> Symbols)>();
        foreach (var rawLine in (text ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var name = DefaultGroupName;
            var body = line;

            // "crypto:BTC" (no space after the colon) is a symbol, not a group called "crypto";
            // "Crypto: crypto:BTC" is a group called "Crypto".
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var isCryptoSymbol = colon > 0 &&
                line[..colon].Trim().Equals("crypto", StringComparison.OrdinalIgnoreCase) &&
                colon + 1 < line.Length && !char.IsWhiteSpace(line[colon + 1]);
            if (colon > 0 && !isCryptoSymbol)
            {
                name = line[..colon].Trim();
                body = line[(colon + 1)..];
            }

            var group = groups.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (group.Symbols is null)
            {
                group = (name, []);
                groups.Add(group);
            }

            foreach (var entry in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (MarketSymbol.TryParse(entry, out var symbol) && !group.Symbols.Any(s => s.Key == symbol!.Key))
                {
                    group.Symbols.Add(symbol!);
                }
            }
        }

        return groups.Where(g => g.Symbols.Count > 0).Select(g => new WatchlistGroup(g.Name, g.Symbols)).ToList();
    }

    public static string Format(IEnumerable<WatchlistGroup> groups) =>
        string.Join("\n", groups.Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Name}: {string.Join(", ", g.Symbols.Select(s => s.ToEntry()))}")));

    public static string Add(string? text, string groupName, MarketSymbol symbol)
    {
        var groups = Parse(text).ToList();
        var index = groups.FindIndex(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            groups.Add(new WatchlistGroup(groupName, [symbol]));
        }
        else if (!groups[index].Symbols.Any(s => s.Key == symbol.Key))
        {
            groups[index] = groups[index] with { Symbols = [.. groups[index].Symbols, symbol] };
        }

        return Format(groups);
    }

    public static string Remove(string? text, string groupName, string symbolKey)
    {
        var groups = Parse(text)
            .Select(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)
                ? g with { Symbols = g.Symbols.Where(s => s.Key != symbolKey).ToList() }
                : g)
            .Where(g => g.Symbols.Count > 0);
        return Format(groups);
    }
}
