using System.Text.Json;
using AlgoSharp.Runner;
using AlgoSharp.Tracing;

// Sandbox entry point: one JSON RunRequest on stdin, one JSON RunResponse on stdout.
// Intended to run inside the locked-down container defined in sandbox/Dockerfile.
const int MaxInput = 8 * 1024 * 1024;

var stdout = Console.OpenStandardOutput();
RunResponse response;
try
{
    using var stdin = Console.OpenStandardInput();
    using var buffer = new MemoryStream();
    var chunk = new byte[81920];
    int read;
    while ((read = stdin.Read(chunk, 0, chunk.Length)) > 0)
    {
        buffer.Write(chunk, 0, read);
        if (buffer.Length > MaxInput) throw new InvalidOperationException("Request too large.");
    }
    var request = JsonSerializer.Deserialize<RunRequest>(buffer.ToArray(), TraceJson.Options)
                  ?? throw new InvalidOperationException("Empty request.");
    response = RunnerEngine.Execute(request);
}
catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
{
    response = new RunResponse(false, ex.Message, []);
}

JsonSerializer.Serialize(stdout, response, TraceJson.Options);
stdout.Flush();
