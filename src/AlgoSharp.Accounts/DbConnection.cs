using Npgsql;

namespace AlgoSharp.Accounts;

/// <summary>Turns whatever the host provides into a safe Npgsql connection string.</summary>
public static class DbConnection
{
    /// <summary>
    /// Accepts either a key/value connection string or a URI such as the one Neon shows
    /// (<c>postgresql://user:pass@host/db?sslmode=require</c>) and returns a key/value string.
    /// Remote hosts always get TLS (SSL Mode=Require at minimum); only local hosts may opt out.
    /// </summary>
    public static string Normalize(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        raw = raw.Trim();
        var b = raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            ? FromUri(raw)
            : new NpgsqlConnectionStringBuilder(raw);

        if (!IsLocal(b.Host) && b.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            b.SslMode = SslMode.Require;

        // Neon suspends idle computes; give the first connection after a cold start time to wake it.
        if (b.Timeout < 15) b.Timeout = 15;
        return b.ConnectionString;
    }

    /// <summary>The host and database only, for logs and diagnostics (never the password).</summary>
    public static string Describe(string normalized)
    {
        var b = new NpgsqlConnectionStringBuilder(normalized);
        return $"{b.Host}/{b.Database} (ssl {b.SslMode})";
    }

    private static NpgsqlConnectionStringBuilder FromUri(string raw)
    {
        var uri = new Uri(raw);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            string[] parts = uri.UserInfo.Split(':', 2);
            b.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1) b.Password = Uri.UnescapeDataString(parts[1]);
        }
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            string value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            switch (key)
            {
                case "sslmode":
                    b.SslMode = value.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => SslMode.Require,
                    };
                    break;
                case "options":
                    b.Options = value;
                    break;
                // channel_binding and other libpq-only parameters are ignored; Npgsql negotiates them.
            }
        }
        return b;
    }

    private static bool IsLocal(string? host) =>
        host is null or "localhost" or "127.0.0.1" or "::1" or "postgres" or "db";
}
