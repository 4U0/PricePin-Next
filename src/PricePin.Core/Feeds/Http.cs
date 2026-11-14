using System.Net;
using System.Net.Http.Headers;
using PricePin.Core.Market;

namespace PricePin.Core.Feeds;

/// <summary>One shared HttpClient for all REST calls (feeds, history, icons).</summary>
public static class Http
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PricePin", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Turns an HTTP failure into the matching <see cref="FeedException"/>.</summary>
    internal static FeedException ToFeedException(string source, Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.UnavailableForLegalReasons or HttpStatusCode.Forbidden } =>
            new FeedException(FeedFailure.Blocked, $"{source} is not available in your region", ex),
        TaskCanceledException =>
            new FeedException(FeedFailure.Transient, $"{source} did not answer in time", ex),
        _ => new FeedException(FeedFailure.Transient, $"Could not reach {source}: {ex.Message}", ex),
    };
}
