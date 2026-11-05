using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace MarketTicker;

/// <summary>
/// Streams live crypto prices from Binance's public combined mini-ticker stream (one push per second per pair).
/// No API key needed. The connection only exists while at least one crypto symbol is being watched.
/// </summary>
internal sealed class BinanceStreamClient : IDisposable
{
    private const string StreamHost = "wss://stream.binance.com:9443/stream?streams=";

    private readonly Action<Quote> _onQuote;
    private readonly Action<string, string> _onError;
    private readonly Lock _lock = new();
    private string[] _pairs = [];
    private CancellationTokenSource? _connectionCts;
    private Task? _loop;
    private bool _disposed;

    public BinanceStreamClient(Action<Quote> onQuote, Action<string, string> onError)
    {
        _onQuote = onQuote;
        _onError = onError;
    }

    /// <summary>Replaces the watched pairs, reconnecting only when the set actually changes.</summary>
    public void SetPairs(IEnumerable<string> pairs)
    {
        var next = pairs.Select(p => p.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray();
        lock (_lock)
        {
            if (_disposed || next.SequenceEqual(_pairs))
            {
                return;
            }

            _pairs = next;
            _connectionCts?.Cancel();
            _connectionCts?.Dispose();
            _connectionCts = null;

            if (next.Length > 0)
            {
                _connectionCts = new CancellationTokenSource();
                var ct = _connectionCts.Token;
                var previous = _loop;
                _loop = Task.Run(async () =>
                {
                    // Let the old socket finish closing before opening a new one.
                    if (previous is not null)
                    {
                        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    }

                    await RunAsync(next, ct).ConfigureAwait(false);
                });
            }
        }
    }

    private async Task RunAsync(string[] pairs, CancellationToken ct)
    {
        var url = new Uri(StreamHost + string.Join("/", pairs.Select(p => p.ToLowerInvariant() + "@miniTicker")));
        var backoff = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await socket.ConnectAsync(url, ct).ConfigureAwait(false);
                backoff = TimeSpan.FromSeconds(1);
                await ReceiveAsync(socket, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                ExtensionHost.LogMessage(new LogMessage { Message = $"MarketTicker: Binance stream error: {ex.Message}" });
                foreach (var pair in pairs)
                {
                    _onError(pair, ex.Message);
                }
            }

            try
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            buffer.Clear();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.GetMemory(4096), ct).ConfigureAwait(false);
                buffer.Advance(result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            if (TryParse(buffer.WrittenSpan, out var quote))
            {
                _onQuote(quote);
            }
        }
    }

    // {"stream":"btcusdt@miniTicker","data":{"e":"24hrMiniTicker","s":"BTCUSDT","c":"80390.29","o":"78619.65",...}}
    private static bool TryParse(ReadOnlySpan<byte> json, out Quote quote)
    {
        quote = null!;
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("s", out var s) ||
                !TryGetDecimalString(data, "c", out var close))
            {
                return false;
            }

            var change = TryGetDecimalString(data, "o", out var open) && open > 0 ? (close - open) / open * 100 : double.NaN;
            quote = new Quote("crypto:" + s.GetString(), close, change, string.Empty, DateTimeOffset.Now);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetDecimalString(JsonElement e, string name, out double value)
    {
        value = double.NaN;
        return e.TryGetProperty(name, out var v) &&
               v.ValueKind == JsonValueKind.String &&
               double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _connectionCts?.Cancel();
            _connectionCts?.Dispose();
            _connectionCts = null;
        }
    }
}
