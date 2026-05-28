FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/ToolSearch.Gateway/ToolSearch.Gateway.csproj", "src/ToolSearch.Gateway/"]
COPY ["src/ServiceDefaults/ServiceDefaults.csproj", "src/ServiceDefaults/"]
RUN dotnet restore "src/ToolSearch.Gateway/ToolSearch.Gateway.csproj"
COPY . .
WORKDIR "/src/src/ToolSearch.Gateway"
RUN dotnet build "ToolSearch.Gateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ToolSearch.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ToolSearch.Gateway.dll"]
