using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using AlgoSharp.Runner;
using AlgoSharp.Tracing;

// Entry point required by the WebAssembly SDK; the worker drives everything through BrowserRunner.Run.
return;

namespace AlgoSharp.Wasm
{
    /// <summary>
    /// The browser twin of the container runner's stdin/stdout loop: one RunRequest JSON in, one RunResponse JSON out.
    /// The trace is recorded by the instrumented assembly itself (Tr), exactly as in the container.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("browser")]
    public static partial class BrowserRunner
    {
        private const int MaxInput = 2 * 1024 * 1024;

        [JSExport]
        public static string Run(string requestJson)
        {
            RunResponse response;
            try
            {
                if (requestJson.Length > MaxInput) throw new InvalidOperationException("Request too large.");
                var request = JsonSerializer.Deserialize<RunRequest>(requestJson, TraceJson.Options)
                              ?? throw new InvalidOperationException("Empty request.");
                response = RunnerEngine.Execute(request);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
            {
                response = new RunResponse(false, ex.Message, []);
            }
            return JsonSerializer.Serialize(response, TraceJson.Options);
        }
    }
}
