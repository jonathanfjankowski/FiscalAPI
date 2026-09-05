# syntax=docker/dockerfile:1

# ---------- Estágio 0: build do painel admin (SPA React) ----------
FROM node:22-alpine AS frontend
WORKDIR /web

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ .
RUN npm run build

# ---------- Estágio 1: build .NET ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copia só os .csproj primeiro — aproveita cache de camada do Docker no restore
COPY src/Fiscal.Api/Fiscal.Api.csproj src/Fiscal.Api/
COPY src/Fiscal.Core/Fiscal.Core.csproj src/Fiscal.Core/
COPY src/Fiscal.Persistence/Fiscal.Persistence.csproj src/Fiscal.Persistence/
COPY src/Fiscal.Adapters.Unimake/Fiscal.Adapters.Unimake.csproj src/Fiscal.Adapters.Unimake/
COPY src/Fiscal.Worker/Fiscal.Worker.csproj src/Fiscal.Worker/

RUN dotnet restore src/Fiscal.Api/Fiscal.Api.csproj \
 && dotnet restore src/Fiscal.Worker/Fiscal.Worker.csproj

# Agora copia o restante do código
COPY src/ src/

RUN dotnet publish src/Fiscal.Api/Fiscal.Api.csproj \
      -c Release -o /app/publish/api --no-restore /p:UseAppHost=false

RUN dotnet publish src/Fiscal.Worker/Fiscal.Worker.csproj \
      -c Release -o /app/publish/worker --no-restore /p:UseAppHost=false

# ---------- Estágio 2: runtime da API ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS api-final
WORKDIR /app

RUN addgroup -S fiscal && adduser -S fiscal -G fiscal
USER fiscal

COPY --from=build /app/publish/api .

# SPA do painel admin — servida pela própria API (wwwroot + fallback).
COPY --from=frontend /web/dist ./wwwroot

EXPOSE 8080
ENTRYPOINT ["dotnet", "Fiscal.Api.dll"]

# ---------- Estágio 3: runtime do Worker ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS worker-final
WORKDIR /app

RUN addgroup -S fiscal && adduser -S fiscal -G fiscal
USER fiscal

COPY --from=build /app/publish/worker .

ENTRYPOINT ["dotnet", "Fiscal.Worker.dll"]
