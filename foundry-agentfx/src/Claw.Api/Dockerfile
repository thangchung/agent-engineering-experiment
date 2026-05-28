FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Claw.Api/Claw.Api.csproj", "src/Claw.Api/"]
COPY ["src/Claw.Core/Claw.Core.csproj", "src/Claw.Core/"]
COPY ["src/ServiceDefaults/ServiceDefaults.csproj", "src/ServiceDefaults/"]
RUN dotnet restore "src/Claw.Api/Claw.Api.csproj"
COPY . .
WORKDIR "/src/src/Claw.Api"
RUN dotnet build "Claw.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Claw.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Claw.Api.dll"]
