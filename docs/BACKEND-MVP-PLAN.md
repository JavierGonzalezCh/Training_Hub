# Plan de construcción — Backend MVP (GSM)

> Documento de trabajo para Claude Code. Cada etapa es un bloque cerrado: se implementa, se compila, se prueba y se commitea antes de pasar a la siguiente. **No avanzar con una etapa si la anterior no compila y sus endpoints no responden.**

## Contexto fijo

- **Stack:** .NET 10, EF Core 10, PostgreSQL 16, ASP.NET Identity + JWT, FluentValidation.
- **Namespace raíz:** `GSM`. La marca comercial no aparece en código, solo en datos de tenant y assets.
- **Capas:** `GSM.Domain` → `GSM.Application` → `GSM.Infrastructure` → `GSM.API`. Las dependencias apuntan hacia adentro. Domain no referencia nada.
- **Multi-tenancy:** `gym_id` en toda tabla de negocio + Global Query Filter en EF Core.
- **Pagos:** 100% manuales en v1. Sin pasarela.
- **Roles:** `superadmin`, `owner`, `staff`, `member`.

## Convenciones transversales (aplican a todas las etapas)

- **IDs:** `Guid` (v7 si es posible, `Guid.CreateVersion7()`) generados en el dominio, no en la DB.
- **Naming en DB:** `snake_case` para tablas y columnas; en C# `PascalCase`. Configurar mapeo con `UseSnakeCaseNamingConvention()`.
- **Fechas:** `DateTimeOffset` en UTC en toda la aplicación. La conversión a `America/Bogota` es responsabilidad del cliente. Excepción: `start_date` / `end_date` de membresías son `DateOnly`.
- **Dinero:** `decimal(12,2)`, COP, sin decimales en la UI.
- **Application:** patrón Command/Query con handlers propios (sin MediatR). Cada caso de uso es una clase con un método `Handle`. Los eventos de dominio in-process se despachan con un despachador mínimo propio cuando haga falta.
- **DTOs:** `record` inmutables. Nunca exponer entidades de Domain en la API. Mapeo entidad ↔ DTO con **Mapperly** (source generator).
- **Errores:** las entidades y servicios de dominio lanzan `DomainException` (jerarquía tipada en `GSM.Domain/Common/Exceptions/`); un middleware global la traduce a `ProblemDetails` con el código HTTP correspondiente. No se usa `Result<T>`.
  - `DomainException` (base, abstracta) · `ValidationDomainException` → 422 · `NotFoundException` → 404 · `ConflictException` → 409 · `ForbiddenDomainException` → 403.
  - El middleware mapea por tipo. Cualquier otra excepción → 500 con mensaje genérico; el detalle solo va al log.
- **Endpoints:** Controllers clásicos (`[ApiController]`) agrupados por módulo en `Controllers/`. Validación con FluentValidation.
- **Validación:** FluentValidation sobre los Commands, ejecutada en un filtro de acción / `ValidationFilter`.
- **Migraciones:** una migración por etapa, nombrada `Etapa0X_Descripcion`.
- **Commits:** convencionales (`feat:`, `fix:`, `chore:`), uno por sub-bloque funcional.

---

# ETAPA 0 — Fundaciones

**Objetivo:** que la solución levante, conecte a Postgres y responda un health check autenticado.

### Tareas

1. Verificar el scaffolding: referencias entre proyectos correctas, `Directory.Build.props` con `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>` en Domain y Application.
2. Paquetes:
   - API: `Microsoft.AspNetCore.Authentication.JwtBearer`, `FluentValidation.AspNetCore`, `Swashbuckle` o `Scalar.AspNetCore`.
   - Infrastructure: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `EFCore.NamingConventions`.
3. `Domain/Common/`:
   - `BaseEntity` (Id, CreatedAt, UpdatedAt).
   - `ITenantEntity` con `GymId` — la interfaz que activa el filtro global.
4. `Infrastructure/Persistence/GsmDbContext` vacío pero configurado: snake_case, `ApplyConfigurationsFromAssembly`, override de `SaveChangesAsync` que setea timestamps.
5. `ICurrentUser` en Application (`UserId`, `GymId`, `Role`, `IsSuperAdmin`) + implementación en API leyendo claims del `HttpContext`.
6. Global Query Filter genérico: en `OnModelCreating`, para cada entidad que implemente `ITenantEntity`, filtrar por `GymId == currentUser.GymId` salvo si `IsSuperAdmin`.
7. Configuración: `appsettings.json` + `secrets.json` local. Connection string, JWT (issuer, audience, key, expiración).
8. Pipeline en `Program.cs`: ProblemDetails middleware, CORS (orígenes de Blazor y MAUI), auth, authorization, OpenAPI.
9. Endpoint `GET /health` (anónimo) y `GET /health/db` (verifica `CanConnectAsync`).
10. Docker Compose local con Postgres 16 para desarrollo.

### Criterio de aceptación
`dotnet run` levanta, `/health/db` devuelve 200, Scalar/Swagger renderiza.

---

# ETAPA 1 — Identidad, tenancy y autenticación

**Objetivo:** un usuario puede autenticarse y su JWT lleva `gym_id` y `role`.

### Entidades
- `Gym` (id, name, slug, nit, phone, email, logo_url, primary_color, secondary_color, is_active, created_at)
- `Location` (id, gym_id, name, address, phone) — multi-sede desde el día 1 aunque la UI del MVP asuma una.
- `AppUser : IdentityUser<Guid>` con `GymId` (nullable para superadmin), `FullName`, `IsActive`.
- `GymFeatureFlags` (gym_id PK, show_routines, show_classes, show_progress, show_gamification, show_community, checkin_qr, community_comments — todos default `true`).

### Tareas
1. Configurar ASP.NET Identity con `Guid` como key. Solo password, sin confirmación de email en MVP (dejar el hook listo).
2. Seed de roles: `superadmin`, `owner`, `staff`, `member`.
3. `JwtTokenService`: genera access token (60 min) con claims `sub`, `gym_id`, `role`, `name`. Refresh token opaco persistido en tabla `refresh_tokens` (id, user_id, token_hash, expires_at, revoked_at).
4. Endpoints:
   - `POST /auth/login`
   - `POST /auth/refresh`
   - `POST /auth/logout` (revoca refresh token)
   - `GET /auth/me` → perfil + rol + gym + feature flags
5. Policies de autorización: `RequireOwner`, `RequireStaff` (owner o staff), `RequireMember`, `RequireSuperAdmin`.
6. `POST /gyms` (solo superadmin): crea gym + owner inicial + feature flags por defecto + sede principal, todo en una transacción.
7. Seed de desarrollo: 1 superadmin, 1 gym demo con owner y staff.

### Criterio de aceptación
Login devuelve token válido; un endpoint protegido rechaza sin token; el claim `gym_id` aparece en el token.

### Prueba crítica de tenancy
Crear dos gyms con datos, autenticarse como owner del gym A y verificar que una consulta directa no devuelve nada del gym B. **Esta prueba debe existir como test automatizado, no manual.**

---

# ETAPA 2 — Miembros y membresías

**Objetivo:** el núcleo del negocio: quién es miembro y hasta cuándo está vigente.

### Entidades
- `Member` (id, gym_id, user_id nullable, first_name, last_name, document_number, phone, email, birth_date, photo_url, emergency_contact, status enum `active|inactive`, qr_code, joined_at)
- `MembershipPlan` (id, gym_id, name, description, price, duration_days, is_active)
- `Membership` (id, member_id, plan_id, status enum `active|expired|paused`, start_date, end_date)

### Reglas de negocio
- `qr_code`: string único e irrepetible por miembro, generado al crear. No es la PK.
- Un miembro puede tener varias membresías históricas pero **solo una activa**. Renovar = crear una nueva; si la anterior aún no vence, la nueva arranca al día siguiente del `end_date` vigente.
- El estado `expired` no se persiste con un job: se calcula por `end_date < today` en las queries y se materializa solo cuando se consulta. Evita depender de un scheduler en el MVP.
- Un miembro puede existir sin `user_id` (registrado en recepción, todavía sin app). El vínculo se crea después con un código de invitación.

### Endpoints
- `GET /members` (paginado, filtros: búsqueda por nombre/documento, estado, vigencia)
- `GET /members/{id}` (perfil con membresía vigente, últimos pagos, últimas asistencias)
- `POST /members`, `PUT /members/{id}`, `DELETE /members/{id}` (soft delete → `inactive`)
- `POST /members/{id}/invite` → genera código para que el miembro se registre en la app
- `POST /members/register` (anónimo) → el miembro canjea el código y crea su `AppUser`
- CRUD de `/membership-plans`
- `POST /members/{id}/memberships` → asignar/renovar plan
- `GET /members/expiring?days=7` → próximos a vencer

### Criterio de aceptación
Alta de miembro, asignación de plan, renovación correcta de fechas y listado paginado funcionando.

---

# ETAPA 3 — Pagos y caja

**Objetivo:** trazabilidad completa del dinero, con registro manual.

### Entidad
- `Payment` (id, gym_id, member_id nullable, membership_id nullable, amount, method enum `cash|transfer|card|other`, concept, notes, registered_by FK→users, paid_at)

### Reglas
- `member_id` nullable porque puede haber ingresos sin miembro (venta de producto, clase suelta a un visitante).
- `registered_by` se toma **siempre del `ICurrentUser`**, nunca del body. Es el campo de auditoría.
- Al registrar un pago con `membership_id`, la membresía asociada queda marcada como pagada. Un pago no crea membresías por sí solo: son operaciones separadas que la UI encadena.
- Los pagos **no se editan ni se borran**. Corrección = registro de ajuste con monto negativo y concepto explícito.

### Endpoints
- `POST /payments`, `GET /payments` (filtros: rango de fechas, método, miembro)
- `GET /payments/{id}`
- `GET /members/{id}/payments`
- `GET /cash-register/daily?date=` → cierre de caja: total del día desglosado por método, cantidad de transacciones, lista completa

### Criterio de aceptación
Registrar pagos de distintos métodos y obtener un cierre de caja cuadrado.

---

# ETAPA 4 — Asistencia y check-in

**Objetivo:** registrar entradas y calcular rachas.

### Entidad
- `Attendance` (id, gym_id, member_id, location_id, checked_in_at, method enum `qr|manual`)

### Reglas
- **Un check-in por miembro por día.** Un segundo intento el mismo día devuelve el registro existente, no error.
- El check-in valida membresía vigente. Si está vencida: se permite el registro pero la respuesta incluye una bandera `membership_expired: true` para que recepción cobre. No bloquear — un gym real no rechaza a alguien en la puerta por software.
- **Racha (streak):** días consecutivos con asistencia, con tolerancia configurable de 1 día de descanso. Calcular en un `StreakService` a partir de las asistencias, sin tabla propia. Cachear el resultado por miembro con expiración corta si el cálculo pesa.

### Endpoints
- `POST /attendance/checkin` (body: `qr_code` o `member_id`)
- `GET /attendance` (filtros: fecha, miembro)
- `GET /members/{id}/attendance` + `GET /members/{id}/streak`
- `GET /attendance/peak-hours?from=&to=` → conteo agrupado por hora del día
- `GET /members/inactive?days=15` → miembros activos sin asistencia reciente

### Criterio de aceptación
Check-in por QR y manual funcionan, el duplicado diario es idempotente, la racha se calcula bien en un escenario con huecos.

---

# ETAPA 5 — Rutinas y ejercicios

**Objetivo:** el entrenador arma rutinas y las asigna; el miembro las consume.

### Entidades
- `Exercise` (id, gym_id nullable, name, description, muscle_group, image_url, video_url, is_global) — `gym_id` null + `is_global` true = biblioteca base compartida por todos los gyms. El filtro global debe permitir estos registros.
- `Routine` (id, gym_id, name, description, days_per_week, is_template, created_by, created_at)
- `RoutineDay` (id, routine_id, day_number, name)
- `RoutineExercise` (id, routine_day_id, exercise_id, order, sets, reps, rest_seconds, notes)
- `MemberRoutine` (id, member_id, routine_id, assigned_at, assigned_by, is_active)

### Reglas
- Asignar una plantilla a un miembro **copia** la rutina, no la referencia. Así el entrenador puede ajustar la rutina de un miembro sin afectar la plantilla ni a los demás.
- Un miembro tiene una sola rutina activa; asignar una nueva desactiva la anterior.
- Seed inicial de ~40 ejercicios globales por grupo muscular.

### Endpoints
- CRUD `/exercises` (listado combina globales + propios del gym)
- CRUD `/routines` con días y ejercicios anidados
- `POST /routines/{id}/assign` (uno o varios miembros)
- `GET /members/{id}/routine` → rutina activa completa
- `GET /me/routine` → para la app del miembro

### Criterio de aceptación
Crear plantilla, asignarla a dos miembros, modificar la de uno y verificar que la del otro y la plantilla no cambiaron.

---

# ETAPA 6 — Clases grupales

**Objetivo:** calendario, cupos e inscripciones.

### Entidades
- `GroupClass` (id, gym_id, location_id, name, description, instructor_name, capacity, day_of_week, start_time, duration_minutes, is_active) — plantilla recurrente semanal.
- `ClassSession` (id, class_id, scheduled_at, capacity_override nullable, status enum `scheduled|cancelled`) — instancia concreta.
- `ClassBooking` (id, session_id, member_id, status enum `booked|attended|cancelled|no_show`, booked_at)

### Reglas
- Las sesiones se **materializan bajo demanda**: al consultar el calendario de un rango, generar las sesiones faltantes de las clases recurrentes. Sin job programado.
- Cupo: contar bookings con estado `booked`. Si está lleno, rechazar con 409 y mensaje claro. Sin lista de espera en MVP.
- Cancelar con menos de X horas de anticipación (configurable por gym, default 2) marca `no_show`. Anotarlo, no penalizar automáticamente.
- Reservar requiere membresía vigente.

### Endpoints
- CRUD `/classes`
- `GET /classes/calendar?from=&to=` → sesiones del rango
- `POST /sessions/{id}/book`, `DELETE /sessions/{id}/book`
- `GET /sessions/{id}/attendees`
- `POST /sessions/{id}/attendance` → marcar asistentes
- `GET /me/bookings`

### Criterio de aceptación
Calendario semanal correcto, cupo respetado bajo reservas concurrentes.

---

# ETAPA 7 — Gamificación

**Objetivo:** logros que se desbloquean solos.

### Entidades
- `Achievement` (id, gym_id, name, description, icon_url, condition_type enum `visits|streak|classes`, condition_value, is_active)
- `MemberAchievement` (id, member_id, achievement_id, unlocked_at)

### Reglas
- `AchievementService.EvaluateAsync(memberId)` se invoca después de cada check-in y de cada asistencia a clase.
- Evalúa solo los logros aún no obtenidos por ese miembro. Idempotente: doble evaluación nunca duplica.
- Devuelve la lista de logros recién desbloqueados para que el endpoint que la disparó los incluya en su respuesta (así la app puede mostrar la animación sin un segundo request).
- Seed de logros por defecto al crear un gym: primera visita, 10 visitas, 50 visitas, racha de 7 días, racha de 30 días, 10 clases.

### Endpoints
- CRUD `/achievements`
- `GET /members/{id}/achievements` → obtenidos + pendientes con progreso
- `GET /me/achievements`

### Criterio de aceptación
Un check-in que cruza un umbral devuelve el logro desbloqueado en la misma respuesta.

---

# ETAPA 8 — Comunidad

**Objetivo:** feed privado por gym.

### Entidades
- `Post` (id, gym_id, author_user_id, content, image_url, type enum `announcement|achievement|general`, is_pinned, created_at)
- `PostLike` (post_id, member_id, created_at) — PK compuesta
- `PostComment` (id, post_id, member_id, content, created_at, deleted_at)

### Reglas
- Publicar como anuncio: solo owner/staff. Los miembros solo publican logros compartidos.
- Comentarios respetan el feature flag `community_comments`.
- Borrado de comentarios: soft delete, y solo por el autor o por staff.
- Feed paginado por cursor (`created_at` + id), no por offset.

### Endpoints
- `GET /community/feed` (cursor), `POST /community/posts`, `DELETE /community/posts/{id}`
- `POST|DELETE /community/posts/{id}/like`
- `GET|POST /community/posts/{id}/comments`, `DELETE /community/comments/{id}`

---

# ETAPA 9 — Dashboard, reportes y feature flags

**Objetivo:** cerrar el panel del dueño.

### Endpoints
- `GET /dashboard` → miembros activos, check-ins de hoy, ingresos del mes, membresías vencidas, próximas a vencer, comparativo con el mes anterior. **Una sola llamada, un solo response.**
- `GET /reports/revenue?from=&to=&groupBy=day|month`
- `GET /reports/attendance?from=&to=`
- `GET /reports/memberships` → altas, bajas, retención
- `GET|PUT /gym/feature-flags`
- `GET|PUT /gym/settings` (datos, logo, colores)
- `POST /gym/logo` → subida a R2

### Nota de rendimiento
El dashboard es el endpoint más consultado. Escribir las agregaciones como SQL crudo o proyecciones EF explícitas, nunca cargando entidades completas. Índices requeridos: `attendances(gym_id, checked_in_at)`, `payments(gym_id, paid_at)`, `memberships(member_id, end_date)`.

---

# ETAPA 10 — Infraestructura externa y cierre

1. **Cloudflare R2:** `IFileStorage` en Application, implementación S3-compatible en Infrastructure. Subida de logos, banners, fotos de miembros e íconos de logros. Validar tipo MIME y tamaño (máx. 5 MB), renombrar con GUID.
2. **Resend:** `IEmailSender`. Plantillas: bienvenida, invitación a la app, recuperación de contraseña.
3. **Push (FCM):** `INotificationSender` + tabla `device_tokens` (user_id, token, platform, registered_at). Disparadores: logro desbloqueado, membresía por vencer, nuevo anuncio de la comunidad.
4. **Logging:** Serilog estructurado, con `gym_id` y `user_id` enriquecidos en cada log.
5. **Rate limiting** en `/auth/login` (built-in de ASP.NET Core).
6. **Deploy:** Railway. Dockerfile multi-stage, migraciones aplicadas al arrancar solo en entorno de desarrollo; en producción, comando explícito.

---

## Orden de trabajo recomendado

Etapas 0 → 4 son el **backend mínimo vendible**: con eso un gym ya gestiona miembros, cobra y controla asistencia. Si aparece un piloto real antes de terminar todo, se puede pausar en la etapa 4 y empezar el panel Blazor sobre esos endpoints. Las etapas 5 a 8 son las que sostienen la app del miembro y los diferenciadores del producto.

## Qué NO construir en el MVP

Pasarela de pagos, facturación electrónica, entrenamiento personal como módulo aparte, chat entre miembros, lista de espera en clases, reportes exportables a PDF, impersonación de superadmin, panel de métricas SaaS. Todo eso es v2.
