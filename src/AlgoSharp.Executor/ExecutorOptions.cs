namespace AlgoSharp.Executor;

/// <summary>Service-level policy. Sandbox limits (memory, CPU, timeout, runtime) live in the "Sandbox" section.</summary>
public sealed class ExecutorOptions
{
    /// <summary>Shared secret (base64, at least 32 bytes). Required: the service refuses to start without it.</summary>
    public string? Key { get; set; }

    /// <summary>Largest accepted request body. Compiled solutions are a few KB; 512 KB is generous.</summary>
    public int MaxRequestBytes { get; set; } = 512 * 1024;

    public int MaxCases { get; set; } = 10;

    /// <summary>Runs one account may start per 10 minutes, and per day.</summary>
    public int PerCallerPer10Minutes { get; set; } = 30;
    public int PerCallerPerDay { get; set; } = 300;

    /// <summary>Runs one account may have in flight at once.</summary>
    public int PerCallerConcurrent { get; set; } = 1;

    /// <summary>Ceiling for the whole service, whatever the number of accounts.</summary>
    public int GlobalPerMinute { get; set; } = 120;

    public int SelfTestMinutes { get; set; } = 10;

    /// <summary>Source networks allowed to call /v1 (CIDR, e.g. the web host's outbound IPs). Empty = any.</summary>
    public string[] AllowedNetworks { get; set; } = [];

    /// <summary>
    /// Proxy networks (CIDR) whose X-Forwarded-For is trusted, e.g. the Docker network Caddy runs on.
    /// Empty = the connection's own address is used (no proxy).
    /// </summary>
    public string[] TrustedProxyNetworks { get; set; } = [];

    public AlgoSharp.Playground.Execution.QuotaLimits Limits => new(PerCallerPer10Minutes, PerCallerPerDay, PerCallerConcurrent, GlobalPerMinute);
}
