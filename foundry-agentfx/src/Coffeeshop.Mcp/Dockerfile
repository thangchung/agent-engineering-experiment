FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Coffeeshop.Mcp/Coffeeshop.Mcp.csproj", "src/Coffeeshop.Mcp/"]
COPY ["src/Coffeeshop.Models/Coffeeshop.Models.csproj", "src/Coffeeshop.Models/"]
COPY ["src/ServiceDefaults/ServiceDefaults.csproj", "src/ServiceDefaults/"]
RUN dotnet restore "src/Coffeeshop.Mcp/Coffeeshop.Mcp.csproj"
COPY . .
WORKDIR "/src/src/Coffeeshop.Mcp"
RUN dotnet build "Coffeeshop.Mcp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Coffeeshop.Mcp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Coffeeshop.Mcp.dll"]
