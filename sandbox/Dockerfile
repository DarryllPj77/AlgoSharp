# AlgoSharp sandbox runner image.
# Build with sandbox/build.ps1 (it publishes the runner first, then builds this image).
FROM mcr.microsoft.com/dotnet/runtime:10.0

# No diagnostics IPC, no telemetry, invariant globalization: fewer moving parts, works on a read-only FS.
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0

COPY runner/ /app/
WORKDIR /tmp

# Runs as an unprivileged user; the host additionally passes --user 65534:65534.
USER 65534:65534
ENTRYPOINT ["dotnet", "/app/AlgoSharp.Runner.dll"]
