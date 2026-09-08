# GSM — Contexto del proyecto

GSM (Gym System Management) es el codename técnico interno de un sistema de gestión de gimnasios B2B SaaS. El nombre comercial final aún no está definido (candidato: LudOS) — **usa siempre `GSM`** en código, namespaces, proyectos y schemas de base de datos. El nombre comercial solo vive en datos de tenant y marketing, nunca en código.

**Especificación técnica completa:** `docs/GSM-especificacion-tecnica.md`. Léela antes de generar entidades, casos de uso o endpoints — este archivo es solo el resumen que debes tener presente siempre.

**Plan de construcción por etapas:** `docs/BACKEND-MVP-PLAN.md`.

## Estado actual

Fase: **MVP, backend desde cero.** Aún no existe código. Trabajamos incremental: scaffold → Domain → Infrastructure/DbContext+migración → Application → API. No implementes más de una etapa sin confirmación.

## Stack

.NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL 16 · JWT + ASP.NET Identity · FluentValidation · Mapperly · Serilog

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

Hay dos casos donde un módulo necesita **escribir** en otro de forma síncrona, porque la operación debe ser atómica o su resultado forma parte de la respuesta. Se resuelven con una interfaz de escritura en el `Public/` del módulo dueño, nunca tocando su repositorio ni su entidad:

- `Identity.Public.IUserProvisioning` — alta de gym y canje de invitación crean el `AppUser` dentro de la misma transacción del caso de uso. Un evento dejaría el gym o el miembro sin cuenta si el handler falla.
- `Achievements.Public.IAchievementService` — el check-in evalúa los logros de forma síncrona para devolver los desbloqueos en su propia respuesta (decisión (b) de la etapa 7: mejor UX que hacer que la app consulte después).

**Antes de añadir una tercera, agota el camino normal.** La excepción se justifica solo por atomicidad o porque el resultado va en la misma respuesta; "es más cómodo" no basta. La regla sigue siendo: lecturas por `I{Módulo}Reader`, reacciones por evento.

### Casos de uso

Una clase por caso de uso con un único método `HandleAsync`, registrada en DI e inyectada directamente en el controller. Sin bus de comandos, sin `ISender`.

### Eventos in-process

En `GSM.Application/Common/Events/`:

- `IDomainEvent` — marcador.
- `IDomainEventHandler<TEvent>` — un método `HandleAsync(TEvent, CancellationToken)`.
- `IDomainEventPublisher` / `DomainEventPublisher` — resuelve del contenedor todos los handlers del tipo y los invoca.

Los handlers se registran por assembly scanning. La publicación ocurre **después** de que la transacción del caso de uso commitea, nunca dentro. Un handler que falla se loguea y no tumba la operación que publicó el evento.

Módulos MVP, en orden de implementación: **Identity → Gyms → Members → Memberships → Payments → Attendance → Achievements.**
Módulos de fases futuras (no implementar aún): Routines, Classes, Community, Reports, Locations, SuperAdmin.

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

## Base de datos

Un schema de PostgreSQL por módulo (`identity`, `gyms`, `members`, `memberships`, `payments`, `attendance`, `achievements`). Convenciones: `snake_case`, tablas en plural, PK siempre `id` (uuid), FK `{tabla_singular}_id`, índices `ix_{tabla}_{columna}`, constraints `fk_{tabla}_{tabla_referenciada}`, enums nativos de Postgres.

No hay FK entre schemas de módulos distintos: la referencia se valida en el caso de uso contra el `I{Módulo}Reader` correspondiente.

## Convenciones de código

- Inglés en código (clases, propiedades, métodos). Español permitido solo en contenido de datos.
- PascalCase (clases/métodos), camelCase (variables/parámetros), prefijo `I` en interfaces.
- IDs `Guid` v7 (`Guid.CreateVersion7()`), generados en el dominio.
- `DateTimeOffset` UTC en toda la aplicación; la conversión a `America/Bogota` es del cliente. Excepción: fechas de vigencia de membresías son `DateOnly`.
- Dinero: `decimal(12,2)`, COP.
- Logging con Serilog, structured, un log de entrada/salida por caso de uso. Nunca loguear passwords ni tokens.
- Conventional Commits (`feat:`, `fix:`, `refactor:`).

## Alcance MVP

7 módulos (Identity, Gyms, Members, Memberships, Payments, Attendance, Achievements), sus entidades, casos de uso y 15 endpoints — todo detallado en `docs/GSM-especificacion-tecnica.md`. No construyas Routines, Classes, Community, Locations ni SuperAdmin todavía.
