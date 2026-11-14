using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using PricePin.Core.Feeds;
using PricePin.Core.Market;

namespace PricePin.Core.Tests;

/// <summary>Runs a real feed against a local WebSocket server.</summary>
public class WebSocketFeedTests
{
    private static (HttpListener Listener, Uri Uri) StartServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var http = new HttpListener();
        http.Prefixes.Add($"http://127.0.0.1:{port}/");
        http.Start();
        return (http, new Uri($"ws://127.0.0.1:{port}/"));
    }

    private static async Task<WebSocket> AcceptAsync(HttpListener http)
    {
        var ctx = await http.GetContextAsync();
        var wsCtx = await ctx.AcceptWebSocketAsync(null);
        return wsCtx.WebSocket;
    }

    private static Task SendAsync(WebSocket ws, string text) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    public async Task Streams_ticks_then_reports_stale_when_server_goes_quiet()
    {
        var (http, uri) = StartServer();
        using var _ = http;
        var feed = new BinanceFeed { Endpoint = uri, StaleAfter = TimeSpan.FromSeconds(3) };

        var server = Task.Run(async () =>
        {
            var ws = await AcceptAsync(http);
            var buf = new byte[4096];
            var sub = await ws.ReceiveAsync(buf, CancellationToken.None); // SUBSCRIBE
            Assert.Contains("btcusdt@aggTrade", Encoding.UTF8.GetString(buf, 0, sub.Count));
            await SendAsync(ws, """{"result":null,"id":1}""");
            await SendAsync(ws, """{"stream":"btcusdt@aggTrade","data":{"e":"aggTrade","s":"BTCUSDT","p":"100.5","T":1}}""");
            await Task.Delay(TimeSpan.FromSeconds(10)); // then say nothing
            return ws;
        });

        var ticks = new List<Ticker>();
        var live = false;
        var ex = await Assert.ThrowsAsync<FeedException>(() =>
            feed.RunSessionAsync([new MarketSymbol("BTC", "USD")], ticks.Add, () => live = true, CancellationToken.None));

        Assert.True(live);
        Assert.Equal(100.5m, Assert.Single(ticks).Last);
        Assert.Equal(FeedFailure.Stale, ex.Failure);
    }

    [Fact]
    public async Task Server_close_is_a_transient_failure()
    {
        var (http, uri) = StartServer();
        using var _ = http;
        var feed = new KrakenFeed { Endpoint = uri };

        var server = Task.Run(async () =>
        {
            var ws = await AcceptAsync(http);
            var buf = new byte[4096];
            await ws.ReceiveAsync(buf, CancellationToken.None);
            await ws.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "maintenance", CancellationToken.None);
        });

        var ex = await Assert.ThrowsAsync<FeedException>(() =>
            feed.RunSessionAsync([new MarketSymbol("BTC", "USD")], _ => { }, () => { }, CancellationToken.None));
        Assert.Equal(FeedFailure.Transient, ex.Failure);
        await server;
    }

    [Fact]
    public async Task Cancelling_ends_the_session_quietly()
    {
        var (http, uri) = StartServer();
        using var _ = http;
        var feed = new CoinbaseFeed { Endpoint = uri };
        using var cts = new CancellationTokenSource();

        var server = Task.Run(async () =>
        {
            var ws = await AcceptAsync(http);
            await SendAsync(ws, """{"channel":"heartbeats","events":[]}""");
            await Task.Delay(500);
            cts.Cancel();
        });

        await feed.RunSessionAsync([new MarketSymbol("BTC", "USD")], _ => { }, () => { }, cts.Token);
        await server;
    }

    [Fact]
    public async Task Unreachable_server_is_transient()
    {
        var feed = new BinanceFeed { Endpoint = new Uri("ws://127.0.0.1:1/") };
        var ex = await Assert.ThrowsAsync<FeedException>(() =>
            feed.RunSessionAsync([new MarketSymbol("BTC", "USD")], _ => { }, () => { }, CancellationToken.None));
        Assert.Equal(FeedFailure.Transient, ex.Failure);
    }
}
