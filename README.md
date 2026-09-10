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

- **Despliegue** — no hay `Dockerfile` ni configuración de Railway (etapa 8, punto 4). En producción
  la base es un Postgres gestionado, no un contenedor propio.
- **Cloudflare R2** (`Storage:R2`) — sin credenciales, las subidas de archivo responden 500.
- **Resend** (`Email:Resend`) — sin API key, los correos se escriben en el log en vez de enviarse.
