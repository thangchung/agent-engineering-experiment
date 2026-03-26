FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore McpExperiments.slnx
RUN dotnet publish src/McpServer/McpServer.csproj -c Release -o /app/out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
# Install Python 3 for LocalConstrainedRunner, which spawns `python3 -` as a subprocess.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "McpServer.dll"]
