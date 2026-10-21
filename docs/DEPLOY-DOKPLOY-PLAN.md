# Plan de despliegue — Dokploy en servidor propio (etapa 8, punto 4)

> **Estado: planificado, sin empezar.** Decidido con el usuario el 2026-10-05: **se despliega en un servidor propio con Dokploy, no en Railway.** Sustituye a la idea original de Railway (etapa 8, punto 4 del backend).
>
> Documento de trabajo para el modelo que lo ejecute. Complementa `CLAUDE.md` y `README.md`. **Ritmo:** una fase por vez y con confirmación del usuario entre fases. Las fases D0 y D5 son trabajo **del usuario en el servidor**; el modelo prepara los comandos y la lista, pero no tiene acceso al servidor y no debe pedir credenciales.

---

## Por qué Dokploy y qué implica

- **A favor:** coste fijo, control total y datos donde el usuario decida. Dokploy cubre lo cotidiano: despliegue desde git, TLS automático con Traefik y Let's Encrypt, variables de entorno, bases Postgres con copias a S3 y avisos de fallos.
- **El coste es operativo:** el servidor es del usuario. El riesgo principal no es montarlo, sino que algo falle en silencio: copias que no se hacen, disco lleno, un parche sin aplicar.
- **Este plan convierte cada riesgo en un paso concreto:** endurecimiento del servidor, copias a R2 con prueba de restauración, alertas y un runbook mensual.

**Requisito de negocio que sube la exigencia:** con el check-in por QR (etapa 12), si la API se cae, ningún miembro puede registrar su entrada. Por eso hay alertas externas y un runbook de caída desde el primer día.

---

## Arquitectura objetivo

```
                 Internet (443)
                      │
              Traefik (Dokploy) ── TLS Let's Encrypt
              │                         │
   api.<dominio>                  app.<dominio>
   GSM.API (contenedor)           GSM.Web (nginx, estáticos WASM)
              │
   Postgres 18 (Dokploy, red interna, sin puerto público)
              │
   Copias diarias ──► Cloudflare R2 (bucket privado aparte)
```

- **Dos aplicaciones en Dokploy, cada una con su subdominio:** `api.<dominio>` para la API y `app.<dominio>` para la web, que incluye el panel, `/admin` y la app del miembro en `/app`.
  - Se separan porque la web son archivos estáticos que nginx sirve sin coste, y la API escala y se reinicia aparte.
  - El precio es configurar CORS, que ya está soportado (`Cors:AllowedOrigins`).
- **Postgres como base de datos de Dokploy**, solo en la red interna de Docker.
  - Alternativa válida si el usuario lo decide más adelante: un Postgres **gestionado** externo. Solo cambia la cadena de conexión; el resto del plan no se toca.
- **Dominio:** sin decidir. Hace falta antes de la fase D3. Lo usan:
  - los certificados TLS;
  - CORS;
  - los enlaces de los emails (Resend);
  - el QR del kiosco (`app.<dominio>/app/checkin?t=…`);
  - los App Links de Android (etapa 12.6).
- **App móvil (MAUI):** fuera de este plan. Sí necesita que `ApiBaseUrl` apunte a `https://api.<dominio>` en las compilaciones de release.

---

## D0 — Servidor (lo hace el usuario; el modelo prepara la guía)

**Objetivo:** un servidor endurecido con Dokploy instalado.

1. **VPS** con Ubuntu LTS, de al menos **2 vCPU, 4 GB de RAM y 40 GB de disco** (Postgres, las dos imágenes y las copias locales temporales). Región cercana a Colombia, por latencia.
2. **Endurecimiento:**
   - usuario no root con sudo;
   - SSH **solo con llave** (`PasswordAuthentication no` y `PermitRootLogin no`);
   - `ufw` permitiendo solo 22, 80 y 443;
   - `fail2ban` para SSH;
   - `unattended-upgrades` activado para las actualizaciones de seguridad.
3. **Instalar Dokploy** con el script oficial. **Proteger su panel:** contraseña fuerte y 2FA si la versión lo permite. Si se puede, acceso solo por VPN o túnel, o al menos solo por su propio dominio con TLS, nunca por la IP pública en el puerto 3000.
4. **DNS:** registros `A` de `api.<dominio>` y `app.<dominio>` (y el del panel de Dokploy, si va por dominio) hacia la IP del servidor.

**Entregable del modelo:** una sección "Servidor" en `docs/DEPLOY-RUNBOOK.md` con estos comandos paso a paso y cómo comprobar cada uno.

---

## D1 — Preparar la API para producción (código)

**Objetivo:** la API corre en un contenedor detrás de un proxy, con la configuración solo por variables de entorno.

1. **Cabeceras reenviadas.** Detrás de Traefik, la API recibe HTTP y la IP del proxy.
   - Añade `UseForwardedHeaders` (`X-Forwarded-For` y `X-Forwarded-Proto`) **al principio** del pipeline, antes de `UseHttpsRedirection`, del limitador de peticiones y de la autenticación.
   - Confía en la red de Docker: `KnownNetworks`/`KnownProxies` configurables por variable, o limpia las listas por defecto **solo** si el contenedor no está expuesto directamente. Documenta la elección.
   - Sin esto, el limitador de peticiones (`AddGsmRateLimiting`) vería a todos los usuarios con la misma IP y bloquearía a todos a la vez, y la redirección a HTTPS podría entrar en bucle.
2. **Dockerfile multi-stage** en `src/Backend/GSM.API/Dockerfile` (contexto: `src/Backend`):
   - compilación con `mcr.microsoft.com/dotnet/sdk:10.0`, `dotnet publish -c Release`;
   - ejecución con `mcr.microsoft.com/dotnet/aspnet:10.0` **en la variante Debian, no la *chiseled*.** La app necesita **tzdata** (`Clock:TimeZone = America/Bogota`) e **ICU** (formato `es-CO`). Comprueba que `InvariantGlobalization` no está activado. Si la imagen no trae tzdata, instálalo;
   - usuario no root, `ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`;
   - `HEALTHCHECK` contra `/health`, que ya existe (`HealthController`);
   - un `.dockerignore` que excluya `bin/`, `obj/` y `TestResults/`.
3. **Producción sin datos de desarrollo:** confirma que `DevSeeder`, OpenAPI y Scalar solo corren en `Development`; hoy es así, solo hay que comprobarlo. `ASPNETCORE_ENVIRONMENT=Production` en Dokploy.
4. **Migraciones con un *bundle* de EF.** La API no migra al arrancar, y se mantiene así: dos réplicas arrancando a la vez podrían migrar a la vez.
   - Un segundo target del Dockerfile (o un script) genera `efbundle` con `dotnet ef migrations bundle --project GSM.Infrastructure --self-contained -r linux-x64`.
   - Se ejecuta como paso previo de cada despliegue con `GSM_MIGRATIONS_CONNECTION` (la variable que ya soporta `GsmDbContextFactory`).
   - Documenta en el runbook cómo lanzarlo desde Dokploy (comando previo al despliegue o tarea manual, según lo que ofrezca la versión instalada).
5. **Logs:** Serilog ya escribe en consola, y Dokploy recoge la salida estándar. Mantén el nivel `Information`, y comprueba que no se loguean tokens ni contraseñas, ni los códigos QR del gym.

**Aceptación:** `docker build` y `docker run` en local con variables de entorno de prueba levantan la API; `/health` y `/health/db` responden contra un Postgres local; una petición con `X-Forwarded-Proto: https` no redirige; el limitador ve la IP de `X-Forwarded-For`.

---

## D2 — Preparar la web para producción (código)

**Objetivo:** la web WASM se sirve con nginx y apunta a la API de producción sin recompilar por cada entorno.

1. **Configuración por entorno:** `wwwroot/appsettings.json` tiene hoy `Api:ApiBaseUrl = https://localhost:7219` fijo. Opciones (elige la primera salvo que no funcione):
   - **a)** `wwwroot/appsettings.Production.json` con `https://api.<dominio>`, y nginx enviando la cabecera `Blazor-Environment: Production`;
   - **b)** que el contenedor escriba `appsettings.json` al arrancar a partir de una variable de entorno (script de entrada de nginx).
2. **Dockerfile** en `src/Frontend/GSM.Web/Dockerfile`: compilación con el SDK 10 (`dotnet publish -c Release`) y ejecución con `nginx:alpine` sirviendo `wwwroot` publicado. `nginx.conf` con:
   - `try_files $uri $uri/ /index.html` para las rutas de la SPA (`/members/...`, `/app/...`);
   - tipos MIME de `.wasm`, `.dat`, `.blat` y `.json`, y compresión (gzip/brotli si están disponibles);
   - caché larga para `_framework/*` (llevan huella de versión) y **sin caché** para `index.html`, `service-worker.js` y `appsettings*.json`;
   - cabeceras de seguridad básicas (`X-Content-Type-Options`, `Referrer-Policy` y una `Content-Security-Policy` compatible con Blazor WASM y Google Fonts; pruébala en el navegador antes de cerrar).
3. **PWA:** `service-worker.js` no cachea (decisión de F7); se mantiene así.

**Aceptación:** la imagen sirve el login en local; refrescar en `/members` o en `/app/rutina` no da 404; la web llama a la URL de API configurada; no hay errores de CSP en la consola.

---

## D3 — Montaje en Dokploy

**Objetivo:** las dos aplicaciones y la base, funcionando con TLS. Lo hace el usuario siguiendo el runbook que escribe el modelo.

1. **Base de datos:** Postgres 18 en Dokploy, con usuario y base `gsm`, contraseña generada y **sin puerto expuesto**.
2. **Aplicación `gsm-api`** desde el repositorio (Dockerfile de D1), dominio `api.<dominio>` con HTTPS. Variables de entorno (los nombres usan `__` en lugar de `:`):

   | Variable | Valor |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `ConnectionStrings__Postgres` | cadena interna de la base de Dokploy |
   | `Jwt__SigningKey` | aleatoria, ≥ 32 caracteres |
   | `Checkin__SigningKey` | aleatoria, ≥ 32, **distinta** de la de JWT |
   | `Cors__AllowedOrigins__0` | `https://app.<dominio>` |
   | `Storage__R2__*` | credenciales de R2 (bucket de archivos, no el de copias) |
   | `Email__Resend__ApiKey`, `Email__Resend__FromAddress` | Resend, con el dominio verificado (SPF/DKIM) |
   | `Communications__UnsubscribeSigningKey` | aleatoria, ≥ 32, distinta de las demás |
   | `Communications__PublicApiBaseUrl` | `https://api.<dominio>` (base del enlace de baja de los correos) |
   | `Jobs__Secret` | aleatoria, ≥ 32; la usa la tarea programada de Comunicaciones |
   | `Identity__PublicWebBaseUrl` | `https://app.<dominio>` (enlaces de activación e invitación en los correos) |
   | `Notifications__Fcm__*` | proyecto de Firebase del ambiente (push web y Android); ver `docs/GSM-modulo-notifications.md` |

   **Tarea programada (Comunicaciones, avisos de suscripción, anonimización y alertas):** en Dokploy, una tarea cada 5–10 min que ejecute `curl -fsS -X POST https://api.<dominio>/internal/jobs/communications/run -H "X-Job-Secret: $JOBS_SECRET"`. Si falla, que avise por el mismo canal que las copias.

   **Ninguna clave va en `appsettings.json` ni en el repositorio.** Las claves se generan con `openssl rand -base64 48`.
3. **Aplicación `gsm-web`** desde el repositorio (Dockerfile de D2), dominio `app.<dominio>` con HTTPS.
4. **Primera migración:** ejecutar el bundle de D1 contra la base. Después, **crear el superadmin**: hoy `IdentityDataSeeder` solo corre en Development. El modelo debe proponer un mecanismo seguro para producción, como un comando único (`--seed-superadmin` con correo y clave por variable de entorno) que no exista en tiempo de ejecución normal. **No reutilizar `DevSeeder`**, que crea el gym demo.
5. **Despliegue automático:** webhook de git en Dokploy al hacer push a `main`. La rama de trabajo actual es `master`: el usuario decide cuál despliega.

**Aceptación:** `https://api.<dominio>/health/db` responde; `https://app.<dominio>` carga el login con certificado válido; el superadmin inicia sesión y da de alta un gym; un owner entra al panel; las subidas a R2 y los emails de Resend funcionan.

---

## D4 — Copias, alertas y monitorización

**Objetivo:** que nada falle en silencio.

1. **Copias de Postgres:**
   - copia programada **diaria** en Dokploy hacia un destino S3 → **un bucket de R2 privado y exclusivo para copias**, con credenciales de solo ese bucket;
   - retención: 14 diarias como mínimo;
   - si Dokploy permite una segunda programación, añade una copia semanal con retención de 8 semanas.
2. **Prueba de restauración (obligatoria antes de dar por cerrada la fase):** descargar la última copia, restaurarla en una base vacía (local o de Dokploy) y comprobar que la API arranca contra ella y que los datos están. Documenta el procedimiento exacto en el runbook: es lo que se hará el día de una emergencia.
3. **Alertas:**
   - monitor externo (UptimeRobot, Better Stack o similar) sobre `https://api.<dominio>/health/db` cada 1–5 min, con aviso por email o Telegram al usuario;
   - otro monitor sobre `https://app.<dominio>`;
   - avisos de Dokploy (fallo de despliegue o de copia) al mismo canal;
   - **aviso de disco al 80 %:** script con cron en el servidor o el monitor del proveedor del VPS. Un disco lleno es la causa más frecuente de caída.
4. **Limpieza de Docker:** `docker system prune` programado (imágenes y capas antiguas) o la opción equivalente de Dokploy, para que las imágenes no llenen el disco.

**Aceptación:** existe una copia en R2 y se ha restaurado con éxito al menos una vez; detener la API dispara la alerta en minutos; un despliegue fallido avisa.

---

## D5 — Runbook de operación (`docs/DEPLOY-RUNBOOK.md`)

El modelo escribe el runbook; el usuario lo sigue. Debe contener, con comandos exactos:

- **Desplegar** (automático y manual) y **migrar**, siempre en este orden: copia → migración → despliegue.
- **Volver a la versión anterior:** redesplegar la imagen previa en Dokploy. Si la migración no era compatible hacia atrás, restaurar la copia previa. Por eso la copia va antes de migrar.
- **Restaurar una copia:** el procedimiento probado en D4.
- **La API está caída:** dónde mirar los logs, cómo reiniciarla y qué comprobar (disco, memoria, base). Mientras tanto, recepción puede registrar a mano cuando vuelva, pero el check-in por QR no funciona.
- **Rotar claves:**
  - `Jwt__SigningKey` cierra todas las sesiones;
  - `Checkin__SigningKey` invalida todos los QR, también los impresos: hay que reimprimirlos.

  Hay que avisar a los gyms antes.
- **Checklist mensual** (≈ 1 h):
  - actualizaciones pendientes y reinicio si lo piden;
  - espacio en disco;
  - que la última copia existe y tiene un tamaño razonable;
  - una restauración de prueba cada trimestre;
  - actualizar Dokploy (leyendo las notas de versión);
  - revisar los accesos SSH.
- **Cambio de versión mayor de Postgres:** volcado y restauración en una base nueva, con ventana de mantenimiento anunciada.

---

## Al cerrar

- Actualizar `CLAUDE.md`:
  - "falta desplegar" → desplegado;
  - Railway → Dokploy;
  - las URLs de producción, sin claves.
- `README.md`: quitar "Despliegue" de "Pendiente de configurar" y enlazar el runbook.
- Este documento: estado "completado", con las desviaciones que haya habido.
