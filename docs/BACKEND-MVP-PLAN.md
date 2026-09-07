# Plan de construcción — Backend MVP (GSM)

> Documento de trabajo para Claude Code. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`. **Si algo aquí contradice a esos dos, ellos mandan.**
>
> **Ritmo:** una etapa por vez. No empezar la siguiente sin confirmación explícita. Cada etapa termina compilando, con su migración aplicada y sus endpoints respondiendo.

---

## Convenciones transversales

- **IDs:** `Guid` v7 (`Guid.CreateVersion7()`), generados en el dominio, no en la DB.
- **DB:** un schema de Postgres por módulo. `snake_case`, tablas en plural, PK `id`, FK `{tabla_singular}_id`, índices `ix_{tabla}_{columna}`, constraints `fk_{tabla}_{referenciada}`, enums nativos de Postgres.
- **Fechas:** `DateTimeOffset` UTC en toda la aplicación; la conversión a `America/Bogota` es del cliente. Excepción: `start_date` / `end_date` de membresías son `DateOnly`.
- **Dinero:** `decimal(12,2)`, COP.
- **Errores:** las entidades validan invariantes en constructor/factory y lanzan `DomainException`. Jerarquía en `GSM.Domain/Common/Exceptions/`:
  - `DomainException` (abstracta) · `ValidationDomainException` → 422 · `UnauthorizedDomainException` → 401 · `NotFoundException` → 404 · `ConflictException` → 409 · `ForbiddenDomainException` → 403
  - **400 queda reservado para errores de forma del request** (payload malformado, validación de FluentValidation vía filtro de acción). La invariante de dominio violada es 422: request bien formado que el servidor entiende pero no puede procesar.
  - Middleware global mapea por tipo a `ProblemDetails`. Cualquier otra excepción → 500 genérico, detalle solo al log. No se usa `Result<T>`.
- **Casos de uso:** un archivo por caso de uso en `GSM.Application/{Módulo}/`. Clase con un único método `HandleAsync`, registrada en DI e inyectada directo en el controller. Sin bus de comandos.
- **Comunicación entre módulos:** lectura síncrona vía `Public/I{Módulo}Reader`; reacción asíncrona vía eventos in-process publicados desde `Events/` con el despachador propio (`IDomainEventPublisher`). **Nunca** referencia directa a entidad, DbContext o repositorio de otro módulo.
- **Sin FK entre schemas de módulos distintos.** La referencia se valida en el caso de uso contra el `Reader` correspondiente.
- **Mapeo:** Mapperly, un mapper parcial estático por módulo. Las consultas de lectura pesadas usan proyección explícita con `Select`, no mapeo post-materialización.
- **DTOs:** `record` inmutables. Nunca exponer entidades de Domain.
- **Validación:** FluentValidation sobre los requests, vía filtro de acción.
- **Endpoints:** Controllers (`[ApiController]`), uno por módulo, bajo el prefijo `/api`. `[Authorize(Policy = ...)]` a nivel de clase (se puede endurecer por acción). `/health` queda fuera del prefijo.
- **Enums en JSON:** `JsonStringEnumConverter` global — los enums viajan por nombre en requests y responses.
- **Migraciones:** una por etapa, `Etapa0X_{Módulo}`.
- **Logging:** Serilog structured, un log de entrada/salida por caso de uso, enriquecido con `gym_id` y `user_id`. Nunca passwords ni tokens.
- **Commits:** Conventional Commits.

> **Nota sobre endpoints:** los listados de cada etapa son una propuesta. `docs/GSM-especificacion-tecnica.md` define los 15 endpoints reales del MVP — verificar contra esa lista antes de implementar y recortar lo que sobre.

---

# ETAPA 0 — Fundaciones

**Objetivo:** la solución levanta, conecta a Postgres y responde health check.

1. Verificar scaffolding: referencias entre capas correctas, `Directory.Build.props` con `<Nullable>enable</Nullable>`, warnings como errores en Domain y Application.
2. Paquetes: `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `Riok.Mapperly`, `FluentValidation`, `Serilog.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Scalar.AspNetCore`.
3. `Domain/Common/`: `BaseEntity` (Id, CreatedAt, UpdatedAt), `ITenantEntity` (GymId), jerarquía de `DomainException`.
3b. **Despachador de eventos** en `Application/Common/Events/`: `IDomainEvent`, `IDomainEventHandler<TEvent>`, `IDomainEventPublisher` + implementación que resuelve los handlers del contenedor y los invoca. Registro por assembly scanning. La publicación va **después** del commit del caso de uso, nunca dentro; un handler que falla se loguea y no tumba la operación que publicó el evento. Son ~40 líneas: escribirlas aquí, en la etapa 0, para que las etapas 6 y 7 ya lo tengan.
4. `Infrastructure/Persistence/GsmDbContext`: snake_case, `ApplyConfigurationsFromAssembly`, schema por configuración de entidad, override de `SaveChangesAsync` para timestamps.
5. `ICurrentUserService` (UserId, GymId, Role, IsSuperAdmin) leyendo claims del `HttpContext`.
6. Global Query Filter genérico sobre `ITenantEntity`, desactivado si `Role == SuperAdmin`.
7. Middleware de excepciones → `ProblemDetails`.
8. Registro por assembly de los casos de uso, los validators y los event handlers.
9. `GET /health` y `GET /health/db`.
10. Docker Compose con Postgres 16 local.

**Aceptación:** `dotnet run` levanta, `/health/db` responde 200, Scalar renderiza.

---

# ETAPA 1 — Identity

**Schema:** `identity`

### Entidades
- `AppUser : IdentityUser<Guid>` — `GymId` (nullable para superadmin), `FullName`, `IsActive`, `Role`. **Vive en Infrastructure** (Domain no conoce Identity); Domain solo aporta el enum `UserRole`.
- `RefreshToken` — id, user_id, token_hash, expires_at, revoked_at. También en Infrastructure.
- Roles: enum nativo Postgres `identity.user_role` (`superadmin`, `owner`, `staff`, `member`) como **columna en `users`**, un rol por usuario. Sin tablas `AspNetRoles`/`AspNetUserRoles` (se usa `IdentityUserContext`). El claim `role` sale de esa columna.
- Password: mínimo 10 caracteres, sin símbolo obligatorio. Sin `AddDefaultTokenProviders` todavía (llega en la etapa 8 con Resend).

### Trabajo
1. Identity con `Guid` como key. Solo password; sin confirmación de email en MVP (dejar el hook).
2. `JwtTokenService`: access token 60 min con claims `sub`, `gym_id`, `role`, `name`. Refresh token opaco, hash en DB.
3. Policies: `RequireSuperAdmin`, `RequireOwner`, `RequireStaff` (owner ∪ staff), `RequireMember`.
4. Rate limiting en login.

### Public
`IIdentityReader`: `GetUserAsync(userId)` → id, nombre, rol, gymId. Es lo único que otros módulos pueden pedir de aquí.

### Endpoints propuestos
`POST /auth/login` · `POST /auth/refresh` · `POST /auth/logout` · `GET /auth/me`

**Aceptación:** login devuelve token con `gym_id` y `role`; endpoint protegido rechaza sin token.

---

# ETAPA 2 — Gyms

**Schema:** `gyms`

### Entidades
- `Gym` — id, name, slug (único), nit, phone, email, logo_url, primary_color, secondary_color, is_active, **plan** (enum `gyms.gym_plan` = basic/pro/enterprise), **status** (enum `gyms.gym_status` = trial/active/suspended, arranca en `trial`), **owner_id** (Guid, sin FK: el user vive en otro schema), created_at. Branding inline (sin `GymTheme` aparte). `Gym` implementa `ITenantEntity` con `GymId => Id`.
- `GymFeatureFlags` — **PK `id`** (convención §8.1) + `gym_id` con índice único (1:1 con `gym`), `show_routines`, `show_classes`, `show_progress`, `show_gamification`, `show_community`, `checkin_qr`, `community_comments`. Todos default `true`.

> Híbrido entre plan y spec: forma plana del plan + `plan`/`status` de la spec (para no migrar datos cuando llegue el módulo SaaS), sin entidad de tema.
> Los flags de módulos aún no construidos se crean igual: la app móvil los lee desde ya y así no hay migración de datos cuando lleguen.

### Global Query Filter — nota
El filtro dinámico debe leer la identidad a través de **miembros del `GsmDbContext`** (`TenantFilterDisabled`, `TenantFilterGymId`), no de una constante con el `ICurrentUserService` capturado: EF Core sustituye la constante-contexto por el contexto activo en cada consulta; una constante con el servicio se evalúa una sola vez y congela el primer gym que consulte.

### Trabajo
1. Alta de gym (solo superadmin): crea gym + owner inicial + feature flags por defecto, en una transacción.
2. Actualización de datos e identidad visual del gym.
3. Seed de desarrollo: 1 superadmin + 1 gym demo con owner y staff.

### Public
`IGymReader`: `ExistsAsync(gymId)`, `GetFeatureFlagsAsync(gymId)`, `GetBrandingAsync(gymId)`.

### Events
`GymCreated` — lo consumirá Achievements (etapa 7) para sembrar los logros por defecto.

### Endpoints
`POST /api/gyms` [superadmin] · `GET /api/gym` [staff] · `PUT /api/gym` [owner] · `GET /api/gym/feature-flags` [staff] · `PUT /api/gym/feature-flags` [owner] — todos operan sobre el gym del token (sin `{id}` en la ruta).
Además: `GET /api/auth/me` pasa a incluir `gym` (branding) y `featureFlags` cuando el usuario tiene gym.

### Prueba crítica de tenancy
Dos gyms con datos; autenticado como owner del gym A, ninguna consulta devuelve nada del gym B. **Test automatizado, no manual.** Se escribe aquí y se re-corre en cada etapa siguiente.

---

# ETAPA 3 — Members

**Schema:** `members`

### Entidad
`Member` — id, gym_id, user_id (nullable), first_name, last_name, document_number, phone, email, birth_date, photo_url, emergency_contact, status (`active|inactive`), qr_code, joined_at.

### Reglas
- `qr_code` único por gym, generado al crear. No es la PK.
- Un miembro puede existir sin `user_id`: se registra en recepción y luego canjea un código de invitación que crea su `AppUser`. El vínculo con Identity se coordina por evento, no por FK cruzada entre schemas.
- `document_number` único dentro del gym.
- Baja = soft delete a `inactive`.

### Public
`IMemberReader`: `GetAsync(memberId)`, `ExistsAsync(memberId)`, `ResolveByQrAsync(qrCode)`, `GetActiveCountAsync(gymId)`.

### Events
`MemberCreated`, `MemberDeactivated`.

### Endpoints propuestos
`GET /members` (paginado; filtros por nombre, documento, estado) · `GET /members/{id}` · `POST /members` · `PUT /members/{id}` · `DELETE /members/{id}` · `POST /members/{id}/invite` · `POST /members/register` (anónimo, canjea código)

---

# ETAPA 4 — Memberships

**Schema:** `memberships`

### Entidades
- `MembershipPlan` — id, gym_id, name, description, price, duration_days, is_active.
- `Membership` — id, gym_id, member_id, plan_id, status (`active|expired|paused`), start_date, end_date.

### Reglas
- Solo una membresía activa por miembro. Renovar = crear otra; si la vigente no ha vencido, la nueva arranca al día siguiente de su `end_date`.
- `expired` **no se persiste con un job**: se deriva de `end_date < today` en las consultas. Sin scheduler en el MVP.
- `member_id` se valida contra `IMemberReader`, no con FK al schema de otro módulo.

### Public
`IMembershipReader`: `GetActiveAsync(memberId)`, `IsActiveAsync(memberId)`, `GetExpiringAsync(gymId, days)`.

### Events
`MembershipCreated`, `MembershipRenewed`.

### Endpoints propuestos
CRUD `/membership-plans` · `POST /members/{id}/memberships` · `GET /members/{id}/memberships` · `GET /memberships/expiring?days=7`

**Aceptación:** renovación encadenada calcula fechas correctas; un miembro nunca queda con dos activas.

---

# ETAPA 5 — Payments

**Schema:** `payments`

### Entidad
`Payment` — id, gym_id, member_id (nullable), membership_id (nullable), amount, method (`cash|transfer|card|other`), concept, notes, registered_by, paid_at.

### Reglas
- Pagos 100% manuales. Sin pasarela.
- `member_id` nullable: hay ingresos sin miembro (producto, visitante).
- `registered_by` se toma **siempre** de `ICurrentUserService`, nunca del body.
- **Los pagos no se editan ni se borran.** Corrección = asiento de ajuste con monto negativo y concepto explícito. Es lo correcto para trazabilidad de caja.
- Registrar un pago con `membership_id` no crea la membresía: son operaciones separadas que la UI encadena.

### Public
`IPaymentReader`: `GetMonthlyRevenueAsync(gymId, month)`, `GetByMemberAsync(memberId)`.

### Endpoints propuestos
`POST /payments` · `GET /payments` (filtros: rango, método, miembro) · `GET /payments/{id}` · `GET /members/{id}/payments` · `GET /cash-register/daily?date=`

**Aceptación:** cierre de caja diario cuadra por método y por total.

---

# ETAPA 6 — Attendance

**Schema:** `attendance`

### Entidad
`Attendance` — id, gym_id, member_id, checked_in_at, method (`qr|manual`).

> Sin `location_id`: Locations es fase futura. Agregarlo después es una migración trivial.

### Reglas
- **Un check-in por miembro por día.** El segundo intento devuelve el registro existente, no un 409.
- Valida membresía vigente vía `IMembershipReader`. Si está vencida **no bloquea**: registra y devuelve `membership_expired: true` para que recepción cobre. Un gym real no rechaza a nadie en la puerta por software.
- **Racha:** días consecutivos con tolerancia de 1 día de descanso. `StreakService` la calcula desde las asistencias, sin tabla propia.
- Índice obligatorio: `ix_attendances_gym_id_checked_in_at`.

### Public
`IAttendanceReader`: `GetVisitCountAsync(memberId)`, `GetStreakAsync(memberId)`, `GetTodayCountAsync(gymId)`.

### Events
`CheckInRegistered` — lo consume Achievements.

### Endpoints propuestos
`POST /attendance/checkin` (por `qr_code` o `member_id`) · `GET /attendance` · `GET /members/{id}/attendance` · `GET /members/{id}/streak` · `GET /attendance/peak-hours?from=&to=`

**Aceptación:** duplicado diario idempotente; racha correcta en escenario con huecos.

---

# ETAPA 7 — Achievements

**Schema:** `achievements`

### Entidades
- `Achievement` — id, gym_id, name, description, icon_url, condition_type (`visits|streak`), condition_value, is_active.
- `MemberAchievement` — id, gym_id, member_id, achievement_id, unlocked_at.

> `condition_type` sin `classes`: el módulo Classes es fase futura. Se agrega al enum cuando exista.

### Reglas
- Handler de `CheckInRegistered` evalúa los logros pendientes de ese miembro leyendo `IAttendanceReader`. Idempotente: doble evaluación nunca duplica.
- Handler de `GymCreated` siembra los logros por defecto: primera visita, 10 visitas, 50 visitas, racha de 7 días, racha de 30 días.
- **Decisión pendiente:** el desbloqueo llega por evento, así que la respuesta del check-in puede no incluirlo. Dos opciones: (a) la app consulta `/me/achievements` después del check-in; (b) el despachador invoca los handlers de forma síncrona dentro del request y el caso de uso de check-in recoge los desbloqueos para devolverlos en la misma respuesta. (b) da mejor UX y con un despachador propio es fácil de implementar, pero acopla el timing de los dos módulos. **Resolver antes de implementar.**

### Endpoints propuestos
`GET /achievements` · `GET /members/{id}/achievements` (obtenidos + pendientes con progreso) · `GET /me/achievements`

---

# ETAPA 8 — Cierre e infraestructura

1. **Cloudflare R2:** `IFileStorage` en Application, implementación S3-compatible en Infrastructure. Logos de gym, fotos de miembros, íconos de logros. Validar MIME y tamaño (máx. 5 MB), renombrar con GUID.
2. **Resend:** `IEmailSender`. Plantillas: invitación a la app, recuperación de contraseña.
3. **Dashboard:** un solo endpoint, un solo response — miembros activos, check-ins de hoy, ingresos del mes, membresías vencidas y por vencer. Se arma componiendo los `Public/I{Módulo}Reader`, sin consultas cruzadas. Agregaciones con proyección explícita, nunca cargando entidades. Índices: `payments(gym_id, paid_at)`, `memberships(member_id, end_date)`.
4. **Deploy:** Railway. Dockerfile multi-stage. Migraciones automáticas solo en desarrollo; en producción, comando explícito.

> FCM/push queda fuera del MVP: sus disparadores naturales (logros, comunidad, vencimientos) o son de fase futura o se resuelven bien con pull desde la app.

---

## Fuera del MVP

Módulos: Routines, Classes, Community, Reports, Locations, SuperAdmin.
Features: pasarela de pagos, facturación electrónica, chat entre miembros, lista de espera, reportes exportables, impersonación, métricas SaaS.

## Punto de corte comercial

Etapas 0 → 6 son el backend mínimo vendible: gestionar miembros, cobrar y controlar asistencia. Si aparece un piloto real antes de terminar, se puede pausar ahí y empezar el panel Blazor sobre esos endpoints.
