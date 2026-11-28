using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AlgoSharp.Tracing;

namespace AlgoSharp.Playground.Execution;

public sealed class SandboxOptions
{
    /// <summary>"Docker" (local containers), "Remote" (the separate AlgoSharp.Executor service) or "Disabled".</summary>
    public string Mode { get; set; } = "Docker";
    /// <summary>
    /// Optional OCI runtime for user containers, e.g. "runsc" (gVisor: a user-space kernel between the
    /// code and the host). Docker refuses to start the container if the runtime isn't installed.
    /// </summary>
    public string? Runtime { get; set; }
    public RemoteSandboxOptions Remote { get; set; } = new();
    /// <summary>Shown in place of Run when execution is disabled (e.g. hosts without an isolated sandbox).</summary>
    public string? DisabledMessage { get; set; }
    /// <summary>Status text shown in Browser mode.</summary>
    public string BrowserMessage { get; set; } = "Your code runs in this browser, in an isolated .NET WebAssembly worker. Nothing runs on the server.";
    public string DockerPath { get; set; } = "docker";
    public string Image { get; set; } = "algosharp-runner:1";
    public int TimeoutSeconds { get; set; } = 15;
    public string Memory { get; set; } = "256m";
    public string Cpus { get; set; } = "1";
    public int PidsLimit { get; set; } = 64;
    public int MaxConcurrentRuns { get; set; } = 2;
    public int MaxEvents { get; set; } = 2000;
    public int MaxOutputChars { get; set; } = 8000;
    public int MaxResponseBytes { get; set; } = 8 * 1024 * 1024;
}

public sealed record SandboxStatus(bool Available, string Message)
{
    /// <summary>Result of the last isolation self-test, when the executor runs one.</summary>
    public IsolationReport? Isolation { get; init; }
}

/// <summary>
/// What the isolation self-test observed from inside a sandbox container started with the exact
/// production flags. <see cref="Passed"/> is true only if every check matched the expected value.
/// </summary>
public sealed record IsolationReport(bool Passed, string Runtime, DateTimeOffset CheckedAt, IReadOnlyDictionary<string, string> Checks, string? Failure);

public sealed record SandboxResult(bool Ok, string? Error, RunResponse? Response)
{
    /// <summary>Short failure category for metrics (timeout, crashed, invalid-trace, ...).</summary>
    public string? FailureKind { get; init; }
}

/// <summary>Executes instrumented assemblies somewhere isolated from the web server.</summary>
public interface ICodeExecutor
{
    Task<SandboxStatus> GetStatusAsync(CancellationToken ct = default);
    Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct = default);
}

/// <summary>An executor that applies per-caller quotas; <c>caller</c> is an opaque, non-reversible key.</summary>
public interface ICallerAwareExecutor : ICodeExecutor
{
    Task<SandboxResult> RunAsync(RunRequest request, string caller, CancellationToken ct = default);
}

/// <summary>Used when no isolation is configured. It never runs code.</summary>
public sealed class DisabledExecutor(string reason) : ICodeExecutor
{
    public Task<SandboxStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(new SandboxStatus(false, reason));

    public Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct = default) =>
        Task.FromResult(new SandboxResult(false, reason, null));
}

/// <summary>
/// Runs each request in a fresh, locked-down Docker container: no network, read-only root
/// filesystem, non-root user, no capabilities, memory/CPU/process limits and a hard timeout.
/// If Docker or the runner image is missing, execution is reported as unavailable.
/// </summary>
public sealed class DockerExecutor(SandboxOptions options) : ICodeExecutor
{
    private readonly SemaphoreSlim slots = new(Math.Max(1, options.MaxConcurrentRuns));
    private SandboxStatus? cached;
    private DateTime cachedAt;

    public async Task<SandboxStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (cached is not null && DateTime.UtcNow - cachedAt < TimeSpan.FromSeconds(20)) return cached;
        SandboxStatus status;
        try
        {
            var (code, stdout, stderr, timedOut) = await RunProcessAsync(["image", "inspect", options.Image, "--format", "{{.Id}}"], null, TimeSpan.FromSeconds(8), 64 * 1024, ct);
            status = timedOut
                ? new(false, "Docker didn't respond. Is Docker Desktop running?")
                : code == 0
                    ? new(true, $"Sandbox ready ({options.Image}).")
                    : stderr.Contains("No such image", StringComparison.OrdinalIgnoreCase)
                        ? new(false, $"The sandbox image {options.Image} isn't built yet. Run sandbox/build.ps1.")
                        : new(false, "Docker isn't running. Start Docker Desktop, then build the sandbox image with sandbox/build.ps1.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            status = new(false, "Docker isn't installed or isn't on PATH, so code execution is disabled.");
        }
        cached = status;
        cachedAt = DateTime.UtcNow;
        return status;
    }

    public async Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(ct);
        if (!status.Available) return new(false, status.Message, null);
        if (!await slots.WaitAsync(TimeSpan.FromSeconds(10), ct)) return new(false, "The sandbox is busy. Try again in a moment.", null);
        string name = ContainerPrefix + Guid.NewGuid().ToString("N")[..12];
        try
        {
            string[] args = [.. HardeningArgs(options, name), options.Image];
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request, TraceJson.Options);
            var (code, stdout, stderr, timedOut) = await RunProcessAsync(args, payload, TimeSpan.FromSeconds(options.TimeoutSeconds), options.MaxResponseBytes, ct);
            if (timedOut)
            {
                await KillAsync(name);
                return new(false, $"Execution timed out after {options.TimeoutSeconds} s and was stopped. Look for an infinite loop or very deep recursion.", null);
            }
            if (code == 137) return new(false, "The sandbox stopped the program because it used too much memory (limit " + options.Memory + ").", null);
            if (code != 0 && stdout.Length == 0)
            {
                string detail = stderr.Contains("Stack overflow", StringComparison.OrdinalIgnoreCase)
                    ? "Stack overflow: the recursion went too deep (missing base case?)."
                    : $"The sandbox process failed (exit code {code}).";
                return new(false, detail, null);
            }
            var response = JsonSerializer.Deserialize<RunResponse>(stdout, TraceJson.Options);
            return response is null ? new(false, "The sandbox returned no result.", null) : new(true, null, response);
        }
        catch (JsonException)
        {
            return new(false, "The sandbox returned an unreadable result (output limit reached?).", null);
        }
        finally
        {
            slots.Release();
        }
    }

    public const string ContainerPrefix = "algosharp-run-";

    /// <summary>
    /// The one set of flags every user container gets (runs and self-tests alike): disposable (--rm), no
    /// network, read-only root, non-root user, no capabilities, no privilege escalation, memory/CPU/
    /// process/file limits, and the hardened runtime when configured. No volumes, no env, no socket.
    /// </summary>
    public static List<string> HardeningArgs(SandboxOptions options, string name)
    {
        List<string> args =
        [
            "run", "--rm", "-i", "--name", name,
            "--network", "none",
            "--read-only", "--tmpfs", "/tmp:rw,noexec,nosuid,size=16m",
            "--memory", options.Memory, "--memory-swap", options.Memory,
            "--cpus", options.Cpus, "--pids-limit", options.PidsLimit.ToString(),
            "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--user", "65534:65534",
            "--ulimit", "nofile=64:64",
        ];
        if (!string.IsNullOrWhiteSpace(options.Runtime)) args.AddRange(["--runtime", options.Runtime]);
        return args;
    }

    private const string ProbeScript =
        "echo uid=$(id -u); " +
        "echo cap=$(grep CapEff /proc/self/status | tr -d ' \\t' | cut -d: -f2); " +
        "if touch /app/.probe 2>/dev/null; then echo rootfs=writable; else echo rootfs=readonly; fi; " +
        "if touch /tmp/.probe 2>/dev/null; then echo tmp=writable; else echo tmp=readonly; fi; " +
        "echo ifaces=$(ls /sys/class/net 2>/dev/null | grep -v '^lo$' | tr '\\n' ' '); " +
        "if getent hosts example.com >/dev/null 2>&1; then echo dns=resolves; else echo dns=blocked; fi; " +
        "if [ -e /var/run/docker.sock ] || [ -e /run/docker.sock ]; then echo docker_sock=present; else echo docker_sock=absent; fi; " +
        "echo secret_env=$(env | grep -ciE 'secret|password|connection|apikey|token'); " +
        "echo kernel=$(uname -r)";

    /// <summary>What a correctly isolated container must report.</summary>
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["uid"] = "65534",
        ["cap"] = "0000000000000000",
        ["rootfs"] = "readonly",
        ["tmp"] = "writable",
        ["ifaces"] = "",
        ["dns"] = "blocked",
        ["docker_sock"] = "absent",
        ["secret_env"] = "0",
    };

    /// <summary>
    /// Starts a throwaway container with exactly the production flags and checks, from inside it, that
    /// isolation holds: non-root, no capabilities, read-only root, no network interfaces or DNS, no Docker
    /// socket and no secrets in the environment. Executors refuse to run code when this fails.
    /// </summary>
    public async Task<IsolationReport> SelfTestAsync(CancellationToken ct = default)
    {
        string name = ContainerPrefix + "probe-" + Guid.NewGuid().ToString("N")[..8];
        string runtime = string.IsNullOrWhiteSpace(options.Runtime) ? "runc (default)" : options.Runtime;
        try
        {
            string[] args = [.. HardeningArgs(options, name), "--entrypoint", "/bin/sh", options.Image, "-c", ProbeScript];
            var (code, stdout, stderr, timedOut) = await RunProcessAsync(args, null, TimeSpan.FromSeconds(Math.Max(20, options.TimeoutSeconds)), 64 * 1024, ct);
            if (timedOut)
            {
                await KillAsync(name);
                return new(false, runtime, DateTimeOffset.UtcNow, new Dictionary<string, string>(), "Self-test container timed out.");
            }
            if (code != 0) return new(false, runtime, DateTimeOffset.UtcNow, new Dictionary<string, string>(), $"Self-test container failed to start (exit {code}).");
            var seen = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.Split('=', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1].Trim());
            var failed = Expected.Where(e => !seen.TryGetValue(e.Key, out var v) || v != e.Value).Select(e => e.Key).ToList();
            return new(failed.Count == 0, runtime, DateTimeOffset.UtcNow, seen,
                failed.Count == 0 ? null : "Isolation check failed: " + string.Join(", ", failed));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(false, runtime, DateTimeOffset.UtcNow, new Dictionary<string, string>(), "Docker isn't available.");
        }
    }

    /// <summary>Force-removes sandbox containers left behind (e.g. after a crash) that are older than <paramref name="age"/>.</summary>
    public async Task<int> SweepAsync(TimeSpan age, CancellationToken ct = default)
    {
        try
        {
            var (code, stdout, _, _) = await RunProcessAsync(
                ["ps", "-a", "--filter", "name=" + ContainerPrefix, "--format", "{{.ID}}|{{.CreatedAt}}"], null, TimeSpan.FromSeconds(15), 256 * 1024, ct);
            if (code != 0) return 0;
            var stale = new List<string>();
            foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = line.Split('|', 2);
                // CreatedAt looks like "2026-11-26 14:00:00 +0000 UTC".
                if (parts.Length == 2 && DateTimeOffset.TryParse(parts[1].Replace(" UTC", "").Trim(), out var created) && DateTimeOffset.UtcNow - created > age)
                    stale.Add(parts[0]);
            }
            if (stale.Count > 0) await RunProcessAsync(["rm", "-f", .. stale], null, TimeSpan.FromSeconds(30), 64 * 1024, ct);
            return stale.Count;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return 0;
        }
    }

    private async Task KillAsync(string name)
    {
        try
        {
            await RunProcessAsync(["rm", "-f", name], null, TimeSpan.FromSeconds(15), 4096, CancellationToken.None);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private async Task<(int Code, string Stdout, string Stderr, bool TimedOut)> RunProcessAsync(
        string[] args, byte[]? stdin, TimeSpan timeout, int maxOutput, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(options.DockerPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start docker.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var stdoutTask = ReadCappedAsync(process.StandardOutput.BaseStream, maxOutput);
        var stderrTask = ReadCappedAsync(process.StandardError.BaseStream, 64 * 1024);
        if (stdin is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(stdin, timeoutCts.Token);
        }
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, "", "", true);
        }
        return (process.ExitCode, await stdoutTask, await stderrTask, false);
    }

    private static async Task<string> ReadCappedAsync(Stream stream, int max)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read <= max) buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
