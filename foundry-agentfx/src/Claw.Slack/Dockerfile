FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Claw.Slack/Claw.Slack.csproj", "src/Claw.Slack/"]
COPY ["src/ServiceDefaults/ServiceDefaults.csproj", "src/ServiceDefaults/"]
RUN dotnet restore "src/Claw.Slack/Claw.Slack.csproj"
COPY . .
WORKDIR "/src/src/Claw.Slack"
RUN dotnet build "Claw.Slack.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Claw.Slack.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Claw.Slack.dll"]
