FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Claw.Agent/Claw.Agent.csproj", "src/Claw.Agent/"]
COPY ["src/Claw.Core/Claw.Core.csproj", "src/Claw.Core/"]
COPY ["src/ServiceDefaults/ServiceDefaults.csproj", "src/ServiceDefaults/"]
RUN dotnet restore "src/Claw.Agent/Claw.Agent.csproj"
COPY . .
WORKDIR "/src/src/Claw.Agent"
RUN dotnet build "Claw.Agent.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Claw.Agent.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Claw.Agent.dll"]
