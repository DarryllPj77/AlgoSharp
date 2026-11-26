# Builds the Docker image that runs user code for the AlgoSharp C# playground.
# Requires Docker Desktop (Linux containers) to be running.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot 'runner'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src/AlgoSharp.Runner') -c Release -o $out --no-self-contained
docker build -t algosharp-runner:1 $PSScriptRoot
Write-Host 'Sandbox image algosharp-runner:1 is ready.'
