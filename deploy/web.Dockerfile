# AlgoSharp web app (Blazor Server). Build from the repository root:
#   docker build -f deploy/web.Dockerfile -t algosharp-web .
#
# The web app compiles user code with Roslyn but never runs it: execution happens in separate
# algosharp-runner containers that this container starts through the host's Docker engine.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AlgoSharp.slnx ./
COPY src/ src/
# The in-browser runner (served as static files) needs the browser-wasm runtime pack; restore it explicitly.
RUN dotnet restore src/AlgoSharp.Wasm/AlgoSharp.Wasm.csproj
RUN dotnet publish src/AlgoSharp.Web/AlgoSharp.Web.csproj -c Release -o /out /p:UseAppHost=false /p:AlgoSharpReadyToRun=true

# Only the Docker CLI binary (no daemon): it talks to the host engine via the mounted socket.
FROM docker:28-cli AS dockercli

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
WORKDIR /app
COPY --from=build /out ./
# Mount point for the Data Protection key ring; a named volume inherits this ownership.
RUN mkdir -p /keys && chown app:app /keys

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

EXPOSE 8080
# The image's non-root "app" user. Access to the Docker socket is granted by group (see compose group_add).
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "AlgoSharp.Web.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "AlgoSharp.Web.dll"]
