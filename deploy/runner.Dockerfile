# The code-execution sandbox image (same as sandbox/Dockerfile, but self-contained for Linux hosts:
# it publishes the runner inside Docker, so no local .NET SDK is needed). From the repository root:
#   docker build -f deploy/runner.Dockerfile -t algosharp-runner:1 .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/AlgoSharp.Runner/ src/AlgoSharp.Runner/
COPY src/AlgoSharp.Tracing/ src/AlgoSharp.Tracing/
RUN dotnet publish src/AlgoSharp.Runner/AlgoSharp.Runner.csproj -c Release -o /runner --no-self-contained /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0
COPY --from=build /runner/ /app/
WORKDIR /tmp
USER 65534:65534
ENTRYPOINT ["dotnet", "/app/AlgoSharp.Runner.dll"]
