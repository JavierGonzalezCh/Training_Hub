\# GSM — Especificación Técnica del Backend



> \*\*GSM\*\* (Gym System Management) es el codename técnico interno mientras se define el nombre comercial final (candidato actual: LudOS). Usar `GSM` en solución, namespaces, proyectos y schemas de base de datos. El nombre comercial vive solo en datos de tenant (gym\_themes) y marketing — nunca en código.



Este documento es la especificación completa para que Claude Code implemente el backend del MVP. No contiene código — contiene todas las decisiones necesarias para que la implementación no requiera inferencias ni suposiciones.



\---



\## 1. Stack tecnológico



| Capa | Tecnología | Versión |

|------|-----------|---------|

| Backend API | ASP.NET Core | .NET 10 LTS |

| ORM | Entity Framework Core | EF Core 10 |

| Base de datos | PostgreSQL | 16 |

| Auth | JWT + ASP.NET Identity | — |

| Storage de archivos | Cloudflare R2 | S3-compatible |

| Email transaccional | Resend | — |

| Push notifications | FCM / APNs | — |

| Validación | FluentValidation | — |

| Mapeo DTO ↔ Entidad | Mapperly | — |

| Eventos in-process | MediatR | — |

| Logging | Serilog | structured logging |



Panel admin (Blazor WASM) y app móvil (.NET MAUI) quedan \*\*fuera de alcance\*\* de esta especificación — solo backend.



\---



\## 2. Arquitectura



\### 2.1 Clean Architecture por capas



```

GSM.Domain          → entidades, enums, invariantes de negocio. Cero dependencias externas.

GSM.Application     → casos de uso, DTOs, interfaces públicas de módulos, eventos.

GSM.Infrastructure  → EF Core, repositorios, clientes externos (R2, Resend, FCM).

GSM.API             → controllers, middleware, auth, composición.

```



\### 2.2 Monolito con disciplina de módulos (no monolito modular con proyectos separados)



Decisión: \*\*un solo monolito\*\*, no microservicios ni proyectos .csproj separados por módulo. La razón: equipo de un desarrollador, sin necesidad actual de escalar módulos independientemente ni de que múltiples devs trabajen en paralelo sin pisarse. El costo de un monolito modular completo (interfaces formales, más proyectos, decisiones prematuras de API interna) no se justifica todavía.



En su lugar, \*\*disciplina de carpetas por módulo dentro de cada capa\*\*:



```

GSM.Domain/{Módulo}/

GSM.Application/{Módulo}/

&#x20; ├── {UseCase}.cs

&#x20; ├── Public/           ← interfaces que otros módulos pueden consumir

&#x20; └── Events/           ← eventos de dominio que este módulo publica

GSM.Infrastructure/{Módulo}/

GSM.API/Controllers/{Módulo}Controller.cs

```



\*\*Regla dura:\*\* un módulo nunca referencia directamente una entidad Domain, DbContext o repositorio concreto de otro módulo. Solo puede consumir:

\- Su `Public/I{Módulo}Reader` (lectura síncrona)

\- Eventos de dominio vía MediatR (reacción asíncrona in-process)



Si el producto crece lo suficiente para justificarlo, esta disciplina permite migrar a monolito modular con proyectos separados sin reescritura — solo formalización de fronteras que ya existen en la práctica.



\### 2.3 Multi-tenancy



Todas las tablas de negocio llevan `gym\_id`. Se implementa con \*\*Global Query Filter\*\* en EF Core:



```

modelBuilder.Entity<T>().HasQueryFilter(e => e.GymId == \_currentUserService.GymId);

```



El filtro se desactiva únicamente cuando `Role == SuperAdmin`. `ICurrentUserService` (en Infrastructure) lee `GymId`, `UserId`, `Role` desde el JWT vía `HttpContext`.



\### 2.4 Manejo de invariantes de negocio — Fail-Fast



Las invariantes de negocio (ej. `EndDate > StartDate`, `Amount > 0`) se validan en el constructor/factory estático de la entidad de Domain, lanzando `DomainException` si se violan. No se usa el patrón `Result<T>`.



```csharp

public static Membership Create(Guid memberId, Guid planId, DateOnly start, DateOnly end)

{

&#x20;   if (end <= start)

&#x20;       throw new DomainException("EndDate debe ser posterior a StartDate");

&#x20;   return new Membership { ... };

}

```



Las excepciones de dominio (`DomainException`, `NotFoundException`, `ConflictException`) se capturan en un middleware global de la API que las traduce a códigos HTTP (ver sección 7).



\### 2.5 Autorización



\- `\[Authorize(Roles = "Owner,Staff")]` a nivel de controller/endpoint.

\- Validación adicional a nivel de caso de uso: si el rol es `Member`, se compara el `memberId` del request contra el `UserId` del token — un miembro solo puede acceder a sus propios recursos.



\---



\## 3. Catálogo de módulos



| # | Módulo | Responsabilidad | Fase |

|---|--------|------------------|------|

| 1 | Identity | Autenticación, JWT, roles | MVP |

| 2 | Gyms | Tenant raíz, theming, feature flags | MVP |

| 3 | Members | Perfil del asistente, QR | MVP |

| 4 | Memberships | Planes y estado de membresía | MVP |

| 5 | Payments | Registro manual de cobros | MVP |

| 6 | Attendance | Check-in QR/manual | MVP |

| 7 | Achievements | Logros y rachas | MVP |

| 8 | Routines | Rutinas de entrenamiento | Fase 2 |

| 9 | Classes | Clases grupales | Fase 2 |

| 10 | Community | Feed, likes, comentarios | Fase 2 |

| 11 | Reports | Agregaciones/KPIs (sin entidades propias, solo lee de otros módulos) | Fase 2 |

| 12 | Locations | Multi-sede | Fase 3 |

| 13 | SuperAdmin | Clientes SaaS, suscripciones, métricas | Fase 3 |



Solo los módulos 1–7 (MVP) tienen especificación completa en este documento. Los módulos de Fase 2/3 quedan listados para preservar el catálogo, pero sin detalle de entidades/casos de uso todavía.



Orden de implementación (respeta dependencias): \*\*Identity → Gyms → Members → Memberships → Payments → Attendance → Achievements\*\*.



\---



\## 4. Comunicación entre módulos



\### 4.1 Interfaces públicas (lectura síncrona)



| Módulo | Interfaz | Métodos |

|--------|----------|---------|

| Identity | `IUserReader` | `GetUserById`, `GetUserRole`, `GetGymIdForUser` |

| Gyms | `IGymReader` | `GetGymById`, `GetGymTheme`, `IsFeatureEnabled(gymId, flagKey)` |

| Members | `IMemberReader` | `GetMemberById`, `GetMemberByQrCode`, `MemberExists` |

| Memberships | `IMembershipReader` | `HasActiveMembership(memberId)`, `GetActiveMembership(memberId)` |

| Payments | `IPaymentReader` | `GetPaymentHistory(memberId)` |

| Attendance | `IAttendanceReader` | `GetLastCheckIn(memberId)`, `GetAttendanceHistory(memberId)` |

| Achievements | `IAchievementReader` | `GetMemberAchievements(memberId)` |



Ningún módulo expone escritura a otros vía interfaz — solo lectura. Escrituras cruzadas van por evento.



\### 4.2 Eventos de dominio (MediatR, in-process)



| Evento | Publica | Escucha | Acción |

|--------|---------|---------|--------|

| `PaymentRecorded` | Payments | Memberships | Actualiza `Membership.EndDate` |

| `MemberCheckedIn` | Attendance | Achievements | Evalúa condiciones de logros/rachas |

| `AchievementUnlocked` | Achievements | Notifications (infra) | Envía push notification |



\### 4.3 Dependencia síncrona explícita permitida



`Attendance.CheckInMember` invoca `IMembershipReader.HasActiveMembership(memberId)` como validación de precondición antes de registrar el check-in.



\### 4.4 Servicio transversal (no es módulo de negocio)



`INotificationService` (en `Infrastructure/Notifications/`) — cualquier módulo puede invocarlo para push/email. Sin entidades propias.



\### 4.5 Grafo de dependencias



```

Identity ← (todos)

Gyms ← (todos)

Members ← Memberships, Payments, Attendance

Memberships ← Attendance (solo lectura)

Payments → evento → Memberships

Attendance → evento → Achievements

```



Sin ciclos, sin escrituras cruzadas directas.



\---



\## 5. Entidades de Domain (MVP)



```

Gym

&#x20; Id: Guid (PK)

&#x20; Name: string(100)

&#x20; Plan: enum { Basic, Pro, Enterprise }

&#x20; Status: enum { Trial, Active, Suspended }

&#x20; OwnerId: Guid (FK → User)

&#x20; CreatedAt: DateTime

&#x20; Invariante: Name no vacío; Status inicial = Trial al crear



GymTheme

&#x20; Id: Guid (PK)

&#x20; GymId: Guid (FK → Gym, único)

&#x20; PrimaryColor: string(7)

&#x20; LogoUrl: string?

&#x20; BannerUrl: string?

&#x20; BannerText: string(100)?

&#x20; ShowPoweredBy: bool = true

&#x20; AppName: string?   // solo Enterprise

&#x20; Invariante: PrimaryColor debe ser hex válido (#RRGGBB)



GymFeatureFlags

&#x20; GymId: Guid (PK + FK → Gym)

&#x20; ShowRoutines, ShowClasses, ShowProgress, ShowGamification, ShowCommunity: bool = true



User

&#x20; Id: Guid (PK)

&#x20; Email: string (único)

&#x20; PasswordHash: string

&#x20; Role: enum { SuperAdmin, Owner, Staff, Member }

&#x20; GymId: Guid? (FK → Gym, null solo si Role = SuperAdmin)

&#x20; IsActive: bool = true

&#x20; CreatedAt: DateTime

&#x20; Invariante: GymId null ⟺ Role = SuperAdmin



Member

&#x20; Id: Guid (PK)

&#x20; UserId: Guid (FK → User)

&#x20; GymId: Guid (FK → Gym)

&#x20; FullName: string(100)

&#x20; Phone: string(20)?

&#x20; PhotoUrl: string?

&#x20; QrCode: string (único, autogenerado)

&#x20; JoinedAt: DateOnly

&#x20; Invariante: User.GymId debe coincidir con Member.GymId



MembershipPlan

&#x20; Id: Guid (PK)

&#x20; GymId: Guid (FK → Gym)

&#x20; Name: string(100)

&#x20; DurationDays: int

&#x20; Price: decimal(10,2)

&#x20; Invariante: DurationDays > 0; Price >= 0



Membership

&#x20; Id: Guid (PK)

&#x20; MemberId: Guid (FK → Member)

&#x20; PlanId: Guid (FK → MembershipPlan)

&#x20; Status: enum { Active, Expired, Paused }

&#x20; StartDate: DateOnly

&#x20; EndDate: DateOnly

&#x20; Invariante: EndDate > StartDate (fail-fast en Create)



Payment

&#x20; Id: Guid (PK)

&#x20; MemberId: Guid (FK → Member)

&#x20; GymId: Guid (FK → Gym)

&#x20; Amount: decimal(10,2)

&#x20; Method: enum { Cash, Transfer, Card, Other }

&#x20; Concept: string(200)

&#x20; RegisteredBy: Guid (FK → User)

&#x20; PaidAt: DateTime

&#x20; Invariante: Amount > 0



Attendance

&#x20; Id: Guid (PK)

&#x20; MemberId: Guid (FK → Member)

&#x20; GymId: Guid (FK → Gym)

&#x20; CheckedInAt: DateTime

&#x20; Method: enum { Qr, Manual }

&#x20; Invariante: ninguna en Domain — validación de membresía activa vive en Application



Achievement

&#x20; Id: Guid (PK)

&#x20; GymId: Guid (FK → Gym)

&#x20; Name: string(100)

&#x20; Description: string?

&#x20; IconUrl: string?

&#x20; ConditionType: enum { Visits, Streak, Classes }

&#x20; ConditionValue: int

&#x20; Invariante: ConditionValue > 0



MemberAchievement

&#x20; Id: Guid (PK)

&#x20; MemberId: Guid (FK → Member)

&#x20; AchievementId: Guid (FK → Achievement)

&#x20; UnlockedAt: DateTime

&#x20; Invariante: (MemberId, AchievementId) único — no se desbloquea dos veces

```



\---



\## 6. Casos de uso de Application (MVP)



| Módulo | Comando/Query | Input clave | Output |

|--------|---------------|--------------|--------|

| Identity | `Login` | email, password | JWT + role + gymId |

| Identity | `RegisterStaffUser` | email, password, role, gymId | UserId |

| Gyms | `CreateGym` | name, ownerEmail | GymId |

| Gyms | `UpdateGymTheme` | gymId, colores, logo | — |

| Gyms | `UpdateFeatureFlags` | gymId, flags | — |

| Members | `RegisterMember` | gymId, fullName, phone | MemberId + QrCode |

| Members | `GetMemberProfile` | memberId | MemberDto |

| Memberships | `CreateMembershipPlan` | gymId, name, duration, price | PlanId |

| Memberships | `AssignMembership` | memberId, planId, startDate | MembershipId |

| Payments | `RecordPayment` | memberId, amount, method, concept | PaymentId |

| Payments | `GetPaymentHistory` | memberId | List\\<PaymentDto\\> |

| Attendance | `CheckInMember` | memberId (o qrCode), method | AttendanceId |

| Attendance | `GetAttendanceHistory` | memberId | List\\<AttendanceDto\\> |

| Achievements | `CreateAchievement` | gymId, name, conditionType, value | AchievementId |

| Achievements | `EvaluateAchievements` \*(interno, disparado por evento)\* | memberId, gymId | List\\<AchievementUnlocked\\> |



\---



\## 7. Contratos de API (MVP)



```

POST   /api/auth/login                    → 200 { token, role, gymId }

POST   /api/gyms                          → 201 { gymId }              \[SuperAdmin]

PATCH  /api/gyms/{id}/theme               → 204                        \[Owner]

PATCH  /api/gyms/{id}/feature-flags       → 204                        \[Owner]



POST   /api/members                       → 201 { memberId, qrCode }   \[Owner, Staff]

GET    /api/members/{id}                  → 200 MemberDto              \[Owner, Staff, Member(self)]



POST   /api/membership-plans              → 201 { planId }             \[Owner]

POST   /api/memberships                   → 201 { membershipId }       \[Owner, Staff]



POST   /api/payments                      → 201 { paymentId }          \[Owner, Staff]

GET    /api/payments?memberId=            → 200 List<PaymentDto>       \[Owner, Staff, Member(self)]



POST   /api/attendance/checkin            → 201 { attendanceId }       \[Owner, Staff, Member(self)]

GET    /api/attendance?memberId=          → 200 List<AttendanceDto>    \[Owner, Staff, Member(self)]



POST   /api/achievements                  → 201 { achievementId }      \[Owner]

GET    /api/achievements/member/{id}      → 200 List<AchievementDto>   \[Owner, Staff, Member(self)]

```



\*\*Códigos de error estandarizados:\*\*



| Código | Causa |

|--------|-------|

| 400 | Validación fallida (FluentValidation) |

| 401 | No autenticado |

| 403 | Rol o tenant incorrecto |

| 404 | Recurso no encontrado |

| 409 | Conflicto de invariante de dominio (ej: logro ya desbloqueado) |

| 500 | Error no manejado |



\---



\## 8. Modelo de datos — PostgreSQL



\### 8.1 Convenciones



\- Todo en `snake\_case`.

\- Schemas: uno por módulo, nombre en minúsculas.

\- Tablas: `snake\_case`, plural.

\- Columnas: `snake\_case`.

\- PK: siempre `id` (uuid).

\- FK: `{tabla\_singular}\_id` (ej. `member\_id`, `gym\_id`).

\- Índices: `ix\_{tabla}\_{columna(s)}`.

\- FK constraints: `fk\_{tabla}\_{tabla\_referenciada}`.

\- Enums nativos de Postgres (no int) para mejor legibilidad en queries directas.



\### 8.2 Schemas por módulo (MVP)



```

identity

&#x20; └── users



gyms

&#x20; ├── gyms

&#x20; ├── gym\_themes

&#x20; └── gym\_feature\_flags



members

&#x20; └── members



memberships

&#x20; ├── membership\_plans

&#x20; └── memberships



payments

&#x20; └── payments



attendance

&#x20; └── attendances



achievements

&#x20; ├── achievements

&#x20; └── member\_achievements

```



Configuración en EF Core:

```csharp

modelBuilder.Entity<Payment>().ToTable("payments", schema: "payments");

modelBuilder.Entity<Member>().ToTable("members", schema: "members");

```



FKs cruzadas entre schemas son válidas en Postgres (ej. `payments.payments.member\_id → members.members.id`).



\*\*Nota:\*\* el schema separado es una capa adicional de claridad y aislamiento a nivel de base de datos, pero no reemplaza la disciplina de código: cada módulo sigue siendo el único que escribe en su propio schema, vía su propio repositorio en Infrastructure.



\---



\## 9. Convenciones de código



\- \*\*Idioma:\*\* inglés en código (clases, propiedades, métodos). Español permitido en contenido de datos (ej. `Concept = "Mensualidad enero"`).

\- \*\*Naming:\*\* PascalCase (clases/métodos), camelCase (variables/parámetros), prefijo `I` en interfaces.

\- \*\*Errores:\*\* excepciones de dominio custom (`DomainException`, `NotFoundException`, `ConflictException`), capturadas por middleware global en API.

\- \*\*Logging:\*\* Serilog, structured logging, un log de entrada/salida por caso de uso ejecutado. Nunca loguear passwords ni tokens.

\- \*\*Commits:\*\* Conventional Commits (`feat:`, `fix:`, `refactor:`).



\---



\## 10. Alcance MVP



\*\*Entra en la primera pasada de Claude Code:\*\* los 7 módulos MVP (sección 3), sus entidades (sección 5), casos de uso (sección 6) y los 15 endpoints (sección 7).



\*\*No entra todavía:\*\* Routines, Classes, Community, Locations, SuperAdmin — quedan en el catálogo (sección 3) sin especificación detallada, para fases posteriores.

