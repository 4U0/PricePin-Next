using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using PricePin.Core.Feeds;

namespace PricePin.Core.Fees;

/// <summary>Bitcoin fee rates in sat/vB, from mempool.space.</summary>
public sealed record BitcoinFees(decimal Fastest, decimal HalfHour, decimal Hour, decimal Economy);

/// <summary>Ethereum gas in gwei: the block's base fee plus the suggested tip.</summary>
public sealed record EthereumGas(decimal BaseFeeGwei, decimal PriorityFeeGwei)
{
    public decimal TotalGwei => BaseFeeGwei + PriorityFeeGwei;

    /// <summary>Cost of a plain ETH transfer (21,000 gas) in ETH.</summary>
    public decimal TransferCostEth => TotalGwei * 21_000m / 1_000_000_000m;
}

/// <summary>A fee snapshot. Either half can be missing when its source did not answer.</summary>
public sealed record FeeSnapshot(BitcoinFees? Bitcoin, EthereumGas? Ethereum, DateTimeOffset Time);

/// <summary>
/// Network fees from free public sources with no key: mempool.space for Bitcoin, and public
/// Ethereum JSON-RPC nodes (with fallbacks) for gas.
/// </summary>
public sealed class FeeService
{
    public const string MempoolUrl = "https://mempool.space/api/v1/fees/recommended";

    public static IReadOnlyList<string> EthereumRpcUrls { get; } =
    [
        "https://ethereum-rpc.publicnode.com",
        "https://eth.drpc.org",
        "https://1rpc.io/eth",
        "https://eth.llamarpc.com",
    ];

    /// <summary>Plain transfers use 21,000 gas.</summary>
    public const int TransferGas = 21_000;

    /// <summary>A typical one-input, two-output SegWit payment is about 140 vbytes.</summary>
    public const int TypicalBitcoinTxVbytes = 140;

    private int _rpcIndex;

    public async Task<FeeSnapshot> FetchAsync(CancellationToken ct = default)
    {
        var btc = FetchBitcoinAsync(ct);
        var eth = FetchEthereumAsync(ct);
        await Task.WhenAll(btc, eth).ConfigureAwait(false);
        return new FeeSnapshot(btc.Result, eth.Result, DateTimeOffset.UtcNow);
    }

    public async Task<BitcoinFees?> FetchBitcoinAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await Http.Client.GetFromJsonAsync<JsonDocument>(MempoolUrl, ct).ConfigureAwait(false);
            return doc is null ? null : ParseMempool(doc.RootElement);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return null;
        }
    }

    /// <summary>Tries the last node that worked first, then the others.</summary>
    public async Task<EthereumGas?> FetchEthereumAsync(CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < EthereumRpcUrls.Count; attempt++)
        {
            var index = (_rpcIndex + attempt) % EthereumRpcUrls.Count;
            var (gas, _) = await FetchEthereumAsync(EthereumRpcUrls[index], ct).ConfigureAwait(false);
            if (gas is not null)
            {
                _rpcIndex = index;
                return gas;
            }
        }
        return null;
    }

    /// <returns>The gas, or null and why (for the feed probe).</returns>
    internal static async Task<(EthereumGas? Gas, string? Error)> FetchEthereumAsync(string url, CancellationToken ct)
    {
        try
        {
            // One batch: the latest block (for its base fee), the node's suggested tip, and the
            // tips actually paid in the last few blocks (the median is a better guide).
            var body = """
                [{"jsonrpc":"2.0","id":1,"method":"eth_getBlockByNumber","params":["latest",false]},
                 {"jsonrpc":"2.0","id":2,"method":"eth_maxPriorityFeePerGas","params":[]},
                 {"jsonrpc":"2.0","id":3,"method":"eth_feeHistory","params":["0x5","latest",[50]]}]
                """;
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await Http.Client.PostAsync(url, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, $"HTTP {(int)response.StatusCode}");
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var gas = ParseRpcBatch(doc.RootElement);
            return (gas, gas is null ? "unexpected answer: " + (text.Length > 160 ? text[..160] : text) : null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            return (null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException or NotSupportedException or InvalidOperationException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    // {"fastestFee":12,"halfHourFee":10,"hourFee":8,"economyFee":4,"minimumFee":2}
    internal static BitcoinFees? ParseMempool(JsonElement root)
    {
        if (Json.Decimal(root, "fastestFee") is not { } fastest || fastest <= 0)
            return null;
        var half = Json.Decimal(root, "halfHourFee") ?? fastest;
        var hour = Json.Decimal(root, "hourFee") ?? half;
        var economy = Json.Decimal(root, "economyFee") ?? hour;
        return new BitcoinFees(fastest, half, hour, economy);
    }

    /// <summary>Reads the batch answer, in whatever order the node sent it.</summary>
    internal static EthereumGas? ParseRpcBatch(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        BigInteger? baseFee = null, tip = null, paid = null;
        foreach (var r in root.EnumerateArray())
        {
            if (!r.TryGetProperty("result", out var result) || !r.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id))
                continue;
            if (id == 1 && result.ValueKind == JsonValueKind.Object && Json.String(result, "baseFeePerGas") is { } b)
                baseFee = ParseHex(b);
            else if (id == 2 && result.ValueKind == JsonValueKind.String)
                tip = ParseHex(result.GetString());
            else if (id == 3 && result.ValueKind == JsonValueKind.Object)
                paid = MedianReward(result);
        }
        if (baseFee is not { } baseWei)
            return null;
        // Prefer what was really paid; some nodes suggest 0 or don't offer the tip call at all.
        var tipWei = paid ?? (tip is { } t && t > 0 ? t : new BigInteger(100_000_000));
        return new EthereumGas(WeiToGwei(baseWei), WeiToGwei(tipWei));
    }

    // {"baseFeePerGas":[...],"reward":[["0x3b9aca00"],["0x5f5e100"],...]}: the 50th percentile tip of each block.
    private static BigInteger? MedianReward(JsonElement history)
    {
        if (!history.TryGetProperty("reward", out var rewards) || rewards.ValueKind != JsonValueKind.Array)
            return null;
        var values = rewards.EnumerateArray()
            .Where(r => r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 0 && r[0].ValueKind == JsonValueKind.String)
            .Select(r => ParseHex(r[0].GetString()))
            .OfType<BigInteger>()
            .Order()
            .ToList();
        return values.Count == 0 ? null : values[values.Count / 2];
    }

    internal static BigInteger? ParseHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || hex.Length > 66)
            return null;
        // A leading 0 keeps the number positive.
        return BigInteger.TryParse("0" + hex[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static decimal WeiToGwei(BigInteger wei) =>
        wei > new BigInteger(decimal.MaxValue) ? decimal.MaxValue : Math.Round((decimal)wei / 1_000_000_000m, 4);

    /// <summary>Cost in BTC of a typical payment at a fee rate.</summary>
    public static decimal BitcoinTxCostBtc(decimal satPerVbyte) => satPerVbyte * TypicalBitcoinTxVbytes / 100_000_000m;
}
