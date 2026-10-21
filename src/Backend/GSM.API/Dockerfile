# API de TrainingHub (GSM). Contexto de construcción: src/Backend
#   docker build -f GSM.API/Dockerfile -t gsm-api src/Backend      (desde la raíz del repositorio: -f src/Backend/GSM.API/Dockerfile)
#
# La imagen es solo la API. Las migraciones no van en ella: se aplican aparte contra la base (ver README).

# ---- Compilación ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero los .csproj: la restauración de paquetes queda en caché mientras no cambien las dependencias.
COPY Directory.Build.props ./
COPY GSM.Domain/GSM.Domain.csproj GSM.Domain/
COPY GSM.Application/GSM.Application.csproj GSM.Application/
COPY GSM.Infrastructure/GSM.Infrastructure.csproj GSM.Infrastructure/
COPY GSM.API/GSM.API.csproj GSM.API/
RUN dotnet restore GSM.API/GSM.API.csproj \
    && dotnet restore GSM.Infrastructure/GSM.Infrastructure.csproj

COPY . .
RUN dotnet publish GSM.API/GSM.API.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- Ejecución --------------------------------------------------------------------------------
# Variante Debian (no "chiseled"): la app necesita tzdata (Clock:TimeZone = America/Bogota) e ICU (formato es-CO).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

# Llaves de ASP.NET Data Protection (protegen los códigos y enlaces de restablecimiento). Van en un volumen para que
# sobrevivan a los redespliegues; el directorio existe y es del usuario `app` para que el volumen herede el dueño.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
VOLUME /home/app/.aspnet/DataProtection-Keys

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "GSM.API.dll"]
