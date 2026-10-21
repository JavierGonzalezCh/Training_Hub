# GSM — Especificación técnica

> **Qué es este documento.** La arquitectura del backend: stack, capas, multi-tenancy, invariantes, autorización, catálogo de módulos, comunicación entre módulos y convenciones de datos. **No** describe entidades, casos de uso ni rutas de cada módulo: eso vive en la especificación de cada módulo (`docs/GSM-modulo-*.md`) y, para las rutas exactas, en la referencia Scalar que sirve la API en desarrollo (`/scalar`). Las convenciones de trabajo están en `docs/DESARROLLO.md`.
>
> **Nombre.** El producto se llama **TrainingHub** (panel = OS, app del miembro = GO). `GSM` (Gym System Management) es el codename técnico y se usa en solución, namespaces, proyectos y schemas. El nombre comercial vive solo en la identidad visible (textos de UI, correos, íconos) y en datos de tenant.

---

## 1. Stack tecnológico

| Capa | Tecnología | Versión |
|------|-----------|---------|
| Backend API | ASP.NET Core | .NET 10 LTS |
| ORM | Entity Framework Core | EF Core 10 |
| Base de datos | PostgreSQL | 18 |
| Auth | JWT + ASP.NET Identity | — |
| Storage de archivos | Cloudflare R2 | S3-compatible |
| Email transaccional | Resend | — |
| Push notifications | FCM (Android y web); iOS después | — |
| Validación | FluentValidation | — |
| Mapeo DTO ↔ Entidad | Mapperly | — |
| Eventos in-process | Despachador propio (`IDomainEventPublisher`), sin MediatR | — |
| Logging | Serilog | structured logging |

El frontend (Blazor WASM, MudBlazor y MAUI Blazor Hybrid) tiene su propia documentación: `GSM-apps-y-vistas.md`.

---

## 2. Arquitectura

### 2.1 Clean Architecture por capas

```
GSM.Domain          → entidades, enums, invariantes de negocio. Cero dependencias externas.
GSM.Application     → casos de uso, DTOs, interfaces públicas de módulos, eventos.
GSM.Infrastructure  → EF Core, repositorios, clientes externos (R2, Resend, FCM).
GSM.API             → controllers, middleware, auth, composición.
```

### 2.2 Monolito con disciplina de módulos (no monolito modular con proyectos separados)

Decisión: **un solo monolito**, no microservicios ni proyectos .csproj separados por módulo. La razón: equipo de un desarrollador, sin necesidad actual de escalar módulos independientemente ni de que múltiples devs trabajen en paralelo sin pisarse. El costo de un monolito modular completo (interfaces formales, más proyectos, decisiones prematuras de API interna) no se justifica todavía.

En su lugar, **disciplina de carpetas por módulo dentro de cada capa**:

```
GSM.Domain/{Módulo}/
GSM.Application/{Módulo}/
  ├── {UseCase}.cs
  ├── Public/           ← interfaces que otros módulos pueden consumir
  └── Events/           ← eventos de dominio que este módulo publica
GSM.Infrastructure/{Módulo}/
GSM.API/Controllers/{Módulo}Controller.cs
```

**Regla dura:** un módulo nunca referencia directamente una entidad Domain, DbContext o repositorio concreto de otro módulo. Solo puede consumir:

- Su `Public/I{Módulo}Reader` (lectura síncrona)
- Eventos de dominio vía el despachador propio `IDomainEventPublisher` (reacción asíncrona in-process)
- Como excepción, las tres interfaces de escritura de §4.2

Si el producto crece lo suficiente para justificarlo, esta disciplina permite migrar a monolito modular con proyectos separados sin reescritura — solo formalización de fronteras que ya existen en la práctica. Salvedad: queda un ciclo deliberado, `Attendance ↔ Achievements` (§4.5), que habría que romper antes de separar esos dos módulos.

### 2.3 Multi-tenancy

Todas las tablas de negocio llevan `gym_id`. Se implementa con **Global Query Filter** en EF Core:

```
modelBuilder.Entity<T>().HasQueryFilter(e => e.GymId == _currentUserService.GymId);
```

El filtro se desactiva únicamente para la cuenta SuperAdmin (`IsSuperAdmin`) y para las rutas del sistema sin usuario (`GymExecutionScope`, tareas periódicas). `ICurrentUserService` (en Infrastructure) lee `GymId`, `UserId`, `Role` desde el JWT vía `HttpContext`.

### 2.4 Manejo de invariantes de negocio — Fail-Fast

Las invariantes de negocio (ej. `EndDate > StartDate`, `Amount > 0`) se validan en el constructor/factory estático de la entidad de Domain, lanzando `DomainException` si se violan. No se usa el patrón `Result<T>`.

```csharp
public static Membership Create(Guid memberId, Guid planId, DateOnly start, DateOnly end)
{
    if (end <= start)
        throw new DomainException("EndDate debe ser posterior a StartDate");
    return new Membership { ... };
}
```

Las excepciones de dominio (`DomainException`, `NotFoundException`, `ConflictException`) se capturan en un middleware global de la API que las traduce a códigos HTTP (ver `CLAUDE.md`: 422 para invariantes, 409 para choques de estado, 400 solo para la forma del request).

### 2.5 Autorización

- `[Authorize(Roles = "Owner,Staff")]` a nivel de controller/endpoint.
- Validación adicional a nivel de caso de uso: si el rol es `Member`, se compara el `memberId` del request contra el `UserId` del token — un miembro solo puede acceder a sus propios recursos.

---

## 3. Catálogo de módulos

| # | Módulo | Responsabilidad | Especificación |
|---|--------|-----------------|----------------|
| 1 | Identity | Cuentas (una por persona, con acceso a varios gyms), JWT, permisos, invitaciones, dispositivos (kiosco) | `GSM-modulo-identity.md` |
| 2 | Gyms | Tenant raíz: datos, marca, horario, dueños, alta y onboarding, feature flags | `GSM-modulo-gyms.md` |
| 3 | Members | Datos personales del miembro, salud, medidas, autorizaciones, anonimización | `GSM-modulo-members.md` |
| 4 | Memberships | Planes, membresías, estado comercial (gracia, por vencer), grupos, congelar y cancelar | `GSM-modulo-memberships.md` |
| 5 | Payments | Registro de cobros, anulaciones, caja por turno con arqueo | `GSM-modulo-payments.md` |
| 6 | Attendance | Check-in con QR del gym o manual, rechazos, ocupación, horas pico | `GSM-modulo-attendance.md` |
| 7 | Achievements | Logros, rachas, récords, referidos, premios, ranking | `GSM-modulo-achievements.md` |
| 8 | Routines | Rutinas, catálogo de ejercicios, plantillas, registro de series | `GSM-modulo-routines.md` |
| 9 | Classes | Clases grupales, reservas, lista de espera, instructores | `GSM-modulo-classes.md` |
| 10 | SuperAdmin | Clientes SaaS: planes, suscripciones, pagos, métricas, impersonación | `GSM-modulo-superadmin.md` |
| 11 | Comunicaciones | Anuncios del gym con audiencias, reacciones, WhatsApp por enlace | `GSM-modulo-comunicaciones.md` |
| 12 | Equipment | Inventario de máquinas, estado y reportes de falla | `GSM-modulo-equipment.md` |
| 13 | Notifications | Bandeja, correos, campana del staff y push (FCM); primer proceso en segundo plano | `GSM-modulo-notifications.md` |

Transversales (no son módulos de negocio): **Logs** (`GSM-logs.md`), **Dashboards** (`GSM-dashboards.md`), **Apps y vistas** (`GSM-apps-y-vistas.md`) y el compositor de la racha (`GSM.Application/MemberStreak/`).

Sin planificar: **Reports** (módulo de descargas: Excel y similares) y **Locations** (multi-sede). No se implementan todavía.

---

## 4. Comunicación entre módulos

> El diseño original de esta sección preveía MediatR, escrituras cruzadas solo por evento y que `PaymentRecorded` renovase la membresía. Lo construido difiere en los tres puntos. Las tablas de interfaces y el grafo se generaron a partir del código. Si algo aquí choca con `CLAUDE.md`, manda `CLAUDE.md`.

Dos vías y una excepción: **lecturas** síncronas por `Public/I{Módulo}Reader`, **reacciones** por eventos in-process, y **tres escrituras síncronas** justificadas por atomicidad o porque su resultado va en la misma respuesta.

### 4.1 Interfaces públicas de lectura

Cada módulo expone su lectura en `Application/{Módulo}/Public/I{Módulo}Reader.cs`. Esa carpeta es el catálogo vigente (métodos incluidos); esta especificación no lo duplica para que no envejezca.

### 4.2 Escrituras cruzadas permitidas

| Módulo dueño | Interfaz | Métodos | Por qué es síncrona |
|---|---|---|---|
| Achievements | `IAchievementService` | `EvaluateForMemberAsync`, `GetOrGrantStreakFreezesAsync`, `ConsumeStreakFreezesAsync` | El check-in devuelve los logros desbloqueados en su propia respuesta; el compositor de racha consume los congeladores. |
| Gyms | `IGymAdministration` | `SetCommercialStatusAsync` | `suspend` / `reactivate` de SuperAdmin: el cambio de estado comercial es la respuesta misma de la operación. |
| Identity | `IUserProvisioning` | `CreateGymUserAsync` | Alta de gym y canje de invitación crean el `AppUser` en la misma transacción. |

No se añade una cuarta sin agotar el camino normal (ver `CLAUDE.md`).

### 4.3 Eventos de dominio (despachador propio, in-process)

Despachador propio (`IDomainEventPublisher`), sin MediatR. La publicación ocurre **después** del commit del caso de uso; un handler que falla se loguea y no tumba la operación.

Los eventos de cada módulo viven en `Application/{Módulo}/Events/`. Reglas: se publican después del commit, un handler que falla queda en `logs.event_handler_failures` con su payload para reintentarlo (`GSM-logs.md` §4.3), y **todo handler debe ser idempotente**. Comunicaciones, Notifications y los demás módulos reaccionan por evento; ninguno escribe en tablas ajenas.

**`PaymentRecorded` no renueva la membresía:** pago y membresía son operaciones separadas (etapa 5). Y el desbloqueo de logros por check-in **no** va por evento: es la escritura síncrona de `IAchievementService` (§4.2).

### 4.4 Servicios transversales (no son módulos de negocio)

- `IClock` — toda lectura de tiempo (`UtcNow`, `Today`, `BusinessDayRange`, `ToBusinessLocal`), en la zona del gym.
- `IEmailSender` — Resend, con `LoggingEmailSender` de respaldo mientras no haya API key.
- `IFileStorage` — Cloudflare R2, con `DisabledFileStorage` de respaldo mientras no haya credenciales.
- `GSM.Application/MemberStreak/` — compositor de la racha (Attendance + Routines + congeladores de Achievements).

El push (FCM para Android y web; iOS queda para después) lo envía el módulo Notifications (`GSM-modulo-notifications.md`). El `INotificationService` del diseño original nunca se construyó.

### 4.5 Grafo de dependencias síncronas

Quién lee (o escribe) a quién, calculado sobre el código sin contar comentarios (septiembre de 2026; desde entonces la v2 añadió Comunicaciones, Equipment y Notifications, y el único ciclo sigue siendo el deliberado de abajo). `API` son los controllers; `Dashboard` y `MemberStreak` son compositores, no módulos de negocio.

Las dependencias síncronas (lecturas por `I{Módulo}Reader`, las tres escrituras de §4.2 y los puertos invertidos como `IGymLoginGate`, que declara Identity e implementa SuperAdmin) se pueden recalcular sobre el código. Lo que importa de ese grafo:

**Un solo ciclo, y es deliberado: `Attendance ↔ Achievements`.** El check-in llama a `IAchievementService` para devolver los logros en su propia respuesta, y Achievements lee `IAttendanceReader` para evaluarlos. Es la excepción de §4.2, acotada a esos dos módulos.

**Cómo se rompieron los demás (septiembre de 2026).** `LoginHandler` (Identity) y `GetGymHandler` (Gyms) leían `ISubscriptionReader`, y como SuperAdmin lee a casi todos para su panel, esas dos lecturas dejaban a los diez módulos en una sola componente. Ahora el bloque `subscription` lo componen los controllers (`SubscriptionInfo` vive en `Application/Common`, solo con primitivos) y el login consulta la suspensión por `IGymLoginGate`, un puerto que declara Identity y que SuperAdmin implementa en Infrastructure. La dependencia quedó invertida: SuperAdmin → Identity.

Sin FK entre schemas: toda referencia cruzada se valida en el caso de uso contra el reader del módulo dueño.

---

## 5. Modelo de datos — PostgreSQL

### 5.1 Convenciones

- Todo en `snake_case`.
- Schemas: uno por módulo, nombre en minúsculas.
- Tablas: `snake_case`, plural.
- Columnas: `snake_case`.
- PK: siempre `id` (uuid).
- FK: `{tabla_singular}_id` (ej. `member_id`, `gym_id`).
- Índices: `ix_{tabla}_{columna(s)}`.
- FK constraints: `fk_{tabla}_{tabla_referenciada}`.
- Enums nativos de Postgres (no int) para mejor legibilidad en queries directas.

### 5.2 Schemas por módulo

Un schema de Postgres por módulo, sin FK entre ellos: `identity`, `gyms`, `members`, `memberships`, `payments`, `attendance`, `achievements`, `routines`, `classes`, `superadmin`, `communications`, `equipment`, `notifications` y `logs`. El de `logs` no tiene migraciones de EF: lo crea `Migrations/Scripts/V2_LG1.sql`. Las tablas de cada módulo están en su especificación y en `GSM.Infrastructure/{Módulo}/`.

```csharp
modelBuilder.Entity<Payment>().ToTable("payments", schema: "payments");
```

El schema separado es una capa extra de claridad y aislamiento en la base, pero no reemplaza la disciplina de código: cada módulo es el único que escribe en sus tablas.

---

## 6. Convenciones de código

- **Idioma:** inglés en código (clases, propiedades, métodos). Español permitido en contenido de datos (ej. `Concept = "Mensualidad enero"`).
- **Naming:** PascalCase (clases/métodos), camelCase (variables/parámetros), prefijo `I` en interfaces.
- **Errores:** excepciones de dominio custom (`DomainException` y sus derivadas), capturadas por middleware global en API.
- **Logging:** Serilog, structured logging, un log de entrada/salida por caso de uso ejecutado. Nunca loguear passwords ni tokens.
- **Commits:** Conventional Commits (`feat:`, `fix:`, `refactor:`).

---
