# GSM — Backend

Sistema de gestión de gimnasios B2B SaaS. API en .NET 10 + PostgreSQL.

El contexto del proyecto, las convenciones y el estado de cada módulo están en [`CLAUDE.md`](CLAUDE.md);
la especificación técnica y los planes de construcción, en [`docs/`](docs/). Este archivo solo explica
**cómo levantar el backend en una máquina de desarrollo**.

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
`attendance`, `achievements`, `routines`, `classes`, `superadmin`.

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

## Tests

```bash
cd src/Backend
dotnet test GSM.sln
```

Son tests de integración **contra Postgres de verdad**, no en memoria: necesitan el servidor
arriba y la base `gsm_test` creada. La suite la migra y la limpia por sí sola.

Si alguna vez queda en mal estado —una ejecución cortada a medias, por ejemplo— se recrea sin
miedo: es desechable y la siguiente ejecución la vuelve a migrar.

```sql
DROP DATABASE gsm_test WITH (FORCE);
CREATE DATABASE gsm_test OWNER gsm;
```

## Pendiente de configurar

Nada de esto impide desarrollar, pero conviene saber en qué estado está:

- **Comunicaciones** (etapa 13) — `Communications:UnsubscribeSigningKey`, `Communications:PublicApiBaseUrl` y `Jobs:Secret` tienen valores de desarrollo en `appsettings.Development.json`; en producción van por variable de entorno. La tarea periódica (recordatorios y envío de correos) no corre sola: en local se lanza a mano con
  `curl -k -X POST https://localhost:7219/internal/jobs/communications/run -H "X-Job-Secret: dev-only-jobs-secret-change-me-32-bytes+"`
  y en producción la invoca una tarea programada de Dokploy (ver `docs/DEPLOY-DOKPLOY-PLAN.md`).

- **Despliegue** — no hay `Dockerfile` ni configuración de despliegue (etapa 8, punto 4). Se desplegará con Dokploy en servidor propio: ver `docs/DEPLOY-DOKPLOY-PLAN.md`. En producción
  la base es un Postgres gestionado, no un contenedor propio.
- **Cloudflare R2** (`Storage:R2`) — sin credenciales, las subidas de archivo responden 500.
- **Resend** (`Email:Resend`) — sin API key, los correos se escriben en el log en vez de enviarse.
- **`Checkin:SigningKey`** — clave HMAC (≥ 32 caracteres) de los QR del gym. Vacía en `appsettings.json` y la API
  no arranca sin ella; en desarrollo viene en `appsettings.Development.json`. En producción hay que definirla.

Para probar el check-in con QR en local, entra como staff y abre `https://localhost:7220/kiosk`: muestra el QR del gym;
el enlace que lleva abre `/app/checkin` en la app del miembro.
