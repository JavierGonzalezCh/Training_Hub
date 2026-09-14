# GSM — Contexto del proyecto

GSM (Gym System Management) es el codename técnico interno de un sistema de gestión de gimnasios B2B SaaS. El nombre comercial final aún no está definido (candidato: LudOS) — **usa siempre `GSM`** en código, namespaces, proyectos y schemas de base de datos. El nombre comercial solo vive en datos de tenant y marketing, nunca en código.

**Especificación técnica completa:** `docs/GSM-especificacion-tecnica.md`. Léela antes de generar entidades, casos de uso o endpoints — este archivo es solo el resumen que debes tener presente siempre.

**Plan de construcción por etapas:** `docs/BACKEND-MVP-PLAN.md` (etapas 0–8, MVP).

**Módulos posteriores al MVP**, cada uno con su especificación y su plan de construcción:

| Módulo | Especificación | Plan |
|---|---|---|
| Routines (etapa 9) | `docs/GSM-modulo-routines.md` | `docs/ROUTINES-BUILD-PLAN.md` |
| Classes (etapa 10) | `docs/GSM-modulo-classes.md` | `docs/CLASSES-BUILD-PLAN.md` |
| SuperAdmin (etapa 11) | `docs/GSM-modulo-superadmin.md` | `docs/SUPERADMIN-BUILD-PLAN.md` |

**Frontend** (etapas F0–F8): `docs/FRONTEND-BUILD-PLAN.md`. Blazor WASM (panel del gym, SuperAdmin y app del socio en `/app`) + MAUI Blazor Hybrid, que comparten las pantallas del socio en `GSM.Member.UI`; cliente generado desde OpenAPI. Vive en `src/Frontend/`.

## Estado actual

Fase: **Fase 2 y 3 construidas; falta desplegar.**

Las etapas 0–7 (MVP), **9 (Routines)**, **10 (Classes)** y **11 (SuperAdmin, sub-etapas 11.1 a 11.5)** están implementadas y validadas end-to-end, con **118 tests** de integración contra Postgres.

De la etapa 8 falta **solo el punto 4 (despliegue)**: no hay Dockerfile ni configuración de Railway. R2 y Resend están integrados pero **sin configurar**, así que las subidas de archivo fallan y los emails van al log.

Ningún módulo nuevo de backend está en construcción. **Del frontend están hechas F0–F6** (`docs/FRONTEND-BUILD-PLAN.md`): `src/Frontend/GSM.Frontend.slnx` con `GSM.Client` (cliente NSwag generado + `AuthenticatedHttpHandler`, `ApiError`, `ITokenStore`, `JwtPayload`), `GSM.UI.Shared` (`GsmAuthenticationStateProvider`, `SessionService`, `GymContext`, `GsmTheme` con la identidad visual v4.0, `SubscriptionBanner`, `RequireRole`, `ServerTable<T>`, `StatusChip`, `QrCodeSvg`), `GSM.Web` (Blazor WASM + MudBlazor en `7220`/`5220`: login, "olvidé mi contraseña", shell del panel, Socios, Planes/membresías/pagos, recepción y asistencia, dashboard, configuración solo-Owner, catálogo de ejercicios, editor de rutinas en la ficha del socio, y clases — CRUD, horario semanal proyectado, roster y marcar asistencia/cancelar sesión) y `tests/GSM.Web.Tests` (bUnit, 47 tests). El cliente se regenera con `scripts/generate-client.ps1`. **Hueco H1 resuelto después de F8**: `api/gym/staff` (GET/POST/DELETE + `POST {id}/reactivate`, solo Owner) vive entero en Identity con `IStaffAccountService` — no es una escritura cruzada. La cuenta de staff nace **sin contraseña** y recibe por email un código de restablecimiento (vence en 24 h; si vence, sirve "olvidé mi contraseña"); desactivar no borra y revoca los refresh tokens. Pestaña "Staff" en `/settings`.

**Segundo defecto de NSwag encontrado y corregido en F6** (el primero fue el de `ReadResponseAsString` en F3): para una colección de enum en el **cuerpo** de una respuesta (no un enum suelto), el generador infiere `int` aunque en runtime viaje como string (`JsonStringEnumConverter` global) — rompía la deserialización de `ClassListItem`/`ClassResponse.DaysOfWeek` con una excepción no controlada. `scripts/generate-client.ps1` ahora reaplica el parche (`ICollection<int>` → `ICollection<string>`) después de cada regeneración; si aparece otra colección de enum nueva en una respuesta, sospecha lo mismo.

**Hueco de NSwag encontrado y corregido en F3:** la plantilla del cliente generado deja `ReadResponseAsString = false` por defecto, así que en el camino feliz de deserialización el `GsmApiException.Response` queda vacío y `ApiError` no puede leer el `detail`/`title` de un 409/422/403 (cae al mensaje genérico). Se corrige forzando `ReadResponseAsString = true` para los 26 clientes en `src/Frontend/GSM.Client/Generated/ReadResponseAsString.cs` (partial classes que implementan el hook `Initialize()`; el archivo no se pisa al regenerar porque vive fuera de `GsmApiClient.g.cs`).

**Con F6 el panel del gym está completo. F7 (app del socio) también está hecha**: `src/Frontend/GSM.Member.UI` (RCL con las pantallas del socio: registro por invitación en `/app/registro`, ficha propia con QR y check-in propio en `/app`, logros en `/app/logros`, rutina y registro por ejercicio en `/app/rutina`, horario y reservas de clases en `/app/clases`) alojada en `GSM.Web` bajo `/app` (PWA: `manifest.json` + `service-worker.js` sin caché, ver `docs/GSM-especificacion-tecnica.md` §1) y en `src/Frontend/GSM.Mobile` (MAUI Blazor Hybrid, Android + Windows; iOS pendiente de host macOS). **`GSM.Mobile` vive fuera de `GSM.Frontend.slnx`** (compilarlo con los targets multi-plataforma rompía el build normal del resto) — se compila aparte: `dotnet build src/Frontend/GSM.Mobile -f net10.0-android -p:RuntimeIdentifier=android-arm64`.

**Sin verificar: `GSM.Mobile` en un emulador Android real.** Este entorno no tiene SDK de Android ni emulador; el proyecto compila limpio (C#, Razor y XAML) pero no se ha podido ejecutar como pide el plan ("mismo guion en los dos hosts"). El lado web de `/app` sí está verificado en el navegador, incluido el ancho móvil.

**Huecos H2 y H4 resueltos**: `MeController.CreateRoutine` declara ya `RoutineResponse`, y `MeFeatureFlags` trae `MembersCanCreateRoutines`. Con el flag activo, `/app/rutina` ofrece "Crear mi rutina" (`/app/rutina/nueva`). El formulario de rutina vive en `GSM.UI.Shared/Routines` (`RoutineForm` + `RoutineDraft`) y lo comparten el editor del staff y el autoservicio del socio.

**F8 (panel SuperAdmin) también está hecha: el frontend planeado (F0–F8) está completo.** `/admin` (dashboard de plataforma), `/admin/gyms` (cartera + alta de gym) y `/admin/gyms/{id}` (ficha, editar plan/precio, renovar sin pago, registrar pago, suspender/reactivar, impersonar), `/admin/payments` (cobros del periodo), `/admin/impersonations` (auditoría). La impersonación (F8.4) guarda los tokens propios del superadmin en un segundo slot de `ITokenStore` (`BackupAsync`/`GetBackupAsync`/`ClearBackupAsync`) y entra al panel del gym con una política de autorización nueva en el cliente (`GsmPolicies.RequireStaff` en `GSM.UI.Shared`, espejo de la del backend: admite Owner, Staff o un SuperAdmin impersonando) — sin esa política, las páginas del panel (con `[Authorize(Roles="owner,staff")]`) habrían rechazado al superadmin impersonando pese a que el backend sí lo admite. Terminar la impersonación usa `forceLoad: true` para evitar una condición de carrera entre restaurar tokens y que el router reevalúe la autorización.

**No construido, fuera de alcance por ahora**: el catálogo de ejercicios del sistema reutilizado desde el panel SuperAdmin (mencionado en el plan para F8) — la pantalla de F6 sigue siendo solo-Staff; abrirla a superadmin sin impersonar exigía resolver el contexto de gym para un rol que no tiene uno, y no se hizo por tiempo.

Siguen pendientes el despliegue (Dockerfile/Railway, etapa 8 punto 4 del backend) y los módulos sin planificar (Community, Reports, Locations).

**Cómo levantar el entorno local está en `README.md`** — Postgres 18 nativo, bases `gsm` y `gsm_test`, migraciones a mano (la API no migra al arrancar).

Trabajamos incremental: Domain → Infrastructure/DbContext+migración → Application → API. **No implementes más de una etapa (ni sub-etapa) sin confirmación.**

## Stack

.NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL 18 · JWT + ASP.NET Identity · FluentValidation · Mapperly · Serilog

Sin MediatR: los comandos se invocan directo y los eventos in-process usan un despachador propio (ver abajo).

## Estructura de la solución

```
src/Backend/
  GSM.Domain/          → entidades, enums, invariantes. Cero dependencias externas.
  GSM.Application/     → casos de uso, DTOs, interfaces públicas por módulo, eventos.
  GSM.Infrastructure/  → EF Core, repositorios, clientes externos (R2, Resend, FCM).
  GSM.API/             → controllers, middleware, auth.
```

## Arquitectura: monolito con disciplina de módulos

Un solo monolito (no microservicios, no proyectos .csproj separados por módulo). Dentro de cada capa, carpetas por módulo:

```
GSM.Application/{Módulo}/
  ├── {UseCase}.cs
  ├── Public/    ← interfaz de lectura que otros módulos pueden consumir (I{Módulo}Reader)
  └── Events/    ← eventos de dominio que este módulo publica
```

**Regla dura:** un módulo nunca referencia directamente la entidad Domain, DbContext o repositorio concreto de otro módulo. Solo consume su `Public/I{Módulo}Reader` (síncrono, solo lectura) o eventos (asíncrono, in-process). Ninguna escritura cruzada directa.

#### Excepción: escritura cruzada vía `Public/`

Hay tres casos donde un módulo necesita **escribir** en otro de forma síncrona, porque la operación debe ser atómica o su resultado forma parte de la respuesta. Se resuelven con una interfaz de escritura en el `Public/` del módulo dueño, nunca tocando su repositorio ni su entidad:

- `Identity.Public.IUserProvisioning` — alta de gym y canje de invitación crean el `AppUser` dentro de la misma transacción del caso de uso. Un evento dejaría el gym o el miembro sin cuenta si el handler falla.
- `Achievements.Public.IAchievementService` — el check-in evalúa los logros de forma síncrona para devolver los desbloqueos en su propia respuesta (decisión (b) de la etapa 7: mejor UX que hacer que la app consulte después).
- `Gyms.Public.IGymAdministration` — `suspend` y `reactivate` de SuperAdmin (etapa 11.1) escriben `Gym.Status`, el estado comercial manual. El gym suspendido **es** la respuesta de la operación, no una reacción a ella; además el bloqueo depende de ese dato y no puede quedar a medias.

**Antes de añadir una cuarta, agota el camino normal.** La excepción se justifica solo por atomicidad o porque el resultado va en la misma respuesta; "es más cómodo" no basta. La regla sigue siendo: lecturas por `I{Módulo}Reader`, reacciones por evento. El alta de suscripción de la etapa 11 es el contraejemplo a imitar: como no forma parte de la respuesta del alta de gym, se resolvió con un handler de `GymCreated`, no con una excepción.

### Casos de uso

Una clase por caso de uso con un único método `HandleAsync`, registrada en DI e inyectada directamente en el controller. Sin bus de comandos, sin `ISender`.

### Eventos in-process

En `GSM.Application/Common/Events/`:

- `IDomainEvent` — marcador.
- `IDomainEventHandler<TEvent>` — un método `HandleAsync(TEvent, CancellationToken)`.
- `IDomainEventPublisher` / `DomainEventPublisher` — resuelve del contenedor todos los handlers del tipo y los invoca.

Los handlers se registran por assembly scanning. La publicación ocurre **después** de que la transacción del caso de uso commitea, nunca dentro. Un handler que falla se loguea y no tumba la operación que publicó el evento.

Módulos MVP, ya implementados: **Identity → Gyms → Members → Memberships → Payments → Attendance → Achievements.**

Fase 2/3, los tres construidos: **Routines** (9), **Classes** (10) y **SuperAdmin** (11).

Fuera de esa cadena vive `GSM.Application/MemberStreak/`, que no es un módulo de negocio sino el compositor de la racha: junta Attendance, Routines y el congelador de Achievements para resolver `GET /api/members/{id}/streak` y `GET /api/me/streak`.

Módulos sin planificar todavía (no implementar): Community, Reports, Locations.

## Multi-tenancy

Toda tabla de negocio lleva `gym_id`. Global Query Filter en EF Core (`HasQueryFilter(e => e.GymId == _currentUserService.GymId)`), desactivado solo si `Role == SuperAdmin`. `ICurrentUserService` en Infrastructure lee `GymId`/`UserId`/`Role` del JWT.

## Invariantes de negocio: fail-fast

Las entidades de Domain validan sus invariantes en el constructor/factory estático, lanzando `DomainException` si se violan (no se usa `Result<T>`). Jerarquía en `GSM.Domain/Common/Exceptions/`:

- `DomainException` (abstracta)
- `ValidationDomainException` → 422
- `NotFoundException` → 404
- `ConflictException` → 409
- `ForbiddenDomainException` → 403

Un middleware global en la API mapea por tipo a `ProblemDetails`. Cualquier otra excepción → 500 genérico, con el detalle solo en el log.

**400 queda reservado para errores de forma del request** (payload malformado, tipos que no castean, validación de FluentValidation vía `ValidationActionFilter`, que responde `ValidationProblemDetails` con los errores por campo). Una invariante de dominio violada llega con un request bien formado que el servidor entiende pero no puede procesar: eso es 422. Así el cliente distingue por status “corrige el formulario” de “la operación no procede en este estado”. La frontera con 409: `ValidationDomainException` es para valores que no satisfacen la regla; `ConflictException` para choques de estado (duplicados, carreras).

## Mapeo entidad → DTO

Mapperly (source generator), un mapper parcial estático por módulo. Las consultas de lectura pesadas (listados paginados, dashboard) usan proyección explícita con `Select` en la query, no mapeo post-materialización.

Routines y Classes mapean a mano (`RoutineMapper`, `ClassMapper`): sus DTOs son árboles anidados que Mapperly no resuelve sin más configuración de la que ahorra. Es la excepción aceptada, no un descuido.

## Base de datos

Un schema de PostgreSQL por módulo, los diez ya creados: `identity`, `gyms`, `members`, `memberships`, `payments`, `attendance`, `achievements`, `routines`, `classes`, `superadmin`. Convenciones: `snake_case`, tablas en plural, PK siempre `id` (uuid), FK `{tabla_singular}_id`, índices `ix_{tabla}_{columna}`, constraints `fk_{tabla}_{tabla_referenciada}`, enums nativos de Postgres.

No hay FK entre schemas de módulos distintos: la referencia se valida en el caso de uso contra el `I{Módulo}Reader` correspondiente.

## Convenciones de código

- Inglés en código (clases, propiedades, métodos). Español permitido solo en contenido de datos.
- PascalCase (clases/métodos), camelCase (variables/parámetros), prefijo `I` en interfaces.
- IDs `Guid` v7 (`Guid.CreateVersion7()`), generados en el dominio.
- **Instantes** en `DateTimeOffset` UTC (`created_at`, `paid_at`, expiraciones). **Días de negocio** en `DateOnly`, y salen de `IClock.Today` en la zona del gym — **nunca** de `DateTime.UtcNow`: Colombia es UTC-5 y derivarlos de UTC adelanta el día entre las 19:00 y medianoche. Toda lectura de tiempo pasa por `IClock` (`UtcNow` / `Today` / `BusinessDayRange` / `ToBusinessLocal`), que es inyectable en tests.
- **Agrupar por día o por mes** (cierres de caja, ingresos, horas pico) usa `IClock.BusinessDayRange`, no un rango UTC. Es un defecto que ya apareció dos veces en el proyecto.
- Dinero: `decimal(12,2)`, COP.
- Logging con Serilog, structured, un log de entrada/salida por caso de uso. Nunca loguear passwords ni tokens.
- Conventional Commits (`feat:`, `fix:`, `refactor:`).
- **Los tests no modifican datos sembrados por migraciones** (el catálogo de ejercicios del sistema): `gsm_test` no se trunca entre ejecuciones y cualquier cambio queda para siempre. Si un test necesita editar algo de sistema, crea su propio registro desechable y lo borra en un `finally`.
- **Para probar por HTTP con la identidad real**, el patrón es `SubscriptionGateHttpTests`: `WithWebHostBuilder` quitando `MutableCurrentUser`, login por `/api/auth/login` y el JWT de verdad.

## Alcance

**MVP (hecho):** 7 módulos — Identity, Gyms, Members, Memberships, Payments, Attendance, Achievements — detallados en `docs/GSM-especificacion-tecnica.md`.

**Fase 2/3 (hecho):** Routines (9), Classes (10) y SuperAdmin (11) — ver la tabla de documentos arriba.

No construyas Community, Reports ni Locations todavía.

### Notas de Routines que afectan a módulos ya cerrados

- **`Exercise` no implementa `ITenantEntity`.** Es la primera entidad con `gym_id` nullable (`null` = catálogo del sistema). Su filtrado va explícito en `ExerciseRepository`, no en el Global Query Filter — esa pieza no se toca.
- **La racha pasa a medirse contra el calendario de la rutina** (el descanso planificado no la rompe). `StreakCalculator` acepta un calendario opcional y **sin él se comporta igual que hoy**; el cálculo lo compone un handler, porque Attendance (6) no puede depender de Routines (9).
- **El congelador de racha vive en `achievements`**, no en Routines: aplica también a socios sin rutina.

### Notas de Classes que afectan a módulos ya cerrados

- **Las sesiones no se generan por adelantado: el horario es una proyección.** Una ocurrencia solo se materializa cuando hay algo que guardar (reserva, asistencia, cancelación puntual), y el índice único `(class_id, session_date)` la hace idempotente. Mismo patrón perezoso que el congelador de racha, y por el mismo motivo: **no hay scheduler en el sistema.**
- **Asistir a una clase registra el check-in del día por evento**, no por escritura cruzada: `ClassAttendanceRegistered` lo consume un handler de Attendance, que ya era idempotente por día desde la etapa 6.
- **`class_booking_status` persiste `no_show`** desde la migración `Etapa10_5_BookingStatusNoShow`; antes guardaba `noshow` por registrarse con `LowerCaseNameTranslator`. Un enum con valores de varias palabras va con `SnakeCaseNameTranslator`, nunca con guion bajo en el nombre del miembro C#: ese atajo (el de `Equipment`) saca `Smith_machine` en el JSON de la API.

### Notas de SuperAdmin (etapa 11)

- **`Gym.Status` y `Gym.Plan` vienen de la etapa 2 y SuperAdmin les dio sentido sin migrarlos:** `gym_status` significa *estado comercial manual* (`trial`/`active`/`suspended`) y los estados `overdue` y `disabled` se **derivan** de `paid_through`, nunca se persisten — mismo criterio que las membresías `expired` de la etapa 4.
- **`trial_ends_on` nulo no es una prueba indefinida.** Todo gym nace con `gym_status = trial`, así que si el nulo valiera como "trial para siempre" ningún cliente llegaría nunca a `overdue` ni a `disabled` y el bloqueo no serviría de nada. Sin fecha de fin, manda `paid_through`.
- **La suscripción se da de alta al crear el gym**, por un handler de `GymCreated` (30 días de prueba, precio 0). Si ese handler fallara, el gym queda sin suscripción y opera con normalidad; `PUT /api/admin/gyms/{id}/subscription` la repone, creándola si no existe.
- **El bloqueo por impago va en un filtro global de la API**, no repartido por casos de uso: es transversal como el multi-tenancy. Un gym **sin suscripción no se bloquea** — ante un dato faltante se deja pasar, porque dejar tirado a un cliente es peor error que no cortarle a un moroso.
- **Los pagos del SaaS no se mezclan con los del gym.** Se comparte el enum `payment_method` y nada más: son bolsillos distintos y no pueden acabar en el mismo cierre de caja.
- **Ni Gyms ni Identity dependen de SuperAdmin.** El bloque `subscription` (`SubscriptionInfo`, en `Application/Common`) lo componen los controllers, y el login consulta la suspensión por `IGymLoginGate`: un puerto que declara Identity e implementa SuperAdmin en Infrastructure. Así el único ciclo entre módulos es el deliberado `Attendance ↔ Achievements`.
- **La impersonación es de solo lectura y la impone el mismo filtro**: token de 15 minutos que conserva la identidad real del superadmin en `sub` y solo admite `GET`. Para que pueda leer, `RequireStaff` admite a un superadmin cuyo token trae `impersonating=true` (el token lleva el rol real, y sin esa excepción recibía 403 en todo el gym); `RequireOwner` no se abre porque no protege ningún GET.
