# TrainingHub

**TrainingHub** es un software para administrar gimnasios, ofrecido como servicio (SaaS) a varios
negocios a la vez: gimnasios, boxes de crossfit, estudios, entrenadores personales y academias. Cada
negocio ve solo sus datos, y una cuenta de administración de la plataforma gestiona la cartera
completa.

En el código el proyecto se llama **GSM** (*Gym System Management*): así se nombran la solución, los
proyectos y la base de datos.

## Qué hace

El sistema tiene dos caras, más una consola para quien opera la plataforma:

**TrainingHub OS — para el equipo del gimnasio** (dueño y staff)

- Ficha de cada miembro, con su membresía, sus pagos, su asistencia y su progreso.
- Planes y membresías: vencimientos, periodo de gracia, congelar y cancelar.
- Cobros, anulaciones y caja por turno con arqueo.
- Control de entrada con un QR del gimnasio que el miembro escanea, o con su documento en recepción.
- Clases con horario, cupos y reservas.
- Rutinas de entrenamiento, plantillas y seguimiento de qué tanto las cumple cada miembro.
- Inventario de máquinas, novedades para los miembros, recordatorios de vencimiento y avisos.
- Tableros con los indicadores del negocio.

**TrainingHub GO — para el miembro** (app web instalable y app Android)

- La rutina de hoy, paso a paso, guardando cada serie.
- El plan de la semana, con la opción de recuperar una sesión perdida.
- Progreso: racha, fuerza, logros con medallas y ranking del gimnasio.
- Mapa muscular con la recuperación de cada grupo.
- Clases, reservas y novedades del gimnasio, y el QR para entrar.

**Administración de la plataforma** (`/admin`)

- Alta de gimnasios, suscripciones y cobro de la plataforma, suspensión por impago.
- Entrar a la cuenta de un gimnasio para darle soporte (con motivo y registro de cada acción).
- Métricas de toda la cartera.

## Cómo está construido

| Parte | Tecnología |
|---|---|
| API | .NET 10, ASP.NET Core, EF Core 10, autenticación con JWT y ASP.NET Identity |
| Base de datos | PostgreSQL 18 |
| Web (panel, app del miembro y administración) | Blazor WebAssembly con MudBlazor |
| App nativa | .NET MAUI Blazor Hybrid (Android y Windows) |

La API es un **monolito modular**: un solo servicio dividido en módulos independientes (miembros,
membresías, pagos, asistencia, rutinas, clases, logros, notificaciones…), cada uno con su propio
schema en PostgreSQL. Los módulos solo se comunican por interfaces de lectura y eventos internos.
Todas las tablas de negocio llevan el identificador del gimnasio y EF Core filtra por él en cada
consulta, así que un gimnasio nunca ve los datos de otro.

```
src/Backend/     API: GSM.Domain · GSM.Application · GSM.Infrastructure · GSM.API   (GSM.sln)
src/Frontend/    Web y app: GSM.Client · GSM.UI.Shared · GSM.Panel.UI · GSM.Member.UI · GSM.Web
                 (GSM.Frontend.slnx) y GSM.Mobile (app nativa, se compila aparte)
tests/           GSM.IntegrationTests (contra PostgreSQL real) · GSM.Web.Tests (componentes, bUnit)
scripts/         generación del cliente de la API y datos de demostración
```

---

## Ponerlo a funcionar

### 1. Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [PostgreSQL 18](https://www.postgresql.org/download/) escuchando en `localhost:5432`
- La herramienta de migraciones de EF Core:

```bash
dotnet tool install --global dotnet-ef
```

- PowerShell y `psql` en el `PATH`, solo si quieres cargar los datos de demostración.

### 2. Crear la base de datos

Con `psql` como superusuario (`postgres`):

```sql
CREATE ROLE gsm LOGIN PASSWORD 'gsm';
CREATE DATABASE gsm OWNER gsm;
CREATE DATABASE gsm_test OWNER gsm;
```

`gsm` es la base de desarrollo y `gsm_test` la de los tests. La cadena de conexión de desarrollo ya
viene configurada en `src/Backend/GSM.API/appsettings.Development.json`
(`Host=localhost;Port=5432;Database=gsm;Username=gsm;Password=gsm`), junto con las demás claves que la
API necesita para arrancar en local.

### 3. Aplicar las migraciones

La API **no** migra al arrancar. Hazlo la primera vez y cada vez que traigas cambios con migraciones
nuevas:

```bash
cd src/Backend
dotnet ef database update --project GSM.Infrastructure
```

### 4. Arrancar la API

```bash
cd src/Backend/GSM.API
dotnet run --launch-profile https
```

Queda en `https://localhost:7219`. La documentación interactiva de los endpoints está en
`https://localhost:7219/scalar` y el estado del servicio en `/health`.

Al arrancar en desarrollo crea estas cuentas (si no existen):

| Correo | Rol | Contraseña |
|---|---|---|
| `admin@gsm.local` | Administrador de la plataforma | `ChangeMe_123456` |
| `owner@demo.local` | Dueño del gimnasio `demo` | `ChangeMe_123456` |
| `staff@demo.local` | Staff del gimnasio `demo` | `ChangeMe_123456` |

### 5. Arrancar la web

En otra terminal, con la API arriba:

```bash
cd src/Frontend/GSM.Web
dotnet run --launch-profile https
```

Abre `https://localhost:7220` y entra con una de las cuentas de arriba:

| Ruta | Qué es |
|---|---|
| `/` | Panel del gimnasio (TrainingHub OS) |
| `/app` | App del miembro (TrainingHub GO) |
| `/admin` | Administración de la plataforma |
| `/kiosk` | Pantalla de recepción con el QR de entrada |

### 6. Datos de demostración (opcional)

Las cuentas de arriba llegan vacías. Para ver todas las pantallas con datos, con la API arrancada:

```powershell
./scripts/seed-demo.ps1
```

Llena el gimnasio `demo` con 30 miembros en distintos estados, planes, dos meses de pagos, seis semanas
de asistencia, clases con reservas, rutinas con historial y logros. También crea cuentas de miembro
para probar `/app` y cuatro gimnasios más para `/admin` (activo, en prueba, en mora y suspendido). Las
cuentas que crea y sus contraseñas están al inicio del script. Se ejecuta una sola vez: si el gimnasio
ya tiene datos, se detiene.

## Tests

```bash
dotnet test src/Backend/GSM.sln          # integración, contra PostgreSQL real
dotnet test tests/GSM.Web.Tests          # componentes de la interfaz (bUnit)
```

Los de integración necesitan PostgreSQL arriba y la base `gsm_test` creada; la migran y la limpian
solos. Si queda en mal estado (por ejemplo, tras cortar una ejecución), se puede recrear sin problema:

```sql
DROP DATABASE gsm_test WITH (FORCE);
CREATE DATABASE gsm_test OWNER gsm;
```

## Servicios externos (opcionales)

En local el sistema funciona sin ninguno de estos; solo se pierde la función correspondiente:

| Servicio | Para qué | Sin configurar |
|---|---|---|
| Cloudflare R2 (`Storage:R2`) | Fotos y archivos | Las subidas fallan |
| Resend (`Email:Resend`) | Correos | Los correos se escriben en el log |
| Firebase Cloud Messaging (`Notifications:Fcm`) | Notificaciones push | No hay push |

Los recordatorios y el envío de correos los hace una tarea periódica que en producción se invoca cada
pocos minutos. En local se lanza a mano:

```bash
curl -k -X POST https://localhost:7219/internal/jobs/communications/run -H "X-Job-Secret: dev-only-jobs-secret-change-me-32-bytes+"
```

## App Android

```bash
dotnet build src/Frontend/GSM.Mobile -f net10.0-android -p:RuntimeIdentifier=android-arm64
```

Para generar un APK instalable hay que indicar la URL de la API, que queda incrustada en la app:

```bash
dotnet publish src/Frontend/GSM.Mobile -f net10.0-android -c Release -p:RuntimeIdentifier=android-arm64 -p:AndroidPackageFormat=apk -p:GsmApiBaseUrl=https://api.midominio.com -o artifacts/apk-arm64
```

Hacen falta el SDK de Android y un JDK (`-p:AndroidSdkDirectory=...`, `-p:JavaSdkDirectory=...`). El
APK sale firmado con la llave de depuración: sirve para instalarlo a mano, no para Google Play. Para el
emulador usa `android-x64`.

## Despliegue con Docker

`docker-compose.yml` levanta tres servicios: `api`, `web` (nginx) y `cron` (la tarea periódica). La
base de datos va aparte: el compose se conecta a un PostgreSQL existente.

```bash
cp .env.example .env              # completar cadena de conexión, URLs públicas y claves
docker compose up -d --build
docker compose run --rm api --seed-superadmin   # solo la primera vez: crea la cuenta de administración
```

- Las claves se generan con `openssl rand -base64 48`.
- Las migraciones no corren en el despliegue. Se aplican antes, desde una máquina que alcance la base:

```bash
cd src/Backend
GSM_MIGRATIONS_CONNECTION="Host=...;Port=5432;Database=...;Username=...;Password=..." dotnet ef database update --project GSM.Infrastructure
```

- Si una migración crea un tipo enum nuevo en PostgreSQL, reinicia la API después: lee esos tipos solo
  al arrancar.
- El compose está pensado para correr detrás de un proxy (Traefik, en la red `dokploy-network`) y no
  publica puertos. Para probarlo en tu máquina, crea la red (`docker network create dokploy-network`),
  usa `Host=host.docker.internal` en la cadena de conexión y añade `ports` a `api` y `web` en un
  archivo de override.
