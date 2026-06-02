FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Claw.Channels/Claw.Channels.csproj", "src/Claw.Channels/"]
COPY ["src/ServiceDefaults/ServiceDefaults.csproj", "src/ServiceDefaults/"]
RUN dotnet restore "src/Claw.Channels/Claw.Channels.csproj"
COPY . .
WORKDIR "/src/src/Claw.Channels"
RUN dotnet build "Claw.Channels.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Claw.Channels.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Claw.Channels.dll"]
