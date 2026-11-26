using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AlgoSharp.Tracing;

namespace AlgoSharp.Playground.Execution;

public sealed class SandboxOptions
{
    /// <summary>"Docker" to run in containers, "Disabled" to turn execution off.</summary>
    public string Mode { get; set; } = "Docker";
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

public sealed record SandboxStatus(bool Available, string Message);

public sealed record SandboxResult(bool Ok, string? Error, RunResponse? Response);

/// <summary>Executes instrumented assemblies somewhere isolated from the web server.</summary>
public interface ICodeExecutor
{
    Task<SandboxStatus> GetStatusAsync(CancellationToken ct = default);
    Task<SandboxResult> RunAsync(RunRequest request, CancellationToken ct = default);
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
        string name = "algosharp-run-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            string[] args =
            [
                "run", "--rm", "-i", "--name", name,
                "--network", "none",
                "--read-only", "--tmpfs", "/tmp:rw,noexec,nosuid,size=16m",
                "--memory", options.Memory, "--memory-swap", options.Memory,
                "--cpus", options.Cpus, "--pids-limit", options.PidsLimit.ToString(),
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                "--user", "65534:65534",
                "--ulimit", "nofile=64:64",
                options.Image,
            ];
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

    private async Task KillAsync(string name)
    {
        try
        {
            await RunProcessAsync(["kill", name], null, TimeSpan.FromSeconds(10), 4096, CancellationToken.None);
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
