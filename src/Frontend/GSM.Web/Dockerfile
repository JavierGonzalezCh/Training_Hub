# Web de TrainingHub (GSM.Web, Blazor WASM): panel, /admin, /kiosk y la app del miembro en /app. Se sirve con nginx.
# Contexto de construcción: src/Frontend
#   docker build -f src/Frontend/GSM.Web/Dockerfile -t gsm-web src/Frontend      (desde la raíz del repositorio)
#
# La URL de la API NO va compilada: el contenedor escribe wwwroot/appsettings.json al arrancar a partir de la variable
# API_BASE_URL, así la misma imagen sirve para pruebas y producción.

# ---- Compilación ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY GSM.Client/GSM.Client.csproj GSM.Client/
COPY GSM.UI.Shared/GSM.UI.Shared.csproj GSM.UI.Shared/
COPY GSM.Member.UI/GSM.Member.UI.csproj GSM.Member.UI/
COPY GSM.Panel.UI/GSM.Panel.UI.csproj GSM.Panel.UI/
COPY GSM.Web/GSM.Web.csproj GSM.Web/
RUN dotnet restore GSM.Web/GSM.Web.csproj

COPY GSM.Client/ GSM.Client/
COPY GSM.UI.Shared/ GSM.UI.Shared/
COPY GSM.Member.UI/ GSM.Member.UI/
COPY GSM.Panel.UI/ GSM.Panel.UI/
COPY GSM.Web/ GSM.Web/
RUN dotnet publish GSM.Web/GSM.Web.csproj -c Release --no-restore -o /app/publish

# ---- Ejecución --------------------------------------------------------------------------------
# nginx sin privilegios (puerto 8080). Las plantillas de /etc/nginx/templates se procesan con envsubst al arrancar.
FROM nginxinc/nginx-unprivileged:1.27-alpine AS web
COPY --from=build --chown=101:101 /app/publish/wwwroot /srv/www
COPY GSM.Web/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=755 GSM.Web/nginx/40-gsm-appsettings.sh /docker-entrypoint.d/40-gsm-appsettings.sh

ENV API_BASE_URL=""
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1
