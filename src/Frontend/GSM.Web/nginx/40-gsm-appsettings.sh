#!/bin/sh
# Escribe wwwroot/appsettings.json con la URL de la API (variable API_BASE_URL) antes de que arranque nginx.
# Lo ejecuta el entrypoint de la imagen de nginx (/docker-entrypoint.d).
set -eu

if [ -z "${API_BASE_URL:-}" ]; then
    echo "40-gsm-appsettings: falta la variable API_BASE_URL (p. ej. https://api.midominio.com)." >&2
    exit 1
fi

root=/srv/www
# Sin barra final: el cliente concatena rutas que empiezan por "/".
api_url="${API_BASE_URL%/}"

printf '{\n  "Api": {\n    "ApiBaseUrl": "%s"\n  }\n}\n' "$api_url" > "$root/appsettings.json"

# El publicado trae copias precomprimidas de appsettings.json; nginx (gzip_static) serviría la vieja.
rm -f "$root/appsettings.json.gz" "$root/appsettings.json.br"

echo "40-gsm-appsettings: ApiBaseUrl = $api_url"
