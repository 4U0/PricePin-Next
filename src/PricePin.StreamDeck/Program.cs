using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace PricePin.StreamDeck;

/// <summary>
/// Stream Deck starts the plugin with <c>-port 28196 -pluginUUID … -registerEvent registerPlugin -info {…}</c>.
/// The plugin connects back on that port, registers, then receives key events and sends images.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = ParseArgs(args);
        if (!options.TryGetValue("port", out var portText) || !int.TryParse(portText, out var port)
            || !options.TryGetValue("pluginUUID", out var uuid) || !options.TryGetValue("registerEvent", out var registerEvent))
        {
            Console.Error.WriteLine("PricePin's Stream Deck plugin is started by the Stream Deck app.");
            return 1;
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), CancellationToken.None);

        var sendLock = new SemaphoreSlim(1, 1);
        async Task Send(string json)
        {
            if (socket.State != WebSocketState.Open)
                return;
            await sendLock.WaitAsync();
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Stream Deck is closing; the receive loop ends the plugin.
            }
            finally
            {
                sendLock.Release();
            }
        }

        await Send(new JsonObject { ["event"] = registerEvent, ["uuid"] = uuid }.ToJsonString());

        await using var host = new PluginHost(Send);
        var inbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var receive = ReceiveAsync(socket, inbox.Writer);

        // One loop handles messages and the once-a-second redraw, so the host never runs on two threads.
        var tick = TimeSpan.FromSeconds(1);
        var nextTick = DateTime.UtcNow + tick;
        while (true)
        {
            var wait = nextTick - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                using var timeout = new CancellationTokenSource(wait);
                try
                {
                    if (!await inbox.Reader.WaitToReadAsync(timeout.Token))
                        break; // socket closed: Stream Deck quit or reloaded the plugin
                }
                catch (OperationCanceledException)
                {
                }
            }
            while (inbox.Reader.TryRead(out var message))
                await host.HandleAsync(message);
            if (DateTime.UtcNow >= nextTick)
            {
                await host.TickAsync();
                nextTick = DateTime.UtcNow + tick;
            }
        }
        await receive;
        return 0;
    }

    private static async Task ReceiveAsync(ClientWebSocket socket, ChannelWriter<string> inbox)
    {
        var buffer = new byte[16 * 1024];
        var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;
                inbox.TryWrite(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                message.SetLength(0);
            }
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            inbox.TryComplete();
        }
    }

    /// <summary>"-port 1 -pluginUUID x" → {port: 1, pluginUUID: x}.</summary>
    internal static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i].StartsWith('-'))
            {
                map[args[i].TrimStart('-')] = args[i + 1];
                i++;
            }
        }
        return map;
    }
}
