FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY coffeeshop-cli.slnx ./
COPY src/CoffeeshopCli/CoffeeshopCli.csproj src/CoffeeshopCli/
RUN dotnet restore src/CoffeeshopCli/CoffeeshopCli.csproj

COPY . .
RUN dotnet publish src/CoffeeshopCli/CoffeeshopCli.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV Hosting__EnableHttpMcpBridge=false
ENV Hosting__Urls=http://0.0.0.0:8080
ENV Hosting__HealthRoute=/healthz
ENV Hosting__HttpMcpRoute=/mcp

COPY --from=build /app/publish ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "CoffeeshopCli.dll"]
