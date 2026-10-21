# Logs y errores — especificación

> **Estado:** construido en la v2 (rama `v2`, verificado con tests el 2026-10-20). Describe el diseño vigente; lo que quedó sin hacer o sin verificar está en `V2-PENDIENTES.md`. Las frases «hoy…» que contrastan con lo anterior describen la v1, previa a esta versión.

**Schema:** `logs` · **Lo escriben:** todos los módulos, la API, los procesos en segundo plano y los
clientes (web y app) · **Lo lee:** solo el SuperAdmin

## 1. Propósito

**Poder identificar cualquier falla** del sistema, en especial las críticas, y que quede guardada en
tablas consultables. Hoy los errores solo van al log de texto de Serilog: un 500 se escribe, pero nadie
se entera, no se puede buscar por gym ni agrupar. Y hay fallas **silenciosas por diseño**: un manejador
de eventos que falla "se loguea y no tumba la operación" (por ejemplo, la suscripción de un gym nuevo
podría no crearse sin que nadie lo note).

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Alcance | **Todo**: fallas reales **y también los rechazos de negocio** (400, 403, 404, 409, 422) (§4.2). |
| Revisión | **Panel en `/admin` con agrupación y alertas**, y reintento de eventos fallidos (§7, §8). |
| Retención | **1 año para todo** (§9). |
| Rendimiento | Se guardan también las **peticiones lentas** (más de 2 segundos) (§4.8). |

## 3. Reglas generales

- **El log nunca tumba la operación.** Si escribir el log falla, la petición sigue.
- **Se escribe fuera de la transacción del caso de uso.** Un error que hace rollback se llevaría consigo
  su propio registro si se escribiera dentro.
- **Si la base no responde**, el registro cae al log de archivo de Serilog, que se mantiene.
- **Nunca se guardan** contraseñas, tokens, códigos (de invitación, de acceso, de restablecimiento,
  del kiosco), firmas del QR ni **datos de salud**. La limpieza es por lista de nombres de campo
  (`password`, `token`, `code`, `secret`, `medical*`, `medications`, `trainingRestrictions`…) y se
  aplica antes de guardar.
- **Código de rastreo:** toda petición tiene un `TraceId`. Las respuestas de error (`ProblemDetails`)
  lo incluyen, y el frontend lo muestra: "Algo salió mal · código 7F3A2C". Con ese código se encuentra
  el registro exacto en cualquier tabla.
- **Las tablas no son de ningún gym:** no implementan `ITenantEntity` ni pasan por el Global Query
  Filter. Guardan `GymId` como dato, para filtrar, y solo el SuperAdmin las consulta.
- **No reemplaza a las auditorías de negocio**, que siguen en su módulo: historial de cambios del
  miembro, consultas de salud, impersonación, pagos inmutables.

## 4. Tablas

Columnas comunes a todas: `Id`, `OccurredAt`, `TraceId`, `GymId` (nulo si no aplica), `UserId`,
`Role`, `Environment`, `AppVersion`.

### 4.1 `logs.errors` — errores no controlados

Todo lo que termina en **500**, y cualquier excepción no controlada fuera de una petición (procesos en
segundo plano).

| Campo | Notas |
|---|---|
| `Source` | `api`, `dispatcher`, `job` |
| `Method`, `Path`, `Endpoint` | Endpoint con su plantilla (`/api/members/{id}`), además de la ruta real. |
| `ExceptionType`, `Message`, `StackTrace`, `InnerExceptions` | |
| `RequestSummary` | Parámetros de ruta y de consulta, y el cuerpo **limpio y recortado a 4 KB**. |
| `Fingerprint` | Para agrupar (§7). |

### 4.2 `logs.business_rejections` — rechazos de negocio

Toda respuesta **400, 403, 404, 409 o 422**. No son fallas del sistema: son las reglas funcionando.
Sirven para ver **qué reglas frenan más a los usuarios** ("Cupo lleno: 340 veces este mes").

| Campo | Notas |
|---|---|
| `Method`, `Endpoint`, `StatusCode` | |
| `ExceptionType` | `ValidationDomainException`, `ConflictException`… o "validación de formulario" para los 400. |
| `Title`, `Detail` | El mensaje que recibió el usuario. |
| `FieldErrors` | Solo en 400: qué campos fallaron (los nombres, no los valores). |

- **Volumen alto:** es la tabla que más crece. Va particionada por mes (§9).
- Los 401 y los 403 por permisos van **además** a `security_events`.

### 4.3 `logs.event_handler_failures` — eventos que fallaron

Cada vez que un manejador de un evento de dominio falla.

| Campo | Notas |
|---|---|
| `EventType`, `HandlerType` | Por ejemplo `GymCreated` → `CreateSubscriptionOnGymCreated`. |
| `Payload` | El evento completo, en JSON, **para poder reintentarlo**. Limpio según §3. Si el evento lleva un dato limpiado que el reintento necesita (p. ej. el código de `MemberInvited`), el reintento no usa el payload para ese dato: lo pide de nuevo al módulo dueño (un código nuevo). Decidido 2026-10-11. Tope de 64 KB; por encima se guarda `{"_truncated":true}` (nunca un JSON cortado). |
| `ExceptionType`, `Message`, `StackTrace` | |
| `RetryStatus` | `pending`, `retried_ok`, `retried_failed`, `ignored`. |
| `RetriedAt`, `RetriedBy` | |

**Reintentar** (desde el panel) vuelve a ejecutar **ese manejador** con ese evento. Exige que los
manejadores sean idempotentes; la mayoría ya lo son por sus índices únicos, y la construcción de cada
manejador nuevo debe asegurarlo.

### 4.4 `logs.job_runs` — ejecuciones de tareas

Cada ejecución de la tarea periódica (`/internal/jobs/…`) y de cada vuelta del dispatcher de avisos
(`GSM-modulo-notifications.md` §5).

| Campo | Notas |
|---|---|
| `Job` | `communications`, `notifications_dispatcher`, `anonymization`, `monthly_metrics`… |
| `StartedAt`, `FinishedAt`, `Status` | `ok`, `partial` (algún elemento falló), `failed`. |
| `ItemsProcessed`, `ItemsFailed` | |
| `Error` | Si falló entera. |

Las vueltas del dispatcher **sin nada que hacer no se guardan** (serían 2.880 filas vacías al día).
Basta con un registro de "latido" (§8).

### 4.5 `logs.external_failures` — servicios externos

Cada llamada fallida a Resend (correo), Cloudflare R2 (archivos) o Firebase (push).

| Campo | Notas |
|---|---|
| `Provider`, `Operation` | `resend` / `send_email`, `r2` / `upload`, `fcm` / `send`. |
| `StatusCode`, `ResponseBody` | Recortada; sin datos personales del destinatario más allá de su id. |
| `DurationMs`, `Attempt` | |

### 4.6 `logs.client_errors` — errores del navegador y de la app

| Campo | Notas |
|---|---|
| `Platform` | `web_panel`, `web_member`, `android`, `ios`. |
| `Route` | Pantalla donde pasó. |
| `ExceptionType`, `Message`, `StackTrace` | |
| `DeviceInfo` | Navegador o modelo y versión del sistema. |

- Los envía el frontend a `POST /api/logs/client`: los errores no controlados de Blazor (el
  `ErrorBoundary` y el manejador global) y los cierres inesperados de la app nativa.
- Acepta envíos sin sesión (un error en la pantalla de login también cuenta), con límite de envíos por
  IP para que nadie llene la tabla.

### 4.7 `logs.security_events` — seguridad

| Evento | Detalle |
|---|---|
| `login_failed` | Email intentado (sin la contraseña), IP. |
| `account_locked` | Tras 5 intentos (`GSM-modulo-identity.md` §7). |
| `invalid_token` | 401: token vencido, mal firmado o revocado. |
| `permission_denied` | 403 por rol o permiso. |
| `rate_limited` | Se alcanzó un límite de peticiones. |
| `kiosk_pairing_failed`, `document_entry_locked` | Código de kiosco inválido; teclado del kiosco bloqueado tras 5 intentos. |
| `code_attempts_exceeded` | Código de acceso del miembro con 5 intentos fallidos. |

Campos: `EventType`, `Ip`, `UserAgent`, `Detail`.

### 4.8 `logs.slow_requests` — peticiones lentas

Toda petición que tarda **más de 2 segundos**: `Method`, `Endpoint`, `DurationMs`, `StatusCode`. El
umbral es configurable (`Logs:SlowRequestMs`).

## 5. Cómo se escribe

- Un servicio transversal, **`ILogStore`**, en `Application/Common`, con un método por tabla. Lo
  implementa Infrastructure con **su propio `DbContext` y su propia conexión**: nunca comparte la
  transacción del caso de uso.
- **Las escrituras se encolan en memoria** y un escritor en segundo plano las guarda en bloque cada
  segundo (o cada 100 registros). La petición no espera al log.
  - Riesgo aceptado: si la API se cae de golpe, se pierden los registros del último segundo. El log de
    archivo de Serilog los conserva.
- **Dónde se engancha cada tabla:**

| Tabla | Punto de enganche |
|---|---|
| `errors`, `business_rejections` | El middleware global de excepciones que ya existe, y el `ValidationActionFilter` (400) |
| `security_events` | El login, el lockout, la autenticación JWT y la autorización |
| `event_handler_failures` | `DomainEventPublisher`, donde hoy se loguea el fallo de un manejador |
| `job_runs` | El endpoint de la tarea y el dispatcher |
| `external_failures` | Los clientes de Resend, R2 y FCM en Infrastructure |
| `slow_requests` | Un middleware que mide la duración |
| `client_errors` | `POST /api/logs/client` |

## 6. Latido de los procesos

Una falla grave es que un proceso **deje de correr sin dar error**. Tabla `logs.heartbeats`
(`Process`, `LastBeatAt`): el dispatcher la actualiza en cada vuelta y la tarea periódica en cada
ejecución. Si un latido pasa de su plazo (dispatcher: 5 minutos; tarea: 30 minutos), es una alerta
(§8).

## 7. Panel de errores (SuperAdmin)

Sección **"Errores"** en `/admin`:

- **Agrupación:** los registros iguales se agrupan por huella (`Fingerprint`):
  - errores: tipo de excepción + primer punto del código propio en la traza;
  - rechazos: endpoint + código + título;
  - eventos fallidos: evento + manejador.
  Tabla `logs.error_groups`: huella, tipo, título, primera y última vez, cantidad, gyms afectados,
  **estado** (`new`, `investigating`, `resolved`, `ignored`) y notas.
- **Vistas:**
  - **Fallas**: errores, eventos fallidos, servicios externos, errores del cliente. Ordenadas por
    "nuevas" y "más frecuentes".
  - **Rechazos de negocio**: las reglas que más frenan, por gym y por mes.
  - **Seguridad**: logins fallidos, bloqueos, accesos denegados, por IP y por cuenta.
  - **Lentitud**: endpoints más lentos.
  - **Procesos**: ejecuciones de tareas y estado de los latidos.
- **Detalle de un grupo:** cada ocurrencia con su código de rastreo, gym, usuario, traza y petición.
- **Búsqueda por código de rastreo:** el usuario dice "me salió el código 7F3A2C" y se encuentra al
  instante. La búsqueda es por prefijo de 6 caracteres y puede devolver más de uno: se distinguen por
  fecha y gym (aceptado 2026-10-11).
- **Reintentar** un evento fallido, o todos los de un grupo.
- Un grupo **resuelto que vuelve a ocurrir** se reabre solo.

## 8. Alertas al SuperAdmin

Por push y correo (Notifications), **solo al SuperAdmin**:

| Alerta | Cuándo |
|---|---|
| Error nuevo | Primera ocurrencia de un grupo de `errors` o `event_handler_failures` |
| Pico | Un grupo con más de 20 ocurrencias en 10 minutos |
| Proceso detenido | Un latido vencido (§6) |
| Servicio externo caído | 5 fallas seguidas del mismo proveedor |
| Seguridad | 20 logins fallidos desde una misma IP en 10 minutos |

Los rechazos de negocio, la lentitud y los errores del cliente **no generan alerta**: se revisan en el
panel. Las alertas se agrupan para no repetir la misma más de una vez cada 30 minutos.

## 9. Retención

- **1 año para todo** (decidido 2026-10-07). Lo borra la tarea periódica una vez al día.
- Las tablas grandes (`business_rejections`, `security_events`, `slow_requests`, `errors`) van
  **particionadas por mes** en Postgres: borrar un mes viejo es soltar su partición, no recorrer la
  tabla.
- `logs.error_groups` se conserva aunque sus ocurrencias se borren (mantiene la historia: "esto pasó
  por primera vez en marzo").
- Los registros tienen ids de usuario y de gym, no nombres. Al anonimizar a un miembro
  (`GSM-modulo-members.md` §11) no hace falta tocar los logs: los ids ya no identifican a nadie.

## 10. Lo que cambia en el resto

- **Middleware de excepciones y `ValidationActionFilter`:** además de responder, registran; el
  `ProblemDetails` lleva el `traceId`.
- **`DomainEventPublisher`:** guarda el fallo con el evento para reintentarlo.
- **Frontend (web y app):** muestra el código de rastreo en los errores y reporta sus propios errores.
- **Notifications:** alertas del §8 al SuperAdmin.
- **SuperAdmin:** sección "Errores".

## 11. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **LG1** | Schema `logs`, `ILogStore` con escritor en segundo plano, código de rastreo en las respuestas, `errors`, `event_handler_failures`, `job_runs`, latidos y purga. **Conviene hacerla al empezar el desarrollo**, para que todo lo nuevo nazca observable. |
| **LG2** | `business_rejections` (particionada), `security_events`, `slow_requests`. |
| **LG3** | `external_failures` y `client_errors` (con el reporte desde web y app). |
| **LG4** | Panel de errores: agrupación, estados, búsqueda por código, reintento de eventos. |
| **LG5** | Alertas al SuperAdmin (depende de Notifications). |
