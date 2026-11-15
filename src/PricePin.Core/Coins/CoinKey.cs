using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace PricePin.Core.Coins;

/// <summary>
/// The string that identifies a coin everywhere in the settings (widget coins, alerts, holdings).
/// Coins traded on the big exchanges use their symbol ("BTC"). Tokens found on decentralised
/// exchanges use "dex:&lt;chain&gt;:&lt;contract address&gt;", because many unrelated tokens share
/// a symbol and only the contract address tells them apart.
/// </summary>
public static partial class CoinKey
{
    public const string DexPrefix = "dex:";

    public static bool IsDex([NotNullWhen(true)] string? key) =>
        key is not null && key.StartsWith(DexPrefix, StringComparison.OrdinalIgnoreCase);

    public static string ForDex(string chain, string address) => Normalize($"{DexPrefix}{chain}:{address}");

    /// <summary>Splits "dex:solana:EPjF…" into its chain and address.</summary>
    public static bool TryParseDex(string? key, out string chain, out string address)
    {
        chain = address = "";
        if (!IsDex(key))
            return false;
        var rest = key[DexPrefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1)
            return false;
        chain = rest[..colon];
        address = rest[(colon + 1)..];
        return true;
    }

    /// <summary>
    /// Exchange symbols are upper-cased. DEX keys keep the address as typed, except EVM
    /// addresses (0x + 40 hex digits), which aren't case-sensitive and are lower-cased so
    /// the same token is never added twice. Solana, Sui and TON addresses are case-sensitive.
    /// </summary>
    public static string Normalize(string key)
    {
        key = key.Trim();
        if (!TryParseDex(key, out var chain, out var address))
            return key.ToUpperInvariant();
        address = address.Trim();
        if (EvmAddress().IsMatch(address))
            address = address.ToLowerInvariant();
        return $"{DexPrefix}{chain.Trim().ToLowerInvariant()}:{address}";
    }

    /// <summary>True for text that looks like a token contract address rather than a symbol or name.</summary>
    public static bool LooksLikeAddress(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;
        return EvmAddress().IsMatch(text) || Base58Address().IsMatch(text) || text.Contains("::", StringComparison.Ordinal);
    }

    [GeneratedRegex("^0x[0-9a-fA-F]{40}$")]
    private static partial Regex EvmAddress();

    // Solana and Tron style addresses.
    [GeneratedRegex("^[1-9A-HJ-NP-Za-km-z]{32,48}$")]
    private static partial Regex Base58Address();
}
