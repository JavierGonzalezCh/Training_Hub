# GSM — Contexto del proyecto

GSM (Gym System Management) es el **codename técnico** de **TrainingHub**, un sistema de gestión de gimnasios B2B SaaS: **TrainingHub OS** es el panel del gym (y `/admin` del SuperAdmin) y **TrainingHub GO** es la app del miembro. El nombre comercial es la identidad visible (textos de UI, títulos, correos, manifiestos, íconos); en código, namespaces, proyectos y schemas de base de datos **usa siempre `GSM`**.

Este archivo es el resumen que debes tener presente siempre. Antes de generar entidades, casos de uso o endpoints de un módulo, lee su especificación.

## Documentos

| Documento | Qué es |
|---|---|
| `README.md` | Cómo levantar el entorno local (Postgres, migraciones, API, web, datos de demostración) |
| `docs/DESARROLLO.md` | Convenciones de trabajo: migraciones, cliente NSwag, tests, carriles y agentes |
| `docs/GSM-especificacion-tecnica.md` | Arquitectura: capas, multi-tenancy, catálogo de módulos, comunicación entre módulos |
| `docs/GSM-modulo-{identity,gyms,members,memberships,payments,attendance,achievements,routines,classes,superadmin,comunicaciones,equipment,notifications}.md` | Especificación vigente de cada módulo (diseño v2, construido) |
| `docs/GSM-logs.md`, `docs/GSM-dashboards.md`, `docs/GSM-apps-y-vistas.md` | Transversales: logs y errores, los tres dashboards, apps y vistas (panel, miembro, nativa) |
| `docs/V2-PENDIENTES.md` | **Qué falta** y qué necesita al dueño (cuentas externas, Android) |
| `docs/PENDIENTES-PRODUCCION.md`, `docs/FEEDBACK-PRODUCCION.md` | Cómo desplegar, pendientes de producción y retroalimentación del recorrido |
| `docs/DEPLOY-DOKPLOY-PLAN.md` | Plan con el que se montó el despliegue en Dokploy (**ya desplegado**) |
| `docs/GSM-app-miembro-v3.md` | App del miembro v3: Hoy, Plan, botón central al entrenamiento, modo sesión, mapa muscular, estilo propio (**construido**) |
| `docs/GSM-sesion-perdida.md` | Sesión perdida: hacer cualquier día del plan, recuperar la de ayer y reorganizar la semana (**construido**, SP1–SP4) |
| `docs/GSM-tipos-de-negocio.md` | Tipo de negocio del tenant (gym, box, estudio, entrenador personal, academia) y plantillas de configuración; cumplimiento de la rutina (**construido**, T1–T7; lo que quedó fuera está al final de §15) |
| `docs/BUGS.md`, `docs/V1-RECORRIDO.md` | Bugs conocidos y recorrido de punta a punta en navegador |

## Estado actual

Todos los módulos están construidos y en `main`: Identity, Gyms, Members, Memberships, Payments, Attendance, Achievements, Routines, Classes, SuperAdmin, Comunicaciones, Equipment y Notifications, más Logs y los dashboards. Suite: tests de integración contra Postgres real + **893 bUnit**.

**Producción:** servidor propio con Dokploy (`ssh iae-server`, compose `gsm-plataforma-gmrss0`: `api`, `web`, `cron`). **Cada push a `main` despliega solo**; las migraciones se aplican a mano antes (README, «Migraciones en producción»). Tras una migración que crea un enum de Postgres, comprobar que el contenedor de la API se recreó (`docker ps`); si no, reiniciarlo: Npgsql lee los tipos solo al arrancar y el enum nuevo da 500 hasta entonces. R2 está configurado; Resend está en modo de pruebas (falta dominio) y Firebase incompleto (no hay push).

Falta, y está en `docs/V2-PENDIENTES.md`: dominio de Resend, Firebase completo y **verificar `GSM.Mobile` en un Android real** (compila para Android, nunca se ha ejecutado en un dispositivo; iOS necesita host macOS). `docs/V1-RECORRIDO.md` lista lo que falta recorrer en el navegador.

**Sin planificar (no implementar):** Reports (módulo de descargas: Excel y similares) y Locations (multi-sede). Las plantillas de rutina existen; el progreso del miembro vive en Routines.

Trabajamos incremental: Domain → Infrastructure/DbContext+migración → Application → API. **No implementes más de una etapa (ni sub-etapa) sin confirmación.**

## Stack

.NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL 18 · JWT + ASP.NET Identity · FluentValidation · Mapperly · Serilog. Frontend: Blazor WASM + MudBlazor y MAUI Blazor Hybrid. Sin MediatR.

## Estructura de la solución

```
src/Backend/
  GSM.Domain/          → entidades, enums, invariantes. Cero dependencias externas.
  GSM.Application/     → casos de uso, DTOs, interfaces públicas por módulo, eventos.
  GSM.Infrastructure/  → EF Core, repositorios, clientes externos (R2, Resend, FCM).
  GSM.API/             → controllers, middleware, auth.
src/Frontend/          → GSM.Frontend.slnx (ver "Frontend")
```

## Arquitectura: monolito con disciplina de módulos

Un solo monolito (no microservicios ni .csproj por módulo). Dentro de cada capa, carpetas por módulo:

```
GSM.Application/{Módulo}/
  ├── {UseCase}.cs
  ├── Public/    ← interfaz de lectura que otros módulos pueden consumir (I{Módulo}Reader)
  └── Events/    ← eventos de dominio que este módulo publica
```

**Regla dura:** un módulo nunca referencia la entidad Domain, el DbContext o el repositorio concreto de otro. Solo consume su `Public/I{Módulo}Reader` (síncrono, solo lectura) o eventos (in-process). Ninguna escritura cruzada directa.

**Excepción — escritura cruzada vía `Public/`**, solo cuando la operación debe ser atómica o su resultado va en la misma respuesta. Hoy hay tres:

- `Identity.Public.IUserProvisioning` — alta de gym y canje de invitación crean el `AppUser` en la misma transacción.
- `Achievements.Public.IAchievementService` — el check-in evalúa los logros de forma síncrona para devolverlos en su respuesta.
- `Gyms.Public.IGymAdministration` — `suspend`/`reactivate` de SuperAdmin escriben `Gym.Status`.

**Antes de añadir una cuarta, agota el camino normal** (lecturas por reader, reacciones por evento): "es más cómodo" no basta. Contraejemplo a imitar: el alta de suscripción se resolvió con un handler de `GymCreated`. Otros puertos invertidos (los declara quien los usa, los implementa el dueño del dato): `IGymLoginGate` (Identity ← SuperAdmin), `IMembershipPaidAmountProvider` (Memberships ← Payments) e `IRemoteTrainingPolicy` (Routines ← Attendance: con entrenamiento a distancia el inventario no decide qué ejercicio está disponible). El único ciclo entre módulos es deliberado: `Attendance ↔ Achievements`.

**Casos de uso:** una clase por caso con un único `HandleAsync`, registrada en DI e inyectada en el controller. Sin bus de comandos.

**Eventos in-process** (`GSM.Application/Common/Events/`: `IDomainEvent`, `IDomainEventHandler<T>`, `IDomainEventPublisher`): handlers por assembly scanning; la publicación ocurre **después** del commit; un handler que falla se loguea (en `logs.event_handler_failures`, con su payload, para reintentarlo) y no tumba la operación, así que **todo handler nuevo debe ser idempotente**.

**Registro por módulo:** `Application/{Módulo}/{Módulo}Module.cs` (`Add{Módulo}Module`), `Infrastructure/{Módulo}/{Módulo}InfrastructureModule.cs` (`Add{Módulo}Infrastructure`) y los enums de Postgres del módulo, una sola vez, en `Infrastructure/{Módulo}/{Módulo}Enums.cs` (`IModuleEnums`). Lo transversal, en `AddCommonInfrastructure`.

Fuera de la cadena de módulos vive `GSM.Application/MemberStreak/`: el compositor de la racha (Attendance + Routines + congelador de Achievements) para `GET /api/members/{id}/streak` y `GET /api/me/streak`.

## Multi-tenancy

Toda tabla de negocio lleva `gym_id`. Global Query Filter en EF Core (`HasQueryFilter(e => e.GymId == _currentUserService.GymId)`), desactivado solo para la cuenta SuperAdmin y para las rutas del sistema sin usuario. `ICurrentUserService` lee `GymId`/`UserId`/`Role` del JWT; **toda implementación nueva (también los dobles de test) debe consultar `GymExecutionScope`**, que hace que informe "sistema dentro de un gym" (lo usa el reintento de eventos fallidos).

## Invariantes de negocio: fail-fast

Las entidades validan sus invariantes en el constructor/factory estático y lanzan `DomainException` (jerarquía en `GSM.Domain/Common/Exceptions/`): `ValidationDomainException` → 422, `NotFoundException` → 404, `ConflictException` → 409, `ForbiddenDomainException` → 403. Un middleware global lo mapea a `ProblemDetails`; cualquier otra excepción → 500 genérico con el detalle solo en el log. No se usa `Result<T>`.

**400 queda reservado para errores de forma del request** (payload malformado, FluentValidation vía `ValidationActionFilter`). Una invariante violada con un request bien formado es 422; la frontera con 409: `ValidationDomainException` es para valores que no satisfacen la regla, `ConflictException` para choques de estado (duplicados, carreras).

## Mapeo y base de datos

Mapperly (mapper parcial estático por módulo); las lecturas pesadas usan proyección con `Select`. Routines y Classes mapean a mano (`RoutineMapper`, `ClassMapper`): sus DTOs son árboles anidados, excepción aceptada.

Un schema de PostgreSQL por módulo: `identity`, `gyms`, `members`, `memberships`, `payments`, `attendance`, `achievements`, `routines`, `classes`, `superadmin`, `communications`, `equipment`, `notifications` y `logs`. `snake_case`, tablas en plural, PK `id` (uuid), FK `{tabla_singular}_id`, índices `ix_{tabla}_{columna}`, constraints `fk_{tabla}_{tabla_referenciada}`, enums nativos de Postgres. **Sin FK entre schemas** de módulos distintos: la referencia se valida en el caso de uso contra el reader. Un enum con valores de varias palabras va con `SnakeCaseNameTranslator`, nunca con guion bajo en el miembro de C#.

Migraciones, SQL a mano (`Migrations/Scripts/V2_<id>.sql`), cliente NSwag y tests: `docs/DESARROLLO.md`.

## Convenciones de código

- Inglés en código; español solo en contenido de datos. **Terminología: "miembro", nunca "socio".**
- PascalCase (clases/métodos), camelCase (variables), prefijo `I` en interfaces. IDs `Guid` v7 generados en el dominio.
- **Instantes** en `DateTimeOffset` UTC. **Días de negocio** en `DateOnly` desde `IClock.Today` (zona del gym), **nunca** de `DateTime.UtcNow`: Colombia es UTC-5. Toda lectura de tiempo pasa por `IClock` (`UtcNow`/`Today`/`BusinessDayRange`/`ToBusinessLocal`). **Agrupar por día o mes usa `BusinessDayRange`**, no un rango UTC (defecto que ya apareció dos veces).
- Dinero: `decimal(12,2)`, COP.
- Serilog estructurado, un log de entrada/salida por caso de uso; nunca passwords ni tokens.
- Conventional Commits.
- Los tests no modifican datos sembrados por migraciones; para probar con la identidad real por HTTP, patrón `SubscriptionGateHttpTests`.

## Frontend (`src/Frontend/GSM.Frontend.slnx`)

| Proyecto | Qué tiene |
|---|---|
| `GSM.Client` | Cliente NSwag generado (`scripts/generate-client.ps1`), `AuthenticatedHttpHandler`, `ApiError`, `ITokenStore` |
| `GSM.UI.Shared` | Tema (`GsmTheme`, Forja: rojo `#B50016` sobre negro), sesión (`SessionService`, `GymContext`), `GsmLoader` (la barra del logo como indicador de carga), `ServerTable<T>`, `StatusChip`, `QrCodeSvg`, `IClipboard`, marca en `wwwroot/brand/` |
| `GSM.Panel.UI` | El panel del gym entero (RCL): layout y todas sus páginas; menú y pestañas de `/settings` declarados por módulo en `Navigation/Modules/*PanelModule.cs` (`IPanelModule`); `ServerTable` con `CardTemplate` para celular, corte en 960 px (`ViewportState`) |
| `GSM.Member.UI` | Las pantallas del miembro (RCL), adaptables a celular y escritorio. Barra flotante: Hoy (`/app`), Plan (`/app/plan`: planificador de `GET /api/me/routines/week` y días de la rutina), botón central (barra de press) al entrenamiento de hoy (`/app/rutina`, con modo sesión: un ejercicio por pantalla, guardado por serie), Progreso (`/app/progreso`: constancia, fuerza, logros y ranking) y Gym (`/app/gimnasio`, que reúne clases y novedades); el QR de la entrada va en el encabezado (`CheckinButton`). Estilo propio (redondeado y vidrio) en `gsm-member-v3.css`; `MuscleMap` dibuja músculos y recuperación (`GET /api/me/body`); fotos de muestra en `PlaceholderMedia` mientras no haya reales |
| `GSM.Web` | Host Blazor WASM (puertos `7220`/`5220`): login, "olvidé mi contraseña", `/admin` (SuperAdmin), `/kiosk`; sirve `/app` como PWA (manifiestos de OS y de GO) |
| `GSM.Mobile` | MAUI Blazor Hybrid (Android y Windows) con el panel y la app del miembro; **vive fuera del `.slnx`**: `dotnet build src/Frontend/GSM.Mobile -f net10.0-android -p:RuntimeIdentifier=android-arm64` |

Cuando una persona es staff y miembro en un gym, cambia entre "Modo staff" y "Modo miembro"; el modo staff en la app pide huella, rostro o PIN tras 15 minutos. La impersonación del SuperAdmin guarda sus tokens en un segundo slot de `ITokenStore` y entra al panel con `GsmPolicies.RequireStaff`; terminarla usa `forceLoad: true`.

## Notas por módulo que afectan a los demás

- **Tipo de negocio (T1–T7, `docs/GSM-tipos-de-negocio.md`):** `gyms.gyms.type` (`GymType`: gym, box, studio, personal_trainer, academy, other; por defecto `gym`) es una **plantilla de arranque y una etiqueta, no un modo**: ningún caso de uso, componente ni filtro hace `if (tipo == …)`; se pregunta por la opción concreta. Al crear el gym, cada módulo aplica su parte reaccionando a `GymCreated` (`GymFeatureFlags.ForType` en el alta; `GymCreatedAppliesCheckin/Membership/RoutineTemplateHandler`; `SeedDefaultAchievementsHandler` con `AchievementTemplates`), solo si no hay fila de ajustes (idempotente) y siempre escribiendo la fila. Cambiar el tipo (Owner en Datos del gym, SuperAdmin en su ficha) **no reaplica nada**; «Ver la configuración recomendada» (`GET /api/gym/recommended-configuration`) compara y guarda lo marcado por los endpoints normales de cada módulo. Opciones nuevas: `RemoteTraining` y `TrainingEntryTrigger` (`start`/`first_log`) en `attendance.checkin_settings`, y `ShowFrontDesk` en `gyms.gym_feature_flags` (solo interfaz, no bloquea la API). Los pasos obligatorios del onboarding salen de las opciones, no del tipo. `BusinessNoun` nombra el negocio en los textos del panel (solo el onboarding por ahora). El tipo se ve en la cartera, la ficha, las métricas por tipo de `/admin` y la barra de impersonación.
- **Cumplimiento de la rutina (T6):** se calcula al leer, sin tabla (`Routines/Adherence/AdherenceCalculator`, función pura, y `IAdherenceSource`). **La regla de qué toca cada fecha es una sola: `RoutineCalendar`** (la usan la racha y el cumplimiento; no la reimplementes). Un día cuenta como hecho solo con registros de sus propias líneas, salvo que el miembro lo haya cambiado (SP, `docs/GSM-sesion-perdida.md`): `routines.routine_day_overrides` dice qué día cuenta en una fecha y `RoutineCalendar.Resolve` lo aplica; la sesión perdida de ayer, el cambio y la reorganización de la semana salen de `MissedSessionPlanner` (función pura). Endpoints: `GET /api/routines/adherence` (permiso `trainer`), `GET /api/members/{id}/adherence` y `GET /api/me/adherence`.
- **Identity (I2/I3):** `AppUser` no lleva `GymId` ni `Role`; la persona tiene un `GymAccess` por gym (máximo uno de equipo y uno de miembro) e `IsSuperAdmin` (una sola cuenta). El token se emite **para un gym** (`gym_id` + rol), así que el filtro global y `ICurrentUserService` no cambian. `POST /api/auth/login` devuelve `LoginResponse` (`Tokens` o la lista de accesos); `select-gym` y `switch-gym` emiten tokens para otro acceso. Permisos de staff por claim `perm` (`view_member_health`, `trainer`, `manage_attendance`, `void_payments`, `view_finances`) con `[RequirePermission(...)]`; Owner y SuperAdmin impersonando cumplen todos; quitar un permiso revoca los refresh tokens de ese acceso. En tests: sembrar `AppUser` sin `Role`/`GymId`. La cuenta de staff nace **sin contraseña** y recibe por email un código de restablecimiento (24 h); desactivar no borra y revoca los refresh tokens. El kiosco es una cuenta de dispositivo (rol `kiosk`) emparejada con un código de 8 caracteres. **PW1:** sin correo, el SuperAdmin fija la contraseña de cualquier cuenta de un gym desde su ficha (sección Cuentas, `PUT /api/admin/gyms/{gymId}/accounts/{userId}/password`): cierra sus sesiones y queda en `staff_access_audits` como `password_set_by_admin`; la suya la cambia en `/admin/cuenta/contrasena`. **FL1:** se entra con el correo o con el celular (10 dígitos, sin +57; solo celulares colombianos). Un miembro sin cuenta entra la primera vez con su número de documento: se le crea la cuenta sin contraseña (`MustChangePassword`, puerto `IMemberLoginDirectory` que implementa Members) y el token lleva `pwd_change`; `PasswordChangeGateFilter` solo deja `[AllowWhilePasswordChangeRequired]` (crear la contraseña en `POST /api/me/initial-password`, `/api/auth/me` y salir) y la app lo lleva a `/app/crear-contrasena`. Un celular en varias cuentas pide el correo.
- **Notifications (N1):** dueño de la bandeja del miembro, la cola de correos y la campana del staff. Los demás módulos publican eventos y Notifications crea los avisos (idempotentes por `(Type, Recipient, ReferenceId)`). Es el **primer proceso en segundo plano**: `NotificationDispatcher` (`BackgroundService`, 30 s, `pg_try_advisory_lock`); en tests está apagado y se usa `NotificationDispatchRunner.RunOnceAsync`. Push con FCM (Android y web). Los correos nunca se envían dentro de la petición.
- **Tarea periódica** `POST /internal/jobs/communications/run` (cabecera `X-Job-Secret` contra `Jobs:Secret`, fuera de OpenAPI), cada 5–10 min desde Dokploy: recordatorios de vencimiento, cola de correos, avisos de suscripción, anonimización y alertas. Corre sin usuario (filtro global desactivado, fija `gym_id` en cada escritura). Una tarea que recorre todos los gyms necesita `onlyGymId` solo para tests.
- **Memberships (MS1):** la gracia y "por vencer" viven en `memberships.membership_settings` (perezosa: 3 y 7 días). Attendance decide con `IMembershipReader.GetAccessStatusAsync`; fuera de la gracia, recepción exige "Dejar pasar" con motivo. `GetLatestEndDateAsync` solo mira membresías `Active`. Una membresía se puede cancelar (`cancelled`, con motivo) y congelar/reanudar (`paused`; al reanudar se corren su fin y el de las encadenadas). Memberships no lee Payments: el crédito del cambio de plan y "pendiente de pago" llegan por `IMembershipPaidAmountProvider`.
- **Payments:** anular un pago crea un asiento inverso fechado hoy (`reverses_payment_id`, índice único; una anulación no se anula) y el original no cambia; `PaymentResponse.IsReversed` lo resuelven los casos de uso de lectura con `GetReversedIdsAsync`. Caja por turno con arqueo. **Los pagos del SaaS (SuperAdmin) no se mezclan con los del gym**: solo comparten el enum `payment_method`.
- **Attendance (check-in con QR del gym):** el miembro solo se registra con el código del QR de su gym (`CheckInRequest(MemberId?, GymCode?)`), un HMAC firmado con `Checkin:SigningKey` recalculado con el `gymId` del JWT (rotativo cada 30 s o fijo y regenerable; solo vale el modo vigente). Código inválido = 422 y no cuenta como rechazo. El check-in es **idempotente** (candado por miembro; un escaneo repetido en 2 minutos devuelve la misma entrada con 200). Los ajustes son perezosos (sin fila valen `CheckinSettings.Default`). `CheckinQr` se aplica en el servidor (403 si está apagado). El QR personal del miembro ya no existe. Quien asiste a una clase registra el check-in del día por el evento `ClassAttendanceRegistered`. Sin registro en la puerta (QR y documento apagados) el día cuenta por entrenamiento (`routine`, A8): al iniciar la rutina, o con `first_log` al registrar un ejercicio (`RoutineExerciseLogged` → `RoutineExerciseLoggedCountsTheDayHandler`, que reintenta con cada ejercicio hasta que el día cuenta). Con `RemoteTraining` ese canal ignora el horario y sus entradas no cuentan en aforo ni horas pico.
- **Sesión (10 nov 2026):** access token de 60 min y refresh token de 90 días que rota en cada renovación. El cliente solo cierra la sesión si el servidor rechaza la renovación (401/403); sin red, con 5xx o 429 conserva los tokens (`RefreshUnavailableException`). La renovación tiene su propio límite por IP (`auth-refresh`, 120/min), aparte del login. El modo sesión del entrenamiento retiene el cierre (`SessionEvents.Hold`) y guarda en el equipo las series que no se pudieron enviar (`gsm.pending-logs`) para reenviarlas.
- **Routines:** `Exercise` no implementa `ITenantEntity` (primera entidad con `gym_id` nullable: `null` = catálogo del sistema; el filtrado va explícito en `ExerciseRepository`). `PUT /api/routines/{id}` edita en el lugar: días y líneas que no vienen se retiran (`retired_on`), nunca se borran; `routine_exercise_logs` tiene `exercise_id` y su FK a la línea es `Restrict`. La racha se mide contra el calendario de la rutina (el descanso planificado no la rompe); `StreakCalculator` sin calendario se comporta como antes. El congelador de racha vive en `achievements`, no en Routines. **Plantillas base (RT1–RT2):** cinco rutinas (`RoutineBaseTemplates`) que cada gym recibe como copias editables al crearse (`GymCreated`, idempotente por `BaseKey`); avisan las máquinas que el gym no tiene (`UnavailableHere`) y su portada sale de una galería fija (`RoutineImageGallery`, otra URL = 422). El miembro edita su rutina y también la del entrenador (queda por revisar con `review_reason = edited_by_member`), pero solo elimina la propia; las notas de la rutina son indicaciones que el miembro ve arriba.
- **Classes:** las sesiones no se generan por adelantado: el horario es una proyección y una ocurrencia solo se materializa cuando hay algo que guardar (reserva, asistencia, cancelación puntual); el índice único `(class_id, session_date)` la hace idempotente. **No hay scheduler de sesiones.** `class_booking_status` persiste `no_show`.
- **SuperAdmin:** el estado comercial del gym (`gym_status`: `trial`/`active`/`suspended`) es manual; `overdue` y `disabled` se **derivan** de `paid_through`, nunca se persisten. `trial_ends_on` nulo no es una prueba indefinida (sin fecha manda `paid_through`). La suscripción se da de alta por un handler de `GymCreated` (prueba opcional, con los días que elija el SuperAdmin; sin prueba, el alta registra el primer pago); si el handler falla, el gym opera con normalidad y `PUT /api/admin/gyms/{id}/subscription` la repone. **El bloqueo por impago va en un filtro global de la API**; un gym sin suscripción no se bloquea. Hay un solo SuperAdmin con permiso total; la impersonación tiene dos modos (`read`: solo `GET`; `write`: todo), motivo obligatorio, claim `impersonation_mode`, token de 15 minutos que conserva la identidad real en `sub`, las escrituras se firman con el nombre de la plataforma ("TrainingHub") y cada petición queda en `superadmin.impersonation_actions`. Ni Gyms ni Identity dependen de SuperAdmin (el bloque `subscription` lo componen los controllers; el login consulta por `IGymLoginGate`). Una impersonación sin cerrar se muestra como terminada al vencer su token (derivado, no persistido).
- **Comunicaciones:** el gym publica y los miembros reaccionan (me gusta / celebrar / fuerza); no hay publicaciones ni comentarios de miembros. Audiencias por segmento, WhatsApp por enlace, preferencias por categoría y canal. Claves a configurar: `Communications:UnsubscribeSigningKey`, `Communications:PublicApiBaseUrl` y `Jobs:Secret` (la API no arranca sin las dos primeras); además `Checkin:SigningKey` (≥ 32 caracteres) y, en producción, `Identity:PublicWebBaseUrl`, `Notifications:Fcm:*` y las de R2 y Resend.
- **Logs:** schema `logs` con su propio `LogsDbContext` (sin migraciones de EF: lo crea `V2_LG1.sql`; `logs.errors` está particionada por mes; retención de 1 año). `ILogStore` encola en memoria y escribe en segundo plano; nunca tumba la petición. Toda respuesta de error lleva `traceId` (y la cabecera `X-Trace-Id`); `ApiError.TraceCode` da el código corto que ve el usuario.
- **Logros (AC1):** el catálogo de un gym nuevo son cadenas por nivel con medalla (`AchievementTemplates`, imágenes en `GSM.UI.Shared/wwwroot/achievements`). Condiciones nuevas: `EarlyVisits` (antes de las 8:00) y `LateVisits` (desde las 20:00) en hora del gym, `Weekends` (fines de semana distintos), `CompleteSessions` (todas las líneas vigentes de un día registradas, `CompleteSessionCalculator`), `DistinctClasses`, `PowerLifts` (marca en los ejercicios marcados con `Exercise.PowerLift`: sentadilla, banca, peso muerto) y `MonthlyRankingTop` (puesto ≤ valor en un mes cerrado). El cierre de mes del ranking (`CloseRankingMonthHandler`, en la tarea periódica) guarda `achievements.ranking_month_results` una vez por gym y mes y reevalúa los logros. Las métricas nuevas van en `AchievementExtraMetrics` y solo se calculan si un logro pendiente las pide.
- **Members:** la anonimización la hace una tarea periódica con plazo por gym (24 meses por defecto desde la baja); `POST /api/members/{id}/anonymize` devuelve el `MemberDetail` completo (membresía y pagos incluidos).
- **Configuración perezosa de módulos** (`CheckinSettings`, `CommunicationsSettings`, `MembershipSettings`, `RoutineSettings`): sin fila valen los valores por defecto y no se persisten hasta el primer `PUT`.
