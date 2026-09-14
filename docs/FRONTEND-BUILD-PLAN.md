# GSM — Plan de construcción del frontend

> **Estado (septiembre de 2026):** **F0, F1 y F2 hechas** (esqueleto + cliente NSwag; login/sesión/shell con identidad visual v4.0; módulo de Socios completo: listado, alta/edición, baja, ficha con QR/invitación/foto/pestañas paginadas). Siguiente: **F3**.

## Contexto
El backend está completo (etapas 0–7, 9 Routines, 10 Classes, 11 SuperAdmin; 111 tests), pero no hay ningún cliente: hoy el sistema solo se usa desde Scalar. Para vender, el gym tiene que poder operar (socios, cobros, asistencia) y el socio tiene que tener su app. Este documento define **las etapas de frontend F0–F8** con el mismo método que el backend: incremental y **una etapa (o sub-etapa) cada vez, con confirmación entre una y otra**.

Decisiones tomadas:
- **Stack:** Blazor WebAssembly para el panel del gym y el panel SuperAdmin, y .NET MAUI para la app del socio, como ya preveía `docs/GSM-especificacion-tecnica.md` §1.
- **El socio puede usar la app desde el navegador, y funciona igual.** Por eso la app del socio es **MAUI Blazor Hybrid** y no una UI nativa en XAML. Sus pantallas viven en una librería Razor compartida (`GSM.Member.UI`) que se aloja tanto en la web como en el contenedor MAUI. El código, las pantallas y las reglas son los mismos. Por plataforma solo cambian unos pocos servicios, que van detrás de interfaces: el almacén de tokens, abrir enlaces y, más adelante, las notificaciones push.
- **UI kit:** MudBlazor.
- **Orden:** primero el panel del gym, luego la app del socio y por último el panel SuperAdmin.
- **El backend está cerrado y no se toca**, salvo los huecos que se listan abajo. Cada hueco se decide en la etapa que lo necesita.

## Huecos de backend detectados (se deciden en su etapa, no antes)
| # | Hueco | Etapa | Propuesta |
|---|---|---|---|
| H1 | **No hay endpoints para crear, listar ni desactivar usuarios staff.** El owner se crea junto con el gym y nadie más puede dar de alta recepcionistas; `staff@demo.local` existe solo porque lo crea el `DevSeeder`. | F5 | Mini-etapa de backend `api/gym/staff` (GET/POST/DELETE, solo Owner) que use `IUserProvisioning`. Si no se hace, F5 sale sin gestión de staff. |
| H2 | `POST /api/me/routines` declara `MyRoutineResponse` en `ProducesResponseType`, pero en realidad devuelve `RoutineResponse`, así que el cliente generado tendría el tipo equivocado. | F7.4 | Corregir el atributo en `MeController.cs` (una línea). |
| H3 | `forgot-password` envía el token por correo **sin enlace** (`resetUrl: null`). | F1 | Pantalla donde el usuario pega el código, sin tocar el backend. Cuando haya dominio desplegado, se pasa la URL de la SPA. |
| H4 | `MeFeatureFlags` no incluye `MembersCanCreateRoutines`. Para saberlo, la app tendría que llamar a `GET /api/gym/feature-flags`, que es solo para staff. | F7.4 | Añadir el flag a `MeFeatureFlags`. La alternativa sin tocar el backend es ocultar "crear rutina" y tratar el 403. |

## Arquitectura

```
src/Frontend/
  GSM.Client/        → librería net10.0: cliente HTTP tipado (generado de OpenAPI), auth y errores. La usan todos los demás proyectos.
  GSM.UI.Shared/     → Razor Class Library: tema por gym, banner de suscripción, manejo de errores, formato de COP y fechas, etiquetas de enums.
  GSM.Member.UI/     → Razor Class Library con TODAS las pantallas del socio (mobile-first). Sin dependencias de plataforma.
  GSM.Web/           → Blazor WASM: panel del gym, panel SuperAdmin y la app del socio en `/app` (aloja GSM.Member.UI). PWA instalable.
  GSM.Mobile/        → .NET MAUI Blazor Hybrid (Android primero, iOS después): aloja GSM.Member.UI en un BlazorWebView.
tests/
  GSM.Web.Tests/     → bUnit para los componentes con lógica: formularios, guardas de rol, banner y pantallas del socio.
```

**Un solo SPA web con tres áreas por rol:**
- el panel del gym, para `owner` y `staff`;
- `/admin/**`, para `superadmin`;
- `/app/**`, para `member`.

Las tres áreas comparten el login, el cliente y el tema, y después del login cada usuario va al área de su rol. Hacer tres SPAs duplicaría la autenticación y el despliegue.

**Cómo se consigue que funcione igual en el navegador y en el móvil:**
- Las pantallas del socio se escriben **una sola vez**, en `GSM.Member.UI`, que solo depende de `GSM.Client` y `GSM.UI.Shared`. Ni `GSM.Web` ni `GSM.Mobile` tienen pantallas propias del socio: solo las alojan.
- Lo que cambia según la plataforma va detrás de interfaces declaradas en `GSM.Client`, con una implementación por host:
  | Interfaz | Web (WASM) | Móvil (MAUI) |
  |---|---|---|
  | `ITokenStore` | `localStorage` | `SecureStorage` |
  | `IAppPlatform` (abrir enlaces, compartir, subir el brillo al mostrar el QR) | JS interop o no-op | APIs de MAUI |
  | Push (futuro) | no disponible | FCM |
- Regla de revisión: si una pantalla de `GSM.Member.UI` necesita un `#if ANDROID` o una API de MAUI, está en el sitio equivocado. Eso tiene que ir detrás de una interfaz.
- La web es una **PWA** (manifest y service worker), así que el socio puede instalarla desde el navegador del móvil sin pasar por la tienda. No tiene modo offline de datos: todo sale de la API.
- MudBlazor se usa en los dos hosts. Las pantallas del socio se diseñan mobile-first; en un PC se muestran en una columna centrada.

### Contratos: un cliente generado desde OpenAPI, sin referenciar `GSM.Application`
Los `*Contracts.cs` de Application importan enums de `GSM.Domain` y tipos de la propia Application (por ejemplo, `AttendanceContracts.cs` usa `Achievements.Public`). Si WASM o MAUI referenciaran `GSM.Application`, arrastrarían Domain, FluentValidation y Mapperly al cliente y se rompería la separación. Por eso:
- La API ya publica su documento OpenAPI con `Microsoft.AspNetCore.OpenApi` (`app.MapOpenApi()` en `Program.cs`).
- `GSM.Client` genera el cliente tipado con **NSwag** (o Kiota) a partir de `/openapi/v1.json`. El código generado se versiona en el repo y se regenera con un script (`scripts/generate-client.ps1`). Si el diff del cliente generado cambia, es que cambió un contrato.
- Por encima del código generado, `GSM.Client` añade a mano tres piezas:
  - `AuthenticatedHttpHandler`: pone el Bearer y hace el refresh de forma transparente cuando llega un 401.
  - `ApiError`: la traducción tipada de `ProblemDetails` y `ValidationProblemDetails`.
  - `ITokenStore`: abstracto; `localStorage` en WASM y `SecureStorage` en MAUI.

Lo que el cliente da por hecho del contrato HTTP (comprobado en `Program.cs` y `Application/Common`):
- El JSON va en camelCase. **Los enums llegan como string con el nombre del miembro C#** (`"Cash"`, `"Smith_machine"`, `subscription.status = "Overdue"`). El cliente los traduce al español con un diccionario; el valor crudo nunca se muestra.
- La paginación es `PagedResult<T>` = `{ items, page, pageSize, total, totalPages }`. Se usa un único componente, `ServerTable<T>`, montado sobre `MudTable` con `ServerData`.
- OpenAPI solo se sirve en Development (`/openapi/v1.json`), así que el cliente se genera en local y nunca contra producción.
- Tokens: un access token de 60 minutos y un refresh token opaco de 30 días **que rota** (cada refresh revoca el anterior). Por eso los refresh tienen que ir de uno en uno: aunque fallen varias llamadas a la vez, solo puede haber un refresh en vuelo. El token de impersonación dura 15 minutos y **no tiene refresh**.
- `GET /api/auth/me` devuelve `MeResponse` (rol, gym, branding, feature flags y suscripción) y es **el bootstrap** de los dos clientes.
- Login, registro y refresh tienen un límite de 10 peticiones por minuto por IP. Al pasarlo llega un 429, que se muestra con un mensaje propio.

### Reglas transversales del cliente (se implementan en F0/F1 y valen para todo)
- **Cada status tiene su tratamiento en la UI**, siguiendo la convención del backend:
  - 400: errores por campo en el formulario, tomados de `ValidationProblemDetails.errors`.
  - 422: mensaje de "la operación no procede", con el `detail`.
  - 409: conflicto (duplicado, cupo lleno).
  - 403: falta de permiso **o bloqueo por suscripción**. Los dos llegan con el mismo `title`; se distinguen mirando `subscription.status` en el bootstrap.
  - 401: se intenta un refresh y, si falla, se vuelve al login.
- **Días de negocio:** un `DateOnly` se muestra tal cual, sin convertirlo desde UTC. Los instantes `DateTimeOffset` se muestran en la zona del gym (America/Bogotá). El "hoy" que usa la lógica nunca sale del reloj del navegador: la fuente es el backend.
- **Dinero:** COP sin decimales en pantalla, con formato `es-CO`.
- **Idioma:** la UI va en español y el código, en inglés.
- **Branding por gym:** el layout aplica al tema de MudBlazor el `PrimaryColor`, el `SecondaryColor` y el `LogoUrl` que devuelve `GET /api/auth/me`. El nombre comercial nunca va en el código.
- **Feature flags del gym** (`GymFeatureFlagsResponse`): ocultan secciones (rutinas, clases, gamificación) tanto en el panel como en la app.
- **Banner de suscripción:** sale del bloque `subscription` que devuelven `/api/auth/me` y `/api/gym`.
  - `Overdue`: aviso, con `daysUntilDisabled`.
  - `Disabled`: **modo solo lectura**. Los GET funcionan y se ocultan las acciones de escritura.
  - `Suspended`: pantalla de bloqueo.
  - `null`: el gym no tiene suscripción. No se muestra ningún aviso, porque el backend tampoco lo bloquea.
- **Impersonación:** cuando el token trae `impersonating=true`, se muestra una barra fija "Viendo como SuperAdmin — solo lectura" con la cuenta atrás de los 15 minutos, y se ocultan las acciones de escritura. Mientras dura, los tokens propios del superadmin se guardan aparte; al terminar (`POST /api/admin/impersonate/end`) se restauran.
- **Una sola directiva de rol** (`<RequireRole>` o `AuthorizeView`) oculta lo que la API rechazaría. Usa las mismas políticas que `AuthSetup.cs`: Owner, Staff y SuperAdmin.

## Etapas

Cada etapa termina con tres cosas:
- el build limpio;
- tests bUnit de todo lo que tenga lógica;
- una **verificación manual contra la API local** con los usuarios del `DevSeeder` (`owner@demo.local`, `staff@demo.local`, `admin@gsm.local`).

### F0 — Esqueleto y cliente
1. Crear los proyectos:
   - `GSM.Client` (net10.0).
   - `GSM.UI.Shared` (RCL con lo transversal).
   - `GSM.Web` (Blazor WASM standalone con MudBlazor).
   - `GSM.Web.Tests` (bUnit).

   `GSM.Member.UI` y `GSM.Mobile` se crean en F7.1, no antes. El frontend tiene su propia `GSM.Frontend.sln` en `src/Frontend/`, para que el build y los tests del backend (`GSM.sln`) no dependan de los workloads de MAUI.
2. `GSM.Web` escucha en **`https://localhost:7220`** y **`http://localhost:5220`**, que son los orígenes que `appsettings.Development.json` ya admite en `Cors:AllowedOrigins`. Así F0 no necesita **ningún cambio en el backend**.
3. El cliente NSwag se genera desde `https://localhost:7219/openapi/v1.json` con `scripts/generate-client.ps1`, y el código generado se versiona.
4. Piezas del cliente:
   - `AuthenticatedHttpHandler`, con el Bearer y el refresh de uno en uno.
   - `ApiError`, que cubre `ProblemDetails`, `ValidationProblemDetails` y el 429.
   - `ITokenStore`.
   - `ApiBaseUrl`, configurada en `wwwroot/appsettings.json`.
5. `.claude/launch.json` con la API y la web, para poder usar `preview_start`.

**Aceptación:**
- La SPA arranca en el puerto 7220 y llama a `/health` de la API sin errores de CORS.
- El cliente generado compila.
- Los tests de `ApiError` (400 con errores por campo, 422, 409 y 403) pasan.

### F1 — Auth y shell del panel
- **Login** con `/api/auth/login`: 401 si las credenciales son malas, 403 si el gym está suspendido y 429 si se pasa el rate limit.
- **Sesión:** refresh, logout y bootstrap con `GET /api/auth/me`.
- **"Olvidé mi contraseña":** `forgot-password`, y después una pantalla donde el usuario pone su email, el código y la clave nueva para llamar a `reset-password` (ver H3).
- **Estado de autenticación:** un `AuthenticationStateProvider` propio que lee `role`, `gym_id` e `impersonating` del JWT.
- **Layout MudBlazor** con el menú según el rol: el Owner ve la configuración y los planes, el Staff no. Lleva aplicado el branding del gym.
- **Protección:** guardas de ruta, banner de suscripción y página de acceso denegado.
- **Redirección por rol después del login:** `owner` y `staff` van al panel, `superadmin` a `/admin` y `member` a `/app`. En F1, `/app` es solo un marcador y se llena en F7. Si alguien entra en un área que no es la de su rol, se le manda a la suya.

**Aceptación:**
- Owner y staff entran y ven menús distintos.
- Un token vencido se refresca sin perder la página.
- Un gym `Disabled` queda en solo lectura y uno `Suspended` no puede entrar.

### F2 — Socios
- **Listado** paginado en el servidor, con búsqueda y filtro de estado (`GET /api/members`, `ListMembersQuery`).
- **Alta y edición** con `CreateMemberRequest` y `UpdateMemberRequest`. Un 409 significa que el documento ya existe.
- **Desactivar:** el `DELETE` es una baja lógica.
- **Ficha** (`MemberDetail`): membresía vigente y últimos 5 pagos, más la racha. Tiene pestañas paginadas de pagos (`/api/members/{id}/payments`) y de asistencia (`/attendance`).
- **Foto:** `POST /photo`, multipart con el campo `file`. Mientras R2 no esté configurado, responde 500; la pantalla tiene que dar un mensaje claro.
- **Código QR** del socio, dibujado en el cliente.
- **Generar invitación** (`POST /invite`): devuelve un código válido 7 días para dárselo al socio.

### F3 — Planes, membresías y pagos
- **Planes** (`/api/membership-plans`): el Staff los consulta y **solo el Owner** los crea, edita y desactiva.
- **Membresías:** asignarlas desde la ficha del socio (`POST /api/members/{id}/memberships`), con su historial, y ver las que están por vencer (`/api/memberships/expiring?days`).
- **Pagos:** registrar un pago (`POST /api/payments`) y listarlos con filtros por fecha, método y socio (`ListPaymentsQuery`).
- **Cierre de caja** diario (`/api/cash-register/daily?date`). La fecha es un `DateOnly` de negocio y no se convierte entre zonas.

### F4 — Recepción: check-in y asistencia
- **Pantalla de recepción:** el socio se identifica con el QR (cámara por JS interop, o un lector de código de barras USB que "teclea" el código) o buscándolo a mano. La pantalla llama a `POST /api/attendance/checkin` con el `QrCode` o el `MemberId` y el `Method`.
- **Resultado en grande:**
  - 201: entrada nueva.
  - 200 con `alreadyCheckedInToday`: el socio ya había entrado hoy.
  - `membershipExpired`: aviso en rojo. No bloquea la entrada, solo avisa.
  - Los logros que se hayan desbloqueado.
- **Listado de asistencia** (`GET /api/attendance`) y gráfico de horas pico (`/peak-hours`).

### F5 — Dashboard y configuración del gym
- **Inicio** con `GET /api/dashboard`: socios activos, check-ins de hoy, ingresos del mes frente al anterior, y membresías vencidas o por vencer con enlace a la ficha.
- **Configuración** (solo Owner): datos del gym y branding (`PUT /api/gym`), logo (`POST /api/gym/logo`) y feature flags (`/api/gym/feature-flags`).
- **Logros del gym** (solo Owner): CRUD e icono sobre `/api/achievements`.
- **Gestión de staff:** solo si se aprueba H1. Si no, F5 sale sin ella.

### F6 — Rutinas y clases (panel)
- **Catálogo de ejercicios** (`/api/exercises`): los del sistema son de solo lectura (intentar editarlos da 403) y los del gym se pueden editar. Se filtra por músculo y equipo, con las etiquetas en español.
- **Editor de rutinas:** un árbol de días y ejercicios, con ciclo rotativo y días de descanso, sobre `/api/members/{id}/routines` y `/api/routines/{id}`.
- **Clases** (`/api/classes`):
  - CRUD de clases recurrentes y puntuales. Da 422 si falta `days_of_week` o `scheduled_date`.
  - **Horario semanal proyectado** (`/schedule?from&to`). Las sesiones que aún no están materializadas se pintan igual que las demás.
  - Cancelar una sesión con motivo.
  - Lista de la sesión (`/roster`) y marcar asistencia o `no_show`.

— *Aquí el panel del gym está completo: es el punto natural para empezar un piloto con un gym real.* —

### F7 — App del socio (web `/app` + MAUI Blazor Hybrid)
Cada sub-etapa se construye en `GSM.Member.UI` y se verifica **en los dos hosts**: el navegador (`GSM.Web/app`) y el emulador de Android (`GSM.Mobile`). Una sub-etapa que solo funciona en uno de los dos no está terminada.

Sub-etapas:
- **F7.1 — Proyectos y acceso.**
  - Se crean `GSM.Member.UI` y `GSM.Mobile` (MAUI Blazor Hybrid, Android primero), con `ITokenStore` sobre `SecureStorage`. En la web se añaden el área `/app` y el manifest de la PWA.
  - Registro por invitación con `POST /api/members/register`. El registro no devuelve tokens, así que después hay que hacer login.
  - Login, "olvidé mi contraseña", bootstrap con `/api/auth/me` y branding del gym.
  - En la web, la pantalla de registro acepta el código en la URL (`/app/registro?code=…`), para poder mandarlo por WhatsApp.
- **F7.2 — Inicio.**
  - Ficha propia (`GET /api/members/{id}`) con la membresía vigente.
  - **Mi QR**, para mostrarlo en recepción.
  - Check-in propio: `POST /api/attendance/checkin` sin cuerpo, solo si `CheckinQr` está activo.
  - Racha y congeladores (`/api/me/streak`) e historial de asistencia.
- **F7.3 — Logros.** La lista de logros (`/api/me/achievements`) y una pantalla de celebración con los `unlockedAchievements` que devuelve el check-in. Solo si `ShowGamification` está activo.
- **F7.4 — Rutina.** La rutina y su historial (`/api/me/routines`) y el registro por ejercicio (`POST`/`DELETE /api/routine-exercises/{id}/log`). Crear una rutina propia depende de H2 y H4.
- **F7.5 — Clases.**
  - Horario (`/api/me/classes`), con `myBookingStatus`, e historial.
  - Reservar y cancelar en `/api/classes/{id}/sessions/{date}/book`. Un 409 significa cupo lleno. Un 422 significa que la clase no tiene cupo y no admite reservas.

Queda fuera:
- Push: FCM no está implementado en el backend. Cuando llegue, será solo para el host móvil.
- Todo lo que depende de Community o de Progress.

CORS: `/app` comparte origen con el panel, así que no hay que añadir nada. MAUI Blazor Hybrid llama a la API desde código nativo (`HttpClient`) y no desde un navegador, de modo que **no necesita CORS**.

### F8 — Panel SuperAdmin (en `GSM.Web`, `/admin`)
Sub-etapas:
- **F8.1 — Cartera y ficha.**
  - Cartera con el estado efectivo de cada gym (`GET /api/admin/gyms?status=`).
  - Ficha del gym con suscripción, pagos y uso (`GET /api/admin/gyms/{id}`).
  - Alta de un gym junto con su owner (`POST /api/gyms`). Un 409 significa que el slug ya existe.
- **F8.2 — Operaciones.**
  - Editar plan y precio (`PUT …/subscription`).
  - Registrar un pago (`POST …/payments`), que renueva la suscripción y devuelve el gym a `Active`.
  - Renovar sin pago.
  - Suspender con motivo y reactivar.
  - Cobros del periodo (`/api/admin/payments?from&to`).
- **F8.3 — Panel de plataforma** (`/api/admin/dashboard`): cartera por estado, contratado frente a cobrado, uso por gym y totales.
- **F8.4 — Impersonación.** Se abre con un motivo (`POST /api/admin/impersonate/{gymId}`). El cliente pasa a usar los tokens de impersonación y abre el panel del gym en solo lectura. Al terminar (`/impersonate/end`) vuelve al panel SuperAdmin. Incluye el historial de auditoría (`/api/admin/impersonations`).
- **Catálogo de ejercicios del sistema:** lo edita el superadmin, reutilizando la pantalla de F6.

### Despliegue
Va de la mano con la etapa 8.4 del backend (Railway), que sigue pendiente.
- `GSM.Web` se publica como archivos estáticos. Los puede servir la propia API o un hosting estático; se decide junto con el Dockerfile.
- La app MAUI se publica en las tiendas después del piloto.

## Ficheros clave
- **Nuevos:** `src/Frontend/GSM.Frontend.sln`, `src/Frontend/GSM.Client/`, `src/Frontend/GSM.UI.Shared/`, `src/Frontend/GSM.Member.UI/`, `src/Frontend/GSM.Web/`, `src/Frontend/GSM.Mobile/`, `tests/GSM.Web.Tests/`, `scripts/generate-client.ps1` y `.claude/launch.json`.
- **Fuente de verdad (se consultan, no se tocan):**
  - `GSM.API/Program.cs`: CORS, JSON y OpenAPI.
  - `GSM.API/Auth/AuthSetup.cs`: las políticas que replica el cliente.
  - `GSM.API/Filters/SubscriptionGateFilter.cs`.
  - `GSM.Application/Common/PagedResult.cs` y `SubscriptionInfo.cs`.
  - `GSM.Application/*/Contracts/*Contracts.cs`.
- **Backend:** **nada en F0–F4**. Solo se tocan los huecos H1–H4, cada uno con confirmación en su etapa. Si otra pantalla necesita un endpoint que no existe, se para y se decide.
- **`CLAUDE.md`:** al cerrar cada etapa se actualiza "Estado actual", igual que se hizo con el backend.

## Verificación
- `dotnet build` de las soluciones y `dotnet test`: los 111 tests de integración siguen en verde, y además pasan los de bUnit.
- En cada etapa se levantan la API (`dotnet run` en `GSM.API`) y `GSM.Web` (con `preview_start`), se recorre el flujo en el navegador con los usuarios del seeder y se comprueba que la consola y la red no muestran errores.
- Casos que se prueban siempre:
  - Un 422 muestra el `detail`.
  - Un 400 marca los campos.
  - Una sesión caducada se refresca.
  - El staff no ve la configuración.
  - Un gym bloqueado ve la pantalla de bloqueo.
  - La impersonación no deja escribir.
- La app del socio se prueba **con el mismo guion en los dos hosts**, que en la práctica son tres entornos:
  - el navegador del PC (`https://localhost:7220/app`);
  - un navegador móvil emulado (`resize_window` con el preset mobile);
  - el emulador de Android contra la API local (`10.0.2.2:5175`).

  El guion es: registro con invitación, login, QR, check-in, racha y reserva de una clase. El resultado tiene que ser el mismo en los tres.
