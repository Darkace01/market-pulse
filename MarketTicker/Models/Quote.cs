using System;
using System.Globalization;

namespace MarketTicker;

internal enum QuoteState
{
    Loading,
    Live,
    Error,
}

/// <summary>An immutable price snapshot for one instrument.</summary>
internal sealed record Quote(
    string Key,
    double Price,
    double ChangePercent,
    string Currency,
    DateTimeOffset UpdatedAt,
    QuoteState State = QuoteState.Live,
    string? Error = null)
{
    public static Quote Loading(string key) => new(key, double.NaN, double.NaN, string.Empty, DateTimeOffset.MinValue, QuoteState.Loading);

    public static Quote Failed(string key, string error, Quote? previous) =>
        previous is { State: QuoteState.Live }
            ? previous with { State = QuoteState.Error, Error = error }
            : new(key, double.NaN, double.NaN, string.Empty, DateTimeOffset.Now, QuoteState.Error, error);

    public bool HasPrice => !double.IsNaN(Price);

    public bool IsUp => ChangePercent >= 0;

    public string FormatPrice(MarketSymbol symbol)
    {
        if (!HasPrice)
        {
            return "—";
        }

        var abs = Math.Abs(Price);
        var format = symbol.IsCurrencyPair ? "N4"
            : abs >= 1 ? "N2"
            : abs >= 0.01 ? "N4"
            : "0.########";
        return Price.ToString(format, CultureInfo.CurrentCulture);
    }

    public string FormatChange()
    {
        if (double.IsNaN(ChangePercent))
        {
            return State == QuoteState.Error ? "offline" : "…";
        }

        var arrow = IsUp ? "▲" : "▼";
        return string.Create(CultureInfo.CurrentCulture, $"{arrow} {Math.Abs(ChangePercent):0.00}%");
    }
}
