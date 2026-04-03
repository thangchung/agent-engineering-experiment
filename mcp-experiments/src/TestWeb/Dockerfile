FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore McpExperiments.slnx
RUN dotnet publish src/TestWeb/TestWeb.csproj -c Release -o /app/out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_HTTP_PORTS=8082
EXPOSE 8082
ENTRYPOINT ["dotnet", "TestWeb.dll"]
