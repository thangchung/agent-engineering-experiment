# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY dotnetclaw.slnx ./
COPY DotNetClaw/DotNetClaw.csproj DotNetClaw/
COPY DotNetClaw.ServiceDefaults/DotNetClaw.ServiceDefaults.csproj DotNetClaw.ServiceDefaults/
RUN dotnet restore DotNetClaw/DotNetClaw.csproj

COPY DotNetClaw/ DotNetClaw/
COPY DotNetClaw.ServiceDefaults/ DotNetClaw.ServiceDefaults/
RUN dotnet publish DotNetClaw/DotNetClaw.csproj -c Release -o /app/out /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/out .
COPY DotNetClaw/mind ./mind

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DotNetClaw.dll"]
