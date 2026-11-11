# GSM — TrainingHub

Sistema de gestión de gimnasios B2B SaaS. El nombre comercial es **TrainingHub**; `GSM` (Gym System
Management) es el codename que se usa en el código, los proyectos y la base de datos.

## Qué es

Una plataforma multi-gimnasio (multi-tenant): cada negocio —gym, box, estudio, entrenador personal o
academia— tiene sus datos aislados, y una cuenta SuperAdmin administra la cartera completa.

| Producto | Para quién | Dónde |
|---|---|---|
| **TrainingHub OS** | El equipo del gym (owner y staff) | Panel web (`/`) y app nativa en modo staff |
| **TrainingHub GO** | El miembro | PWA en `/app` y app nativa (Android) |
| **Admin** | El SuperAdmin de la plataforma | `/admin`: gyms, suscripciones, impersonación, métricas |
| **Kiosco** | La recepción del gym | `/kiosk`: muestra el QR de entrada |

Módulos: Identity (cuentas, accesos por gym, permisos), Gyms, Members, Memberships (planes, gracia,
congelar/cancelar), Payments (pagos, anulaciones, caja por turno), Attendance (check-in con QR del gym,
aforo), Achievements (logros, rachas, ranking), Routines (rutinas, plantillas, progreso, cumplimiento),
Classes (horarios y reservas), Comunicaciones (novedades y recordatorios), Equipment (inventario de
máquinas), Notifications (bandeja, correos, push), SuperAdmin y Logs.

**Stack:** .NET 10, ASP.NET Core, EF Core 10, PostgreSQL 18, JWT + ASP.NET Identity. Frontend en Blazor
WASM + MudBlazor y MAUI Blazor Hybrid. Monolito modular: un schema de Postgres por módulo.

```
src/Backend/    GSM.Domain · GSM.Application · GSM.Infrastructure · GSM.API   (GSM.sln)
src/Frontend/   GSM.Client · GSM.UI.Shared · GSM.Panel.UI · GSM.Member.UI · GSM.Web   (GSM.Frontend.slnx)
                GSM.Mobile (MAUI, fuera del .slnx)
tests/          GSM.IntegrationTests (contra Postgres) · GSM.Web.Tests (bUnit)
scripts/        cliente NSwag y datos de demostración
```

El resto de este archivo explica **cómo ponerlo a funcionar**.

---

## Requisitos

- **.NET SDK 10**
- **PostgreSQL 18** — instalación nativa. No hay contenedor de base de datos: el proyecto asume un
  servidor Postgres escuchando en `localhost:5432`.
- **`dotnet-ef`** para las migraciones: `dotnet tool install --global dotnet-ef`

## Base de datos

Dos bases en el mismo servidor, ambas propiedad del rol `gsm`:

| Base | Uso | Cómo se migra |
|---|---|---|
| `gsm` | desarrollo | a mano, con `dotnet ef database update` |
| `gsm_test` | tests de integración | sola, `ApiFactory` la migra en cada ejecución |

Credenciales de desarrollo (usuario, clave y nombre de base coinciden a propósito):

```
Host=localhost;Port=5432;Database=gsm;Username=gsm;Password=gsm
```

Vive en `src/Backend/GSM.API/appsettings.Development.json`. En `appsettings.json` la cadena está
**vacía a propósito**: fuera de desarrollo se inyecta por variable de entorno
(`ConnectionStrings__Postgres`), igual que `Jwt__SigningKey`.

### Crear las bases desde cero

Con `psql` como superusuario (`postgres`):

```sql
CREATE ROLE gsm LOGIN PASSWORD 'gsm';
CREATE DATABASE gsm OWNER gsm;
CREATE DATABASE gsm_test OWNER gsm;
```

### Aplicar las migraciones

**La API no migra al arrancar.** Después de cada `git pull` que traiga migraciones nuevas:

```bash
cd src/Backend
dotnet ef database update --project GSM.Infrastructure
```

`GSM.Infrastructure` es a la vez proyecto de migraciones y de arranque para `dotnet ef`: ahí están el
paquete `Microsoft.EntityFrameworkCore.Design` y la fábrica de diseño `GsmDbContextFactory`. **No uses
`--startup-project GSM.API`**: la API no referencia ese paquete y el comando falla.

Por defecto migra la base `gsm`. Para apuntar a otra —la de producción, por ejemplo— define
`GSM_MIGRATIONS_CONNECTION` con su cadena de conexión antes de ejecutarlo.

Para comprobar en qué punto está una base:

```sql
SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1;
```

Un schema de Postgres por módulo: `identity`, `gyms`, `members`, `memberships`, `payments`,
`attendance`, `achievements`, `routines`, `classes`, `superadmin`, `communications`, `equipment`,
`notifications` y `logs`.

## Arrancar la API

```bash
cd src/Backend/GSM.API
dotnet run
```

- `https://localhost:7219` · `http://localhost:5175`
- Documentación interactiva (solo en `Development`): `https://localhost:7219/scalar`
- Health: `/health` y `/health/db`

En `Development` corre `DevSeeder`, que es idempotente y deja:

| Usuario | Rol | Clave |
|---|---|---|
| `admin@gsm.local` | SuperAdmin | `ChangeMe_123456` |
| `owner@demo.local` | Owner del gym `demo` | `ChangeMe_123456` |
| `staff@demo.local` | Staff del gym `demo` | `ChangeMe_123456` |

### Datos de demostración

`DevSeeder` solo deja las cuentas. Para revisar todas las pantallas con datos, con la API
arrancada:

```powershell
./scripts/seed-demo.ps1
```

Siembra el gym `demo`: 30 miembros (activos, por vencer, vencidos y uno dado de baja), planes,
pagos de dos meses, ventas de mostrador, seis semanas de asistencia, clases con reservas, asistencia
y una sesión cancelada, rutinas con historial y logros. También crea tres cuentas para la app
del miembro (`/app`) y cuatro gyms más para el panel SuperAdmin, cada uno en un estado distinto:
activo, en prueba, en mora y suspendido. Las cuentas y sus claves están en la cabecera del script.

Lo que tiene reglas de negocio pasa por la API. El historial (fechas pasadas) se escribe por
SQL, porque la API solo registra "hoy". Si el script detecta que el gym ya está sembrado, se
detiene.

## Arrancar la web

Con la API arriba:

```bash
cd src/Frontend/GSM.Web
dotnet run --launch-profile https
```

`https://localhost:7220` (panel, `/admin`, `/kiosk` y la app del miembro en `/app`). Se entra con el correo o
el celular; un miembro sin cuenta entra la primera vez con su número de documento y crea su contraseña. El
cliente de la API se regenera con `scripts/generate-client.ps1`.

La app nativa (`GSM.Mobile`, Android y Windows) se compila aparte:
`dotnet build src/Frontend/GSM.Mobile -f net10.0-android -p:RuntimeIdentifier=android-arm64`.
Compila para Android, pero aún no se ha verificado en un dispositivo real.

### APK de la app Android (release)

La URL de la API se incrusta al compilar (`GsmApiBaseUrl`, obligatoria en Release; sin ella la compilación falla). Para un teléfono (arm64):

```bash
dotnet publish src/Frontend/GSM.Mobile -f net10.0-android -c Release -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormat=apk -p:GsmApiBaseUrl=https://api.midominio.com -o artifacts/apk-arm64
```

Sale `com.gsm.mobile-Signed.apk`, firmado con la llave de depuración de Android: sirve para instalar a mano, pero no para subir a Google Play (para eso hace falta una llave propia).
Para el emulador de x64 usa `android-x64`. Hacen falta el SDK de Android y un JDK (`-p:AndroidSdkDirectory=...`, `-p:JavaSdkDirectory=...`).

## Tests

```bash
cd src/Backend
dotnet test GSM.sln
```

Son tests de integración **contra Postgres de verdad** (los de la UI, bUnit, están en `tests/GSM.Web.Tests`: `dotnet test tests/GSM.Web.Tests`), no en memoria: necesitan el servidor
arriba y la base `gsm_test` creada. La suite la migra y la limpia por sí sola.

Si alguna vez queda en mal estado —una ejecución cortada a medias, por ejemplo— se recrea sin
miedo: es desechable y la siguiente ejecución la vuelve a migrar.

```sql
DROP DATABASE gsm_test WITH (FORCE);
CREATE DATABASE gsm_test OWNER gsm;
```

## Pendiente de configurar

Nada de esto impide desarrollar, pero conviene saber en qué estado está:

- **Tareas periódicas y claves** — `Communications:UnsubscribeSigningKey`, `Communications:PublicApiBaseUrl` y `Jobs:Secret` tienen valores de desarrollo en `appsettings.Development.json`; en producción van por variable de entorno. La tarea periódica (recordatorios y envío de correos) no corre sola: en local se lanza a mano con
  `curl -k -X POST https://localhost:7219/internal/jobs/communications/run -H "X-Job-Secret: dev-only-jobs-secret-change-me-32-bytes+"`
  y en producción la invoca una tarea programada de Dokploy.

- **Despliegue** — producción corre en un servidor propio con Dokploy y **cada push a `main` despliega solo**. Hay
  `docker-compose.yml` (API, web y tarea periódica; las migraciones se aplican aparte; **la base de datos es un Postgres externo**,
  fuera del compose) y un Dockerfile por servicio. Guía en la sección «Docker» de abajo.
- **Cloudflare R2** (`Storage:R2`) — configurado en producción; en local, sin credenciales, las subidas de archivo responden 500.
- **Resend** (`Email:Resend`) — sin API key, los correos se escriben en el log en vez de enviarse. En producción está en modo de pruebas (falta verificar el dominio).
- **Firebase** (`Notifications:Fcm`) — incompleto: sin configurar no hay push (web ni Android).
- **`Checkin:SigningKey`** — clave HMAC (≥ 32 caracteres) de los QR del gym. Vacía en `appsettings.json` y la API
  no arranca sin ella; en desarrollo viene en `appsettings.Development.json`. En producción hay que definirla.

Para probar el check-in con QR en local, entra como staff y abre `https://localhost:7220/kiosk`: muestra el QR del gym;
el enlace que lleva abre `/app/checkin` en la app del miembro.

## Docker

Servicios: `api` (puerto 8080), `web` (nginx, puerto 8080) y `cron` (la tarea periódica de Comunicaciones). La base de datos **no** está en el
compose: se conecta por `DB_CONNECTION`. Las migraciones **tampoco** corren en el despliegue (la API no migra al arrancar): se aplican aparte,
antes de desplegar una versión que traiga migraciones nuevas (ver «Migraciones en producción»).

```bash
cp .env.example .env     # rellenar: cadena de conexión, URLs públicas y claves (openssl rand -base64 48)
docker compose up -d --build
docker compose run --rm api --seed-superadmin    # solo la primera vez (SUPERADMIN_EMAIL / SUPERADMIN_PASSWORD del .env)
```

Probarlo en el PC: crear antes la red una vez (`docker network create dokploy-network`) y poner en `DB_CONNECTION` el Postgres de tu máquina
(`Host=host.docker.internal`). El compose no publica puertos; para abrirlos en local, añade `ports` a `api` y `web` en un archivo aparte.

- La URL de la API de la web se escribe al arrancar desde `PUBLIC_API_URL`: la misma imagen sirve para cualquier entorno.
- Detrás de un proxy (Traefik/Dokploy) la API usa `X-Forwarded-For/Proto`; el compose no publica puertos.
- Las llaves de Data Protection van en el volumen `dataprotection`, para que los enlaces de restablecimiento sobrevivan a los redespliegues.
- En Dokploy: aplicación tipo «Docker Compose», rama `main`, «Compose Path» = `./docker-compose.yml`, y dominios hacia el puerto 8080 de `api`
  y de `web`. El compose conecta `api` y `web` a `dokploy-network`, donde se resuelve el nombre interno del Postgres de Dokploy y desde donde
  Traefik enruta.
- `DB_CONNECTION` con la base de Dokploy: `Host=<nombre-del-servicio-postgres>;Port=5432;Database=<db>;Username=<usuario>;Password=<clave>`.

### Migraciones en producción

Se aplican a mano contra la base real, desde una máquina que la alcance (en Dokploy, exponiéndola con un túnel SSH o con el puerto restringido a tu IP):

```bash
cd src/Backend
GSM_MIGRATIONS_CONNECTION="Host=...;Port=5432;Database=gsm_prod_db;Username=...;Password=..." dotnet ef database update --project GSM.Infrastructure
```

Orden de un despliegue con cambios de esquema: **copia de la base → migraciones → desplegar**. Si la migración crea
un enum de Postgres, comprueba que el contenedor de la API se recreó (`docker ps`) y, si no, reinícialo: Npgsql lee
los tipos solo al arrancar y el enum nuevo da 500 hasta entonces. Después de la primera migración, crear el SuperAdmin con
`docker compose run --rm api --seed-superadmin`.
