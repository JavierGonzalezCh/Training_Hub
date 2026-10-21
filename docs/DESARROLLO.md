# Cómo se desarrolla GSM

Convenciones de trabajo del proyecto. Lo que describe qué hace cada módulo está en `docs/GSM-modulo-*.md`; la arquitectura, en `docs/GSM-especificacion-tecnica.md`; el resumen que se carga siempre, en `CLAUDE.md`. Cómo levantar el entorno: `README.md`.

## 1. Convenciones transversales

- **IDs:** `Guid` v7 (`Guid.CreateVersion7()`), generados en el dominio, no en la base.
- **Base de datos:** un schema de Postgres por módulo; `snake_case`, tablas en plural, PK `id`, FK `{tabla_singular}_id`, índices `ix_{tabla}_{columna}`, constraints `fk_{tabla}_{referenciada}`, enums nativos. Sin FK entre schemas de módulos distintos.
- **Tiempo:** instantes en `DateTimeOffset` UTC; días de negocio en `DateOnly` desde `IClock.Today` (zona del gym), nunca de UTC. Agrupar por día o mes usa `IClock.BusinessDayRange`. Npgsql solo escribe `DateTimeOffset` con offset 0.
- **Dinero:** `decimal(12,2)`, COP.
- **Errores:** las entidades validan en constructor o factory y lanzan `DomainException` (`ValidationDomainException` → 422, `UnauthorizedDomainException` → 401, `NotFoundException` → 404, `ConflictException` → 409, `ForbiddenDomainException` → 403). 400 es solo para la forma del request. Un middleware global lo mapea a `ProblemDetails`; todo lo demás es 500 genérico con el detalle en el log. No se usa `Result<T>`.
- **Casos de uso:** una clase por caso con un único `HandleAsync`, registrada en DI e inyectada en el controller. Sin bus de comandos.
- **Entre módulos:** lecturas por `Public/I{Módulo}Reader`, reacciones por eventos in-process (después del commit, handlers idempotentes). Solo hay tres escrituras síncronas cruzadas (ver `GSM-especificacion-tecnica.md` §4.2).
- **DTOs:** `record` inmutables; nunca se exponen entidades. Mapperly, un mapper por módulo; las consultas pesadas usan proyección con `Select`. Validación con FluentValidation por filtro de acción.
- **API:** controllers bajo `/api`, uno por módulo, `[Authorize(Policy = ...)]` a nivel de clase; enums en JSON por nombre (`JsonStringEnumConverter` global).
- **Logging:** Serilog estructurado con `gym_id` y `user_id`; nunca contraseñas ni tokens.
- **Commits:** Conventional Commits.
- **Terminología:** "miembro", nunca "socio".

## 2. Estructura de un módulo

```
Application/{Módulo}/{Módulo}Module.cs            → Add{Módulo}Module (DI)
Infrastructure/{Módulo}/{Módulo}InfrastructureModule.cs → Add{Módulo}Infrastructure
Infrastructure/{Módulo}/{Módulo}Enums.cs          → los enums de Postgres del módulo (IModuleEnums), una sola vez
```

Lo transversal se registra en `AddCommonInfrastructure`. El menú y las pestañas de `/settings` del panel se declaran por módulo en `GSM.Panel.UI/Navigation/Modules/*PanelModule.cs`; no se edita `NavMenu` ni `Settings/Index`.

## 3. Migraciones

- La API no migra al arrancar. `dotnet ef` usa `GSM.Infrastructure` como proyecto de arranque y lee `GSM_MIGRATIONS_CONNECTION` (por defecto la base `gsm`): **pasa siempre `--connection` explícito** para no migrar la base equivocada.
- El SQL escrito a mano de una migración (copiar datos, convertir enums, rellenar columnas) va en `GSM.Infrastructure/Migrations/Scripts/V2_<id>.sql` (recurso embebido) y la migración lo ejecuta con `migrationBuilder.SqlScript("<id>")`. Ese archivo no se pierde al regenerar.
- Si al unir ramas choca el snapshot y la migración tiene `Up` escrito a mano con un orden que importa, no se regenera: se regenera solo el snapshot desde el modelo unido (comprobado con una migración temporal) y se reescribe el Designer.
- Un relleno tras `AddColumn<DateOnly>` no nulo debe buscar `'-infinity'` (Npgsql escribe así `DateOnly.MinValue`).
- `ALTER TYPE ... ADD VALUE` no se revierte: va en el script SQL y se revisa con cuidado.
- Varias migraciones de la v2 no se revierten en datos (M6, R4, I2): respalda con `pg_dump` antes de migrar una base con datos.

## 4. Cliente del frontend

`GSM.Client/Generated/GsmApiClient.g.cs` nunca se edita ni se une a mano: se regenera con `scripts/generate-client.ps1` (Windows PowerShell 5.1 o `pwsh`). Tras cada regeneración hay que comprobar tres cosas de NSwag:

1. Cada cliente nuevo necesita su partial en `GSM.Client/Generated/ReadResponseAsString.cs`.
2. Una colección de enum o un enum suelto en el cuerpo de una respuesta sale como `int` aunque viaje como texto; el script reaplica el parche de colecciones, y para enums sueltos el DTO usa `string`.
3. Un parámetro `int X = 0` en un record de respuesta sale como `int? X = "0"` y no compila: sin valores por defecto en parámetros `int` de los DTOs de respuesta.

## 5. Tests

- **Integración** (`tests/GSM.IntegrationTests`): contra Postgres de verdad. `ApiFactory` lee la base de `GSM_TEST_DATABASE` (por defecto `gsm_test`), la migra y la limpia sola. Para trabajar en paralelo, una base por carril: `scripts/new-lane.ps1 <carril>` crea `gsm_dev_<carril>` y `gsm_test_<carril>`, y `scripts/drop-lane.ps1` las borra. Dos ejecuciones sobre la misma base se borran los datos.
- **bUnit** (`tests/GSM.Web.Tests`): registrar todos los servicios antes de pedir cualquiera (también `ViewportState.Pin`); un `[SupplyParameterFromQuery]` se pasa navegando con `NavigationManager`; MudBlazor puede poner `data-testid` en el `input` mismo. Los tests del teclado del kiosco son sensibles a la carga de la máquina y pueden fallar de forma intermitente.
- **Los tests no modifican datos sembrados por migraciones** (catálogo de ejercicios del sistema): si hace falta editar algo de sistema, se crea un registro desechable y se borra en un `finally`.
- **Identidad real por HTTP:** patrón `SubscriptionGateHttpTests` (`WithWebHostBuilder` sin `MutableCurrentUser`, login por `/api/auth/login`).
- **Tareas que recorren todos los gyms** (anonimización, `NotificationDispatchRunner.RunOnceAsync`) admiten `onlyGymId` solo para tests; con el reloj adelantado borrarían datos de otros tests en paralelo.
- **Un bug se arregla con su test:** primero el test que lo reproduce y falla, después el arreglo (`docs/BUGS.md`).
- Un test de migración puede fallar de forma esporádica si otras clases usan la misma base en paralelo; pasa ejecutado solo.

## 6. Trabajo con varios agentes

Se usó para construir la v2 y sirve para tareas grandes en paralelo.

- **Integrador:** reparte, une, regenera migraciones y cliente, corre toda la suite y actualiza `CLAUDE.md` y `docs/BUGS.md`. Es el único que toca la rama de integración.
- **Implementador:** una sub-etapa por agente, en su propio worktree, con tests; **si la especificación no alcanza o se contradice, se detiene y pregunta** (no inventa reglas de negocio) y no edita `CLAUDE.md` ni `docs/`.
- **Límite:** 3 o 4 agentes a la vez como máximo. Solo escriben código: no compilan ni corren tests ni `dotnet ef`; eso lo hace el integrador al unir la tanda. Tareas que comparten terreno van en serie.
- **Ramas de tarea:** `v2-<id>-<nombre>` (Git no admite `v2/...` mientras exista la rama `v2`).
- **Al unir:** el alcance es el de la tarea y nada más; todos los criterios tienen test; sin escrituras cruzadas nuevas; `IClock`; el patrón 400/422/409. Los worktrees de los agentes viven en `.claude/worktrees/` (ignorado por git).
- **Dos trampas conocidas:** la plantilla de Visual Studio de `.gitignore` ignora `[Ll]ogs/` (hay una excepción para `src/**` y `tests/**`; si una carpeta de código "desaparece" al unir, revisa `git status --ignored`), y un `GSM.API` en ejecución bloquea las DLL al compilar los tests (compilar con `--artifacts-path` aparte o detenerlo).
