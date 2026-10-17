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
| Check-in con QR del gym (etapa 12) | `docs/GSM-especificacion-tecnica.md` (Attendance) | `docs/CHECKIN-QR-BUILD-PLAN.md` |
| Comunicaciones (etapa 13, **hecho**) | `docs/GSM-modulo-comunicaciones.md` | `docs/COMMUNICATIONS-BUILD-PLAN.md` |
| Equipment — inventario de máquinas, estado y fallas (**nuevo, en revisión, sin construir**) | `docs/GSM-modulo-equipment.md` | sub-etapas E1–E4 en la misma especificación |
| Despliegue con Dokploy (etapa 8, punto 4, **planificado, sin empezar**) | — | `docs/DEPLOY-DOKPLOY-PLAN.md` |

**Hecho — Check-in con QR del gym (etapa 12, sub-etapas 12.1–12.6):** `docs/CHECKIN-QR-BUILD-PLAN.md`. El gym muestra el QR en `/kiosk` (rotativo cada 30 s o cartel impreso fijo) y el miembro lo escanea con la cámara del teléfono o con el escáner de la app nativa; días de gracia por gym; el QR personal del miembro ya no existe.

**Frontend** (etapas F0–F8): `docs/FRONTEND-BUILD-PLAN.md`. Blazor WASM (panel del gym, SuperAdmin y app del miembro en `/app`) + MAUI Blazor Hybrid, que comparten las pantallas del miembro en `GSM.Member.UI`; cliente generado desde OpenAPI. Vive en `src/Frontend/`.

## Estado actual

Fase: **Fase 2 y 3 construidas; falta desplegar.**

Las etapas 0–7 (MVP), **9 (Routines)**, **10 (Classes)** y **11 (SuperAdmin, sub-etapas 11.1 a 11.5)** están implementadas y validadas end-to-end, con **160 tests** de integración contra Postgres.

De la etapa 8 falta **solo el punto 4 (despliegue)**: no hay Dockerfile ni configuración de despliegue. **Se despliega en servidor propio con Dokploy, no en Railway** (decidido el 2026-10-05; plan en `docs/DEPLOY-DOKPLOY-PLAN.md`). R2 y Resend están integrados pero **sin configurar**, así que las subidas de archivo fallan y los emails van al log.

Ningún módulo nuevo de backend está en construcción. **Del frontend están hechas F0–F6** (`docs/FRONTEND-BUILD-PLAN.md`): `src/Frontend/GSM.Frontend.slnx` con `GSM.Client` (cliente NSwag generado + `AuthenticatedHttpHandler`, `ApiError`, `ITokenStore`, `JwtPayload`), `GSM.UI.Shared` (`GsmAuthenticationStateProvider`, `SessionService`, `GymContext`, `GsmTheme` con la identidad visual v4.0, `SubscriptionBanner`, `RequireRole`, `ServerTable<T>`, `StatusChip`, `QrCodeSvg`), `GSM.Web` (Blazor WASM + MudBlazor en `7220`/`5220`: login, "olvidé mi contraseña", shell del panel, Miembros, Planes/membresías/pagos, recepción y asistencia, dashboard, configuración solo-Owner, catálogo de ejercicios, editor de rutinas en la ficha del miembro, y clases — CRUD, horario semanal proyectado, roster y marcar asistencia/cancelar sesión) y `tests/GSM.Web.Tests` (bUnit, 47 tests). El cliente se regenera con `scripts/generate-client.ps1`. **Hueco H1 resuelto después de F8**: `api/gym/staff` (GET/POST/DELETE + `POST {id}/reactivate`, solo Owner) vive entero en Identity con `IStaffAccountService` — no es una escritura cruzada. La cuenta de staff nace **sin contraseña** y recibe por email un código de restablecimiento (vence en 24 h; si vence, sirve "olvidé mi contraseña"); desactivar no borra y revoca los refresh tokens. Pestaña "Staff" en `/settings`.

**Segundo defecto de NSwag encontrado y corregido en F6** (el primero fue el de `ReadResponseAsString` en F3): para una colección de enum en el **cuerpo** de una respuesta (no un enum suelto), el generador infiere `int` aunque en runtime viaje como string (`JsonStringEnumConverter` global) — rompía la deserialización de `ClassListItem`/`ClassResponse.DaysOfWeek` con una excepción no controlada. `scripts/generate-client.ps1` ahora reaplica el parche (`ICollection<int>` → `ICollection<string>`) después de cada regeneración; si aparece otra colección de enum nueva en una respuesta, sospecha lo mismo.

**Hueco de NSwag encontrado y corregido en F3:** la plantilla del cliente generado deja `ReadResponseAsString = false` por defecto, así que en el camino feliz de deserialización el `GsmApiException.Response` queda vacío y `ApiError` no puede leer el `detail`/`title` de un 409/422/403 (cae al mensaje genérico). Se corrige forzando `ReadResponseAsString = true` para los 26 clientes en `src/Frontend/GSM.Client/Generated/ReadResponseAsString.cs` (partial classes que implementan el hook `Initialize()`; el archivo no se pisa al regenerar porque vive fuera de `GsmApiClient.g.cs`).

**Con F6 el panel del gym está completo. F7 (app del miembro) también está hecha**: `src/Frontend/GSM.Member.UI` (RCL con las pantallas del miembro: registro por invitación en `/app/registro`, ficha propia con QR y check-in propio en `/app`, logros en `/app/logros`, rutina y registro por ejercicio en `/app/rutina`, horario y reservas de clases en `/app/clases`) alojada en `GSM.Web` bajo `/app` (PWA: `manifest.json` + `service-worker.js` sin caché, ver `docs/GSM-especificacion-tecnica.md` §1) y en `src/Frontend/GSM.Mobile` (MAUI Blazor Hybrid, Android + Windows; iOS pendiente de host macOS). **`GSM.Mobile` vive fuera de `GSM.Frontend.slnx`** (compilarlo con los targets multi-plataforma rompía el build normal del resto) — se compila aparte: `dotnet build src/Frontend/GSM.Mobile -f net10.0-android -p:RuntimeIdentifier=android-arm64`.

**Sin verificar: `GSM.Mobile` en un emulador Android real.** Este entorno no tiene SDK de Android ni emulador; el proyecto compila limpio (C#, Razor y XAML) pero no se ha podido ejecutar como pide el plan ("mismo guion en los dos hosts"). El lado web de `/app` sí está verificado en el navegador, incluido el ancho móvil.

**Huecos H2 y H4 resueltos**: `MeController.CreateRoutine` declara ya `RoutineResponse`, y `MeFeatureFlags` trae `MembersCanCreateRoutines`. Con el flag activo, `/app/rutina` ofrece "Crear mi rutina" (`/app/rutina/nueva`). El formulario de rutina vive en `GSM.UI.Shared/Routines` (`RoutineForm` + `RoutineDraft`) y lo comparten el editor del staff y el autoservicio del miembro.

**F8 (panel SuperAdmin) también está hecha: el frontend planeado (F0–F8) está completo.** `/admin` (dashboard de plataforma), `/admin/gyms` (cartera + alta de gym) y `/admin/gyms/{id}` (ficha, editar plan/precio, renovar sin pago, registrar pago, suspender/reactivar, impersonar), `/admin/payments` (cobros del periodo), `/admin/impersonations` (auditoría). La impersonación (F8.4) guarda los tokens propios del superadmin en un segundo slot de `ITokenStore` (`BackupAsync`/`GetBackupAsync`/`ClearBackupAsync`) y entra al panel del gym con una política de autorización nueva en el cliente (`GsmPolicies.RequireStaff` en `GSM.UI.Shared`, espejo de la del backend: admite Owner, Staff o un SuperAdmin impersonando) — sin esa política, las páginas del panel (con `[Authorize(Roles="owner,staff")]`) habrían rechazado al superadmin impersonando pese a que el backend sí lo admite. Terminar la impersonación usa `forceLoad: true` para evitar una condición de carrera entre restaurar tokens y que el router reevalúe la autorización.

**No construido, fuera de alcance por ahora**: el catálogo de ejercicios del sistema reutilizado desde el panel SuperAdmin (mencionado en el plan para F8) — la pantalla de F6 sigue siendo solo-Staff; abrirla a superadmin sin impersonar exigía resolver el contexto de gym para un rol que no tiene uno, y no se hizo por tiempo.

**En curso — versión inicial** (`docs/VERSION-INICIAL-PLAN.md`): los huecos que salieron de recorrer el sistema de punta a punta en el navegador. No se despliega hasta cerrarlos. Hecha la sub-etapa **1.1 (anular un pago)**: `POST /api/payments/{id}/void` crea el asiento inverso fechado hoy con `reverses_payment_id` (índice único: un pago se anula una sola vez; una anulación no se anula) y motivo obligatorio en `Notes`. El original no cambia, así que el cierre de un día pasado sigue igual. `PaymentResponse.IsReversed` no sale de la entidad: lo resuelven los casos de uso de lectura con `IPaymentRepository.GetReversedIdsAsync`. Hechas también **1.2–1.4**: "Asignar membresía" cobra en el mismo paso (dos llamadas desde la UI, no una escritura cruzada); una membresía se puede **cancelar** (`cancelled`, con motivo: deja de cubrir, no da gracia y libera su hueco) y **congelar/reanudar** (`paused` + `paused_on`; al reanudar se corren su fin y el de las encadenadas). `IMembershipReader.GetLatestEndDateAsync` solo mira membresías `Active`.

**En curso — replanteamiento del sistema (desde 2026-10-07).** Se revisa módulo por módulo, sin tocar código. Cada módulo revisado deja su especificación nueva (hechos: `docs/GSM-modulo-members.md`, Members v2, sub-etapas M1–M6; `docs/GSM-modulo-routines.md`, sección "Replanteamiento v2", sub-etapas R1–R11; módulo nuevo `docs/GSM-modulo-equipment.md`, sub-etapas E1–E4; `docs/GSM-modulo-memberships.md`, Memberships v2, sub-etapas MS1–MS8 — la gracia pasa de Attendance a Memberships; `docs/GSM-modulo-attendance.md`, Attendance v2, sub-etapas A1–A7; `docs/GSM-modulo-gyms.md`, Gyms v2, sub-etapas G1–G6 — alta con invitación, onboarding obligatorio, varios Owners; `docs/GSM-modulo-payments.md`, Payments v2, sub-etapas P1–P3 — caja por turno con arqueo, el sistema solo registra cobros, no los recibe; `docs/GSM-modulo-identity.md`, Identity v2, sub-etapas I1–I7 — una cuenta por persona con acceso a varios gyms (`GymAccess`), perfiles de staff, login propio del miembro con código; `docs/GSM-modulo-classes.md`, sección "Replanteamiento v2", sub-etapas C1–C7 — instructores de la lista de entrenadores, lista de espera con confirmación, reglas de reserva; `docs/GSM-modulo-achievements.md`, Achievements v2, sub-etapas L1–L6 — logros de antigüedad, récords, referidos y manuales, premios opcionales, ranking voluntario, logros retroactivos; `docs/GSM-modulo-comunicaciones.md`, sección "Replanteamiento v2", sub-etapas CO1–CO7 — audiencias por segmento, WhatsApp por enlace, preferencias por categoría y canal; `docs/GSM-modulo-notifications.md`, módulo nuevo Notifications, sub-etapas N1–N5 — **primer proceso en segundo plano del sistema** (dispatcher en la API con candado de Postgres), push con FCM para Android y web, iOS después; absorbe la bandeja, la cola de correos y la campana del panel; `docs/GSM-modulo-superadmin.md`, sección "Replanteamiento v2", sub-etapas S1–S7 — planes con límite de miembros, prueba opcional, **un solo SuperAdmin con permiso total**, impersonación con escritura y en silencio, catálogos del sistema; **`docs/GSM-apps-y-vistas.md`** — el panel del gym pasa a una librería compartida `GSM.Panel.UI` y entra en la app nativa (staff y Owner en el celular), modo staff/miembro para quien tiene los dos roles en un gym, `/app` adaptable a escritorio, bloqueo con huella/PIN en modo staff; sub-etapas AP1–AP7, **AP1 va antes que el resto de la v2**; `docs/GSM-dashboards.md`, los tres dashboards; **`docs/GSM-logs.md`** — schema `logs` con una tabla por tipo de falla (errores, rechazos de negocio, eventos fallidos con reintento, tareas, servicios externos, cliente, seguridad, lentitud), panel y alertas para el SuperAdmin, retención de 1 año; **LG1 va al empezar el desarrollo**). **Reports y Locations quedan para más adelante** (decidido 2026-10-07). **Plan de construcción de la v2: `docs/V2-BUILD-PLAN.md`** — olas 0 a 6 para varios agentes Sonnet en paralelo con un integrador; empieza por X0 (una base de tests por carril, registro por módulo), luego AP1, LG1 y T1.. **Pendientes transversales** que salieron de la revisión y se diseñan aparte: (1) **notificaciones push** — diseñado como módulo nuevo en `docs/GSM-modulo-notifications.md`, (2) **permisos por cuenta de staff** en Identity — ya diseñados en `docs/GSM-modulo-identity.md` §5 (`ViewMemberHealth`, `Trainer`, `ManageAttendance`, `VoidPayments`, `ViewFinances`), (3) **rol `kiosk`** de dispositivo — diseñado en Identity §8, (4) **onboarding del gym** — ya diseñado en `docs/GSM-modulo-gyms.md` §5.. **Los bugs encontrados se documentan en `docs/BUGS.md` y no se corrigen durante la revisión**: se resuelven al empezar el desarrollo, cada uno con su test. Las plantillas de rutina quedan para el final. **Reports** queda definido como el módulo de descargas (Excel y similares); el progreso del miembro va en Routines.

**En construcción — v2 (rama `v2`, desde la noche del 2026-10-07).** Bitácora de la construcción: `docs/V2-NOCHE.md` (estado por tarea, preguntas abiertas, desvíos aceptados). **Ola 0 cerrada:** X0 (base de tests por carril con `GSM_TEST_DATABASE`, `scripts/new-lane.ps1`/`drop-lane.ps1`, DI por módulo, enums por módulo, SQL a mano en `Migrations/Scripts/`), AP1 (`GSM.Panel.UI`), LG1 (schema `logs`, `ILogStore`, `traceId`) y T1 (terminología). **Ola 1 cerrada:** I2 (una cuenta, varios gyms), M1 (ficha ampliada), R1–R2 (rutinas en el lugar, corrige B1; modo semanal), P1 (métodos de pago de Colombia), N1 (módulo Notifications con dispatcher), AP2 (panel adaptable), B3 (formato del precio), S5–S6 (SuperAdmin único; impersonación con escritura). **Olas 2 a 6 construidas y unidas (2026-10-16):** todas las sub-etapas del plan que se pueden escribir, incluidas AP3+N3 y AP5 (compilan para Android, nunca ejecutadas en dispositivo). Suite: **1224 integración + 693 bUnit, en verde** (2026-10-17). **Todas las preguntas de `V2-NOCHE.md` §2 están respondidas** y sus cambios hechos (tandas Q1–Q7). Faltan solo **V1** (recorrido de punta a punta) y **DP** (despliegue), que dependen de cuentas externas y de un Android: **qué falta y qué necesita al dueño está en `docs/V2-PENDIENTES.md`**. La base de desarrollo `gsm` está atrasada (`V2_P13`); `gsm_test` está al día.

### Notas de la v2 que afectan a todo

- **Agentes en paralelo: 3 o 4 como máximo, nunca 6–9** (decidido 2026-10-11). **Desde el 2026-10-12 solo escriben código: no compilan, no corren tests ni `dotnet ef`; el integrador genera migraciones, regenera el cliente y prueba al unir toda la tanda.** El límite de uso de la API es el cuello de botella real, cada agente arranca en frío, unir más ramas cuesta más y las preguntas al dueño se acumulan. Tareas que comparten terreno van en serie y cada una se une apenas termina. Detalle en `docs/V2-BUILD-PLAN.md` §3.
- **Tests por carril:** `ApiFactory` lee la base de `GSM_TEST_DATABASE` (por defecto `gsm_test`). Cada agente usa `gsm_test_<carril>`; solo el integrador corre contra `gsm_test`.
- **Registro por módulo:** lo de cada módulo se registra en `Application/{Módulo}/{Módulo}Module.cs` (`Add{Módulo}Module`) e `Infrastructure/{Módulo}/{Módulo}InfrastructureModule.cs` (`Add{Módulo}Infrastructure`); lo transversal en `AddCommonInfrastructure`. Los enums de Postgres de cada módulo se declaran **una sola vez** en `Infrastructure/{Módulo}/{Módulo}Enums.cs` (`IModuleEnums`), que alimenta modelo, proveedor y factory de diseño.
- **SQL a mano de una migración** va en `GSM.Infrastructure/Migrations/Scripts/V2_<id>.sql` (recurso embebido) y la migración lo ejecuta con `migrationBuilder.SqlScript("<id>")`. Si una migración se regenera al unir, se conserva esa llamada.
- **Panel del gym en `GSM.Panel.UI`** (RCL): layout y todas sus páginas. En `GSM.Web` quedan el host, el login y "olvidé mi contraseña", `/admin` y `/kiosk`. Portapapeles por `IClipboard` (`GSM.UI.Shared`), con implementación web y MAUI.
- **Logs (`GSM-logs.md`):** schema `logs` con su propio `LogsDbContext` (sin migraciones propias: el schema lo crea `V2_LG1.sql`; `logs.errors` está particionada por mes). `ILogStore` encola en memoria y escribe en segundo plano; nunca tumba la petición. Toda respuesta de error lleva `traceId` (y la cabecera `X-Trace-Id`); `ApiError.TraceCode` da el código corto que ve el usuario. Un manejador de evento que falla queda en `logs.event_handler_failures` con su payload para reintentarlo, así que **todo manejador nuevo debe ser idempotente**.
- **`.gitignore`:** la plantilla de Visual Studio ignora `[Ll]ogs/`; hay una excepción para `src/**` y `tests/**`. Si una carpeta de código "desaparece" al unir, revisa `git status --ignored`.
- **Ramas de tarea:** `v2-<id>-<nombre>` (no `v2/...`: Git no lo admite mientras exista la rama `v2`).
- **`scripts/generate-client.ps1`** funciona con Windows PowerShell 5.1 y con `pwsh`.
- **Cuentas (I2):** `AppUser` ya no lleva `GymId` ni `Role`; la persona tiene un `GymAccess` por gym (máximo uno de equipo y uno de miembro) y `IsSuperAdmin` (una sola cuenta, índice parcial, S5). El token sigue emitiéndose **para un gym** (`gym_id` + rol), así que el Global Query Filter y `ICurrentUserService` no cambian. `POST /api/auth/login` devuelve `LoginResponse` (`Tokens` o la lista de accesos para elegir); `select-gym` y `switch-gym` emiten tokens para otro acceso. En tests: sembrar `AppUser` sin `Role`/`GymId`, y leer `LoginResponse.Tokens`.
- **Permisos (I3):** claim `perm` con `view_member_health`, `trainer`, `manage_attendance`, `void_payments`, `view_finances`; `ICurrentUserService.HasPermission` y `[RequirePermission(...)]`. Owner y SuperAdmin impersonando cumplen todos. Quitar un permiso revoca los refresh tokens de ese acceso. **Aplicarlos a los endpoints es tarea de cada módulo** (M3, P2, A2, M1b).
- **Notifications (N1):** dueño de la bandeja del miembro, la cola de correos y la campana del staff (`StaffBell`). Comunicaciones y los demás módulos **publican eventos**; Notifications crea los avisos (idempotentes por `(Type, Recipient, ReferenceId)`). El `NotificationDispatcher` (`BackgroundService`, 30 s, `pg_try_advisory_lock`) envía los correos; en tests está apagado y se usa `NotificationDispatchRunner.RunOnceAsync`. Los enums viejos de Comunicaciones viven como `Legacy*` en `Migrations/LegacyCommunicationsEnums.cs`.
- **Estado comercial (MS1):** la gracia y "por vencer" viven en `memberships.membership_settings` (perezosa: 3 y 7 días por defecto); `attendance.checkin_settings` ya no tiene `grace_days`. Attendance decide con `IMembershipReader.GetAccessStatusAsync`; recepción fuera de la gracia exige "Dejar pasar" con motivo (`authorized_by`, `authorization_reason`).
- **Panel adaptable (AP2):** las entradas de menú y las pestañas de `/settings` se declaran por módulo en `GSM.Panel.UI/Navigation/Modules/*PanelModule.cs` (`IPanelModule`); no se editan `NavMenu` ni `Settings/Index`. `ServerTable` tiene `CardTemplate` para celular; corte en 960 px (`ViewportState`).
- **Al unir migraciones que chocan en el snapshot**, si la del agente tiene `Up` escrito a mano con un orden que importa, no se regenera: se regenera solo el snapshot desde el modelo unido y se reescribe el Designer de esa migración (ver `docs/V2-NOCHE.md` §4).
- **Cuarto defecto de NSwag (A1b):** un parámetro con valor por defecto `int` en un record de respuesta (`int X = 0`) sale en el cliente como `int? X = "0"` y no compila. En los DTOs de respuesta, sin valores por defecto en parámetros `int`.
- **Tareas periódicas en tests:** una tarea que recorre todos los gyms (anonimización de M6) con el reloj adelantado borra datos de otros tests que corren en paralelo. La tarea de anonimización y `NotificationDispatchRunner.RunOnceAsync` admiten `onlyGymId` solo para tests (el dispatcher, además, no toma el candado con él); cualquier tarea nueva del mismo tipo necesita lo mismo.
- **bUnit:** registrar todos los servicios antes de pedir cualquiera (también `ViewportState.Pin`); un parámetro `[SupplyParameterFromQuery]` se pasa navegando con `NavigationManager`, no con `p.Add`; MudBlazor puede poner `data-testid` en el `input` mismo.
- **Instantes en tests:** Npgsql solo escribe `DateTimeOffset` con offset 0; un helper que construye la hora local de Colombia debe terminar en `.ToUniversalTime()`.
- **`ReadResponseAsString.cs`:** todo cliente NSwag nuevo necesita su partial; el integrador lo comprueba después de cada regeneración (faltaban 12).
- **Ciclos entre módulos:** sigue habiendo uno solo (`Attendance ↔ Achievements`). Memberships no lee Payments: el crédito del cambio de plan y "pendiente de pago" llegan por su puerto `IMembershipPaidAmountProvider`, que implementa Payments en Infrastructure (MS6-1).
- **`GymExecutionScope`** (`Application/Common/Abstractions`): hace que `ICurrentUserService` informe "sistema dentro de un gym". Lo usa el reintento de eventos fallidos (LG4-4). Toda implementación nueva de `ICurrentUserService` (también los dobles de test) debe consultarlo.
- **El check-in es idempotente (A1b):** candado por miembro y un escaneo repetido en 2 minutos devuelve la misma entrada con 200. Un test que quiera probar "ya entró hoy" corre hacia atrás las entradas con `factory.AgeEntriesAsync(memberId)`.
- **Rutinas (R1):** `PUT /api/routines/{id}` edita en el lugar; días y líneas que no vienen se retiran (`retired_on`), nunca se borran. `routine_exercise_logs` tiene `exercise_id` y su FK a la línea es `Restrict`.

Siguen pendientes el despliegue (Dokploy, etapa 8 punto 4 del backend), y los módulos sin planificar (Reports, Locations).

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

Módulos sin planificar todavía (no implementar): Reports, Locations. **Community se sustituyó por Comunicaciones** (etapa 13): publica el gym y los miembros reaccionan; sin publicaciones ni comentarios de miembros.

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

Un schema de PostgreSQL por módulo, los once ya creados: `identity`, `gyms`, `members`, `memberships`, `payments`, `attendance`, `achievements`, `routines`, `classes`, `superadmin`, `communications`. Convenciones: `snake_case`, tablas en plural, PK siempre `id` (uuid), FK `{tabla_singular}_id`, índices `ix_{tabla}_{columna}`, constraints `fk_{tabla}_{tabla_referenciada}`, enums nativos de Postgres.

No hay FK entre schemas de módulos distintos: la referencia se valida en el caso de uso contra el `I{Módulo}Reader` correspondiente.

## Convenciones de código

- Inglés en código (clases, propiedades, métodos). Español permitido solo en contenido de datos.
- **Terminología: "miembro", nunca "socio"** (decidido 2026-10-07), en docs, textos de la UI, comentarios y respuestas. Los docs ya están corregidos; quedan textos de UI y comentarios del código, que se cambian al empezar el desarrollo.
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

No construyas Reports ni Locations todavía.

### Notas de Routines que afectan a módulos ya cerrados

- **`Exercise` no implementa `ITenantEntity`.** Es la primera entidad con `gym_id` nullable (`null` = catálogo del sistema). Su filtrado va explícito en `ExerciseRepository`, no en el Global Query Filter — esa pieza no se toca.
- **La racha pasa a medirse contra el calendario de la rutina** (el descanso planificado no la rompe). `StreakCalculator` acepta un calendario opcional y **sin él se comporta igual que hoy**; el cálculo lo compone un handler, porque Attendance (6) no puede depender de Routines (9).
- **El congelador de racha vive en `achievements`**, no en Routines: aplica también a miembros sin rutina.

### Notas de Classes que afectan a módulos ya cerrados

- **Las sesiones no se generan por adelantado: el horario es una proyección.** Una ocurrencia solo se materializa cuando hay algo que guardar (reserva, asistencia, cancelación puntual), y el índice único `(class_id, session_date)` la hace idempotente. Mismo patrón perezoso que el congelador de racha, y por el mismo motivo: **no hay scheduler en el sistema.**
- **Asistir a una clase registra el check-in del día por evento**, no por escritura cruzada: `ClassAttendanceRegistered` lo consume un handler de Attendance, que ya era idempotente por día desde la etapa 6.
- **`class_booking_status` persiste `no_show`** desde la migración `Etapa10_5_BookingStatusNoShow`; antes guardaba `noshow` por registrarse con `LowerCaseNameTranslator`. Un enum con valores de varias palabras va con `SnakeCaseNameTranslator`, nunca con guion bajo en el nombre del miembro C#: ese atajo (el de `Equipment`) saca `Smith_machine` en el JSON de la API.

### Notas de SuperAdmin (etapa 11)

- **El enum `GymPlan` ya no existe (S2-1, 2026-10-17):** manda `SaasPlanId`; las migraciones viejas lo nombran por `Migrations/LegacyGymPlan.cs`. **`Gym.Status` viene de la etapa 2 y SuperAdmin le dio sentido sin migrarlo:** `gym_status` significa *estado comercial manual* (`trial`/`active`/`suspended`) y los estados `overdue` y `disabled` se **derivan** de `paid_through`, nunca se persisten — mismo criterio que las membresías `expired` de la etapa 4.
- **`trial_ends_on` nulo no es una prueba indefinida.** Todo gym nace con `gym_status = trial`, así que si el nulo valiera como "trial para siempre" ningún cliente llegaría nunca a `overdue` ni a `disabled` y el bloqueo no serviría de nada. Sin fecha de fin, manda `paid_through`.
- **La suscripción se da de alta al crear el gym**, por un handler de `GymCreated` (30 días de prueba, precio 0). **Replanteamiento (2026-10-07): la prueba pasa a ser opcional**, con los días que elija el SuperAdmin en el alta; sin prueba, el alta registra el primer pago (`docs/GSM-modulo-superadmin.md` §V2). Si ese handler fallara, el gym queda sin suscripción y opera con normalidad; `PUT /api/admin/gyms/{id}/subscription` la repone, creándola si no existe.
- **El bloqueo por impago va en un filtro global de la API**, no repartido por casos de uso: es transversal como el multi-tenancy. Un gym **sin suscripción no se bloquea** — ante un dato faltante se deja pasar, porque dejar tirado a un cliente es peor error que no cortarle a un moroso.
- **Los pagos del SaaS no se mezclan con los del gym.** Se comparte el enum `payment_method` y nada más: son bolsillos distintos y no pueden acabar en el mismo cierre de caja.
- **Ni Gyms ni Identity dependen de SuperAdmin.** El bloque `subscription` (`SubscriptionInfo`, en `Application/Common`) lo componen los controllers, y el login consulta la suspensión por `IGymLoginGate`: un puerto que declara Identity e implementa SuperAdmin en Infrastructure. Así el único ciclo entre módulos es el deliberado `Attendance ↔ Achievements`.
- **La impersonación la impone el mismo filtro.** Desde S6 (v2) tiene **dos modos**, `read` (solo `GET`, como antes) y `write` (todo), con motivo obligatorio; claim `impersonation_mode`. Las escrituras del superadmin se firman como **"GSM"** (lo resuelve `IdentityReader`), cada petición queda en `superadmin.impersonation_actions` y el gym nunca se entera. Token de 15 minutos que conserva la identidad real del superadmin en `sub`. Para que pueda leer, `RequireStaff` admite a un superadmin cuyo token trae `impersonating=true` (el token lleva el rol real, y sin esa excepción recibía 403 en todo el gym); `RequireOwner` también lo admite desde S6.

### Notas de Comunicaciones (etapa 13)

- **Sustituye a "Community" con alcance reducido:** publica el gym, los miembros reaccionan (me gusta / celebrar / fuerza); sin publicaciones ni comentarios de miembros. El flag `ShowCommunity` se reutiliza como "mostrar comunicaciones"; `CommunityComments` sigue sin uso.
- **Existe la primera tarea periódica del sistema**, sin scheduler interno: `POST /internal/jobs/communications/run` (cabecera `X-Job-Secret` contra `Jobs:Secret`, fuera de OpenAPI) que debe invocar una tarea programada de Dokploy cada 5–10 min. Crea los recordatorios de vencimiento, vacía la cola de correos y purga avisos de más de 90 días. Es idempotente y tolera ejecuciones perdidas.
- **La tarea y la baja por enlace corren sin usuario**, así que el Global Query Filter está desactivado (`GymId` nulo) y fijan `gym_id` explícitamente en cada escritura. Son las únicas rutas, junto con SuperAdmin, que ven varios gyms.
- **Los correos nunca se envían dentro de la petición:** se encolan en `communications.email_outbox` (índice único por miembro + tipo + referencia) y los envía la tarea. Sin Resend configurado van al log.
- **Claves nuevas pendientes de configurar en producción:** `Communications:UnsubscribeSigningKey`, `Communications:PublicApiBaseUrl` y `Jobs:Secret` (la API no arranca sin las dos primeras).
- **Avisos por evento, sin escrituras cruzadas:** Communications consume `ClassSessionCancelled` y `AchievementUnlockedEvent`; para los textos lee `IClassReader.GetClassNameAsync` y `IAchievementReader.GetAchievementNameAsync` (añadidos, solo lectura), y `IMemberReader.GetContactAsync`/`GetEmailContactsAsync` para el email.

### Notas del check-in con QR del gym (etapa 12)

- **El miembro solo se registra con el código del QR de su gym** (`CheckInRequest(MemberId?, GymCode?)`). El código es un HMAC firmado con `Checkin:SigningKey`, recalculado con el `gymId` del JWT: uno de otro gym falla solo. Rotativo = ventana de 30 s (vale la actual y la anterior); fijo = versión regenerable. Solo vale el modo vigente del gym. Un código inválido es 422 y **no** cuenta como intento rechazado.
- **Regla de gracia** (`checkin_settings.grace_days`, por gym, 3 por defecto): membresía vencida hasta `EndDate + GraceDays` entra con `GraceDaysLeft`; pasada la gracia, o sin membresía, es 409 y queda un `CheckinAttempt` (se repite la fila si el miembro insiste el mismo día mientras siga sin verse). Quien ya entró hoy no vuelve a ser rechazado. El **staff** registra a mano siempre, sin gracia, con el aviso en la respuesta.
- **Los ajustes son perezosos:** sin fila en `attendance.checkin_settings` valen los de `CheckinSettings.Default` (rotativo, 3 días) y no se persisten hasta el primer `PUT`.
- **`CheckinQr` se aplica en el servidor** (403 al miembro y al kiosco si está apagado); el registro manual del staff sigue funcionando.
- **El QR personal del miembro ya no existe** (`members.qr_code` borrada; migración irreversible en datos).
- **El kiosco (`/kiosk`) usa una sesión de staff normal.** Una cuenta de "dispositivo" solo para el kiosco queda pendiente.
- **Tercer defecto de NSwag:** un enum suelto en el cuerpo de una respuesta (`CheckinMode`) sale como `int` aunque viaje como texto; por eso `Mode` es `string` en los DTOs de check-in.
- **Producción:** `Checkin:SigningKey` (≥ 32 caracteres) debe configurarse; la API no arranca sin ella.
- **Sin verificar en dispositivo:** el escáner de `GSM.Mobile` (`ZxingQrScanner`, ZXing.Net.Maui.Controls 0.10.4) compila para Android, pero no se ha probado con una cámara real. El build de Windows de `GSM.Mobile` ya fallaba en este entorno antes de la etapa 12 (NETSDK1047).
- **Pendientes, no construidos:** cuenta de dispositivo para el kiosco; avisos por email o push de los intentos rechazados (el evento `CheckInRejected` ya existe); App Links verificados en producción para que el enlace abra la app nativa; geocerca.
