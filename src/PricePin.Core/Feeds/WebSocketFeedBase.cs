using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>
/// Shared plumbing for WebSocket feeds: connect, subscribe, read whole messages,
/// detect silent connections, and turn every failure into a <see cref="FeedException"/>.
/// A subclass only says where to connect, what to send and how to read a message.
/// </summary>
public abstract class WebSocketFeedBase : IMarketFeed
{
    /// <summary>No message for this long means the connection is dead even if the socket looks open.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Reconnect before exchanges force a disconnect (Binance does at 24h).</summary>
    public TimeSpan MaxSessionLength { get; init; } = TimeSpan.FromHours(23);

    public abstract string Name { get; }

    public bool IsStreaming => true;

    protected abstract Uri BuildUri(IReadOnlyList<MarketSymbol> symbols);

    /// <summary>Text frames to send right after connecting (subscriptions).</summary>
    protected abstract IEnumerable<string> BuildSubscribeMessages(IReadOnlyList<MarketSymbol> symbols);

    /// <summary>Reads one message and reports any tickers in it.</summary>
    protected internal abstract void ParseMessage(ReadOnlySpan<byte> utf8Json, Action<Ticker> emit);

    public async Task RunSessionAsync(
        IReadOnlyList<MarketSymbol> symbols,
        Action<Ticker> onTick,
        Action onLive,
        CancellationToken cancellationToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        session.CancelAfter(MaxSessionLength);

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        ws.Options.CollectHttpResponseDetails = true;
        ws.Options.SetRequestHeader("User-Agent", "PricePin");

        await ConnectAsync(ws, BuildUri(symbols), session.Token).ConfigureAwait(false);

        foreach (var message in BuildSubscribeMessages(symbols))
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, session.Token)
                .ConfigureAwait(false);
        }

        // Watchdog: abort the socket if the exchange goes quiet.
        var lastMessage = Environment.TickCount64;
        var stale = false;
        using var watchdog = new Timer(_ =>
        {
            if (Environment.TickCount64 - Volatile.Read(ref lastMessage) > StaleAfter.TotalMilliseconds)
            {
                stale = true;
                ws.Abort();
            }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        var buffer = new ArrayBufferWriter<byte>(16 * 1024);
        var live = false;

        void Emit(Ticker t)
        {
            if (!live)
            {
                live = true;
                onLive();
            }
            onTick(t);
        }

        try
        {
            while (true)
            {
                buffer.ResetWrittenCount();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer.GetMemory(8 * 1024), session.Token).ConfigureAwait(false);
                    buffer.Advance(result.Count);
                }
                while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseQuietlyAsync(ws).ConfigureAwait(false); // complete the close handshake
                    throw new FeedException(FeedFailure.Transient,
                        $"{Name} closed the connection ({ws.CloseStatus}: {ws.CloseStatusDescription})");
                }

                Volatile.Write(ref lastMessage, Environment.TickCount64);
                try
                {
                    ParseMessage(buffer.WrittenSpan, Emit);
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or OverflowException or InvalidOperationException or KeyNotFoundException)
                {
                    // One odd message must not kill the stream.
                    System.Diagnostics.Debug.WriteLine($"{Name}: skipped unreadable message: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CloseQuietlyAsync(ws).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (stale)
        {
            throw new FeedException(FeedFailure.Stale, $"{Name} went quiet for {StaleAfter.TotalSeconds:0}s", ex);
        }
        catch (OperationCanceledException)
        {
            // MaxSessionLength reached: end this session so the service reconnects with a fresh one.
            await CloseQuietlyAsync(ws).ConfigureAwait(false);
            throw new FeedException(FeedFailure.Transient, $"{Name} session refreshed");
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            if (stale)
                throw new FeedException(FeedFailure.Stale, $"{Name} went quiet for {StaleAfter.TotalSeconds:0}s", ex);
            throw new FeedException(FeedFailure.Transient, $"{Name} connection lost: {ex.Message}", ex);
        }
    }

    private async Task ConnectAsync(ClientWebSocket ws, Uri uri, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await ws.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FeedException(FeedFailure.Transient, $"{Name} did not answer in time");
        }
        catch (WebSocketException ex)
        {
            throw ws.HttpStatusCode is HttpStatusCode.UnavailableForLegalReasons or HttpStatusCode.Forbidden
                ? new FeedException(FeedFailure.Blocked, $"{Name} is not available in your region (HTTP {(int)ws.HttpStatusCode})", ex)
                : new FeedException(FeedFailure.Transient, $"Could not reach {Name}: {ex.Message}", ex);
        }
    }

    private static async Task CloseQuietlyAsync(ClientWebSocket ws)
    {
        if (ws.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            // Closing politely is best effort.
        }
    }
}
