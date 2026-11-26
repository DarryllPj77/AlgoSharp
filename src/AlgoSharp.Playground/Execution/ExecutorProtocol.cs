using System.Security.Cryptography;
using System.Text;

namespace AlgoSharp.Playground.Execution;

/// <summary>Connection settings for Sandbox:Mode = "Remote" (the web app's side).</summary>
public sealed class RemoteSandboxOptions
{
    /// <summary>Base URL of the AlgoSharp.Executor service. Must be https:// unless it's on localhost.</summary>
    public string? Url { get; set; }

    /// <summary>Shared secret (base64, at least 32 random bytes) used to sign requests and responses.</summary>
    public string? Key { get; set; }

    /// <summary>
    /// Runtime the executor must report from its isolation self-test (e.g. "runsc"). When set, a service
    /// using any other runtime is treated as unavailable.
    /// </summary>
    public string? RequireRuntime { get; set; }

    public int TimeoutSeconds { get; set; } = 45;
}

/// <summary>
/// Request/response signing between the web app and the executor service (HMAC-SHA256 over a shared
/// secret, on top of TLS). Requests carry a timestamp and a single-use nonce, so a captured request
/// can't be replayed; responses are signed over the request's nonce, so the web app only trusts answers
/// from the real service. The caller id is a keyed hash of the account id: the executor can apply
/// per-account quotas without learning who anyone is.
/// </summary>
public static class ExecutorProtocol
{
    public const string TimestampHeader = "X-AlgoSharp-Timestamp";
    public const string NonceHeader = "X-AlgoSharp-Nonce";
    public const string CallerHeader = "X-AlgoSharp-Caller";
    public const string SignatureHeader = "X-AlgoSharp-Signature";
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(60);
    public const int MinKeyBytes = 32;

    public static byte[] ParseKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) throw new InvalidOperationException("The executor key is not configured.");
        byte[] key;
        try { key = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("The executor key must be base64."); }
        if (key.Length < MinKeyBytes) throw new InvalidOperationException($"The executor key must be at least {MinKeyBytes} random bytes.");
        return key;
    }

    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Opaque per-account id: stable for one account, meaningless without the key.</summary>
    public static string CallerKey(byte[] key, string accountId) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("caller\n" + accountId)))[..32].ToLowerInvariant();

    public static string SignRequest(byte[] key, string method, string path, string timestamp, string nonce, string caller, ReadOnlySpan<byte> body) =>
        Sign(key, $"request\n{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{nonce}\n{caller}\n{Sha256Hex(body)}");

    public static string SignResponse(byte[] key, string nonce, int status, ReadOnlySpan<byte> body) =>
        Sign(key, $"response\n{nonce}\n{status}\n{Sha256Hex(body)}");

    /// <summary>Constant-time comparison of a received signature with the expected one.</summary>
    public static bool Matches(string expected, string? received)
    {
        if (string.IsNullOrEmpty(received)) return false;
        byte[] a = Encoding.ASCII.GetBytes(expected), b = Encoding.ASCII.GetBytes(received);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static bool TimestampFresh(string? timestamp, DateTimeOffset now) =>
        long.TryParse(timestamp, out long unix)
        && (now - DateTimeOffset.FromUnixTimeSeconds(unix)).Duration() <= MaxClockSkew;

    private static string Sign(byte[] key, string payload) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));

    private static string Sha256Hex(ReadOnlySpan<byte> body) => Convert.ToHexString(SHA256.HashData(body));
}
