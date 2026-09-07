\# GSM — Contexto del proyecto



GSM (Gym System Management) es el codename técnico interno de un sistema de gestión de gimnasios B2B SaaS. El nombre comercial final aún no está definido (candidato: LudOS) — \*\*usa siempre `GSM`\*\* en código, namespaces, proyectos y schemas de base de datos. El nombre comercial solo vive en datos de tenant y marketing, nunca en código.



\*\*Especificación técnica completa:\*\* `docs/GSM-especificacion-tecnica.md`. Léela antes de generar entidades, casos de uso o endpoints — este archivo es solo el resumen que debes tener presente siempre.



\## Estado actual



Fase: \*\*MVP, backend desde cero.\*\* Aún no existe código. Trabajamos incremental: scaffold → Domain → Infrastructure/DbContext+migración → Application → API. No implementes más de una etapa sin confirmación.



\## Stack



.NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL 16 · JWT + ASP.NET Identity · FluentValidation · Mapperly · MediatR · Serilog



\## Estructura de la solución



```

src/Backend/

&#x20; GSM.Domain/          → entidades, enums, invariantes. Cero dependencias externas.

&#x20; GSM.Application/     → casos de uso, DTOs, interfaces públicas por módulo, eventos.

&#x20; GSM.Infrastructure/  → EF Core, repositorios, clientes externos (R2, Resend, FCM).

&#x20; GSM.API/             → controllers, middleware, auth.

```



\## Arquitectura: monolito con disciplina de módulos



Un solo monolito (no microservicios, no proyectos .csproj separados por módulo). Dentro de cada capa, carpetas por módulo:



```

GSM.Application/{Módulo}/

&#x20; ├── {UseCase}.cs

&#x20; ├── Public/    ← interfaz de lectura que otros módulos pueden consumir (I{Módulo}Reader)

&#x20; └── Events/    ← eventos de dominio que este módulo publica

```



\*\*Regla dura:\*\* un módulo nunca referencia directamente la entidad Domain, DbContext o repositorio concreto de otro módulo. Solo consume su `Public/I{Módulo}Reader` (síncrono, solo lectura) o eventos vía MediatR (asíncrono, in-process). Ninguna escritura cruzada directa.



Módulos MVP, en orden de implementación: \*\*Identity → Gyms → Members → Memberships → Payments → Attendance → Achievements.\*\*

Módulos de fases futuras (no implementar aún): Routines, Classes, Community, Reports, Locations, SuperAdmin.



\## Multi-tenancy



Toda tabla de negocio lleva `gym\_id`. Global Query Filter en EF Core (`HasQueryFilter(e => e.GymId == \_currentUserService.GymId)`), desactivado solo si `Role == SuperAdmin`. `ICurrentUserService` en Infrastructure lee `GymId`/`UserId`/`Role` del JWT.



\## Invariantes de negocio: fail-fast



Las entidades de Domain validan sus invariantes en el constructor/factory estático, lanzando `DomainException` si se violan (no se usa `Result<T>`). Las excepciones de dominio se capturan en un middleware global en la API que las traduce a códigos HTTP (400/404/409/etc.).



\## Base de datos



Un schema de PostgreSQL por módulo (`identity`, `gyms`, `members`, `memberships`, `payments`, `attendance`, `achievements`). Convenciones: `snake\_case`, tablas en plural, PK siempre `id` (uuid), FK `{tabla\_singular}\_id`, índices `ix\_{tabla}\_{columna}`, constraints `fk\_{tabla}\_{tabla\_referenciada}`, enums nativos de Postgres.



\## Convenciones de código



\- Inglés en código (clases, propiedades, métodos). Español permitido solo en contenido de datos.

\- PascalCase (clases/métodos), camelCase (variables/parámetros), prefijo `I` en interfaces.

\- Logging con Serilog, structured, un log de entrada/salida por caso de uso. Nunca loguear passwords ni tokens.

\- Conventional Commits (`feat:`, `fix:`, `refactor:`).



\## Alcance MVP



7 módulos (Identity, Gyms, Members, Memberships, Payments, Attendance, Achievements), sus entidades, casos de uso y 15 endpoints — todo detallado en `docs/GSM-especificacion-tecnica.md`. No construyas Routines, Classes, Community, Locations ni SuperAdmin todavía.

