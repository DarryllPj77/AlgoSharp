# AlgoSharp.Executor: the code-execution service for the public site. Build from the repository root:
#   docker build -f deploy/executor/executor.Dockerfile -t algosharp-executor .
# It starts one disposable algosharp-runner container per run through the host's Docker engine.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AlgoSharp.slnx ./
COPY src/ src/
RUN dotnet publish src/AlgoSharp.Executor/AlgoSharp.Executor.csproj -c Release -o /out --no-self-contained /p:UseAppHost=false

FROM docker:28-cli AS dockercli

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
WORKDIR /app
COPY --from=build /out ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "AlgoSharp.Executor.dll"]
