# Plan de construcción — Módulo Comunicaciones (etapa 13)

> **Estado: completado (2026-10-06).** Las sub-etapas 13.1 a 13.6 están construidas y validadas: 156 tests de integración y 68 de bUnit en verde, y el panel y la app del miembro verificados en el navegador (escritorio y 375 px).
>
> **Desviaciones respecto a este plan:**
> - Los ajustes del recordatorio viven en `/api/communications/reminder-settings`, no en `/api/communications/settings`: dos rutas que terminan en `settings` hacían que NSwag renombrara los métodos de los ajustes de check-in.
> - La baja por enlace no es "de un solo uso": es **idempotente** (pulsar el enlace dos veces no da error ni cambia nada), que cumple el mismo objetivo sin guardar tokens usados. El token no caduca, para que un correo antiguo siga sirviendo para darse de baja.
> - La preferencia de email del miembro está en la sección "Para ti" de Novedades, no en la ficha (`/app`), que otra sesión estaba rediseñando.
> - `AnnouncementResponse.Delivery` (estado de los correos) se rellena en el listado, el detalle y al publicar; en editar, archivar y fijar vuelve nulo y el panel recarga la lista.
> - La referencia de un aviso de clase cancelada es un Guid derivado de `(clase, día)`, porque `ClassSessionCancelled` no trae el id de la sesión. La **especificación funcional** está en `docs/GSM-modulo-comunicaciones.md`; este documento dice en qué orden se construye. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`: **si algo aquí los contradice, ellos mandan.**
>
> **Ritmo:** una sub-etapa por vez, **sin empezar la siguiente sin confirmación explícita del usuario**. Cada una termina compilando, con su migración aplicada, sus endpoints respondiendo y la suite completa en verde.

---

## Punto de partida

Las etapas 0–7, 9, 10, 11 y 12 (check-in con QR del gym) están hechas; no hay dependencia entre la 12 y este módulo. Comprueba el estado real en `CLAUDE.md` antes de empezar.

Convenciones que este módulo hereda **sin excepción** (lee `CLAUDE.md` entero):

- `IClock` para toda lectura de tiempo: `Today` para días de negocio y `UtcNow` para instantes. **`EndDate` de una membresía es inclusive.**
- Un caso de uso por clase, con `HandleAsync`, inyectado directo en el controller.
- **Entre módulos, solo `Public/I{Módulo}Reader` o eventos.** Este módulo **no añade escrituras cruzadas**.
- **Enums de Postgres en tres sitios:** `GsmDbContext`, `DependencyInjection` y `GsmDbContextFactory`. Si tienen valores de varias palabras, `SnakeCaseNameTranslator` con el miembro C# en PascalCase.
- **Errores:** 422 invariante, 409 conflicto, 403 prohibido, 404 no encontrado, 400 forma del request.
- **Migraciones:** `Etapa13_{n}_{Nombre}`. Se aplican a mano (`cd src/Backend && dotnet ef database update --project GSM.Infrastructure`, **sin** `--startup-project`).
- **Toda entidad de negocio implementa `ITenantEntity`.** El proceso multi-gym de 13.4 es la única ruta que ignora el filtro, y lo hace explícitamente.
- **Mapeo:** a mano, como `RoutineMapper` (los DTOs del feed son compuestos). Las consultas de listados usan proyección con `Select`.
- **Tests de integración** con `MutableClock`. Para HTTP con la identidad real, el patrón de `SubscriptionGateHttpTests`. **No modifiques datos sembrados por migraciones.**

**Entorno:** el mismo que describe `docs/CHECKIN-QR-BUILD-PLAN.md`, sección "Entorno":
- no hay `pwsh`, así que el cliente NSwag se regenera a mano y hay que reaplicar el parche de `DaysOfWeek`;
- la API se usa por HTTPS (7219);
- muchos archivos usan CRLF;
- los servidores se arrancan con las herramientas de vista previa.

**Frontend:** la identidad visual es **un solo tema, Forja**, más la marca del gym (`GymBrand`, `GsmThemeProvider`). Usa los tokens `--gsm-*` y los componentes `PageHeader`, `gsm-panel` y `StatCard`. **Ningún color va a mano.**

---

# 13.1 — Anuncios (backend)

**Objetivo:** Owner y Staff crean, editan, publican, fijan y archivan anuncios; el miembro los lee.

### Trabajo
1. **Domain** (`GSM.Domain/Communications/`)
   - `Announcement` (`ITenantEntity`) con su enum `AnnouncementStatus` (`Draft`, `Published`, `Archived`).
   - Factory `CreateDraft(gymId, authorUserId, title, body, pinned, expiresOn, sendEmail)` y métodos `Edit(...)`, `Publish(now)`, `Archive()`, `Pin()` y `Unpin()`.
   - Invariantes: título de 3–120 caracteres y cuerpo de 1–4000 (`Trim`); no se edita un archivado; no se publica dos veces; `expiresOn`, si existe, debe ser hoy o posterior al publicar.
2. **Infrastructure**
   - Schema `communications`, tabla `announcements` e índice `(gym_id, status, published_at)`.
   - Enum `communications.announcement_status`.
   - Repositorio.
3. **Application** (`GSM.Application/Communications/`): contratos, validadores y casos de uso:
   - `ListAnnouncementsHandler` (panel: todos los estados, paginado);
   - `GetAnnouncementHandler`;
   - `CreateAnnouncementHandler`, `UpdateAnnouncementHandler`, `PublishAnnouncementHandler`, `ArchiveAnnouncementHandler`;
   - `PinAnnouncementHandler`: 409 si ya hay 3 fijados en el gym;
   - `GetMemberFeedHandler`: solo publicados y no caducados (`expiresOn >= Today` o nulo), los fijados primero y luego por `published_at` descendente, paginado; respeta `ShowCommunity` (si está apagado, 403).

   `PublishAnnouncementHandler` publica `AnnouncementPublished` después del commit. El encolado de emails llega en 13.4: deja el punto preparado y un `TODO` que apunte a 13.4.
4. **API**

   | Método | Ruta | Política |
   |---|---|---|
   | GET | `/api/announcements?status=&page=&pageSize=` | RequireStaff |
   | GET | `/api/announcements/{id}` | RequireStaff |
   | POST | `/api/announcements` | RequireStaff |
   | PUT | `/api/announcements/{id}` | RequireStaff |
   | POST | `/api/announcements/{id}/publish` | RequireStaff |
   | POST | `/api/announcements/{id}/archive` | RequireStaff |
   | POST | `/api/announcements/{id}/pin` | RequireStaff |
   | POST | `/api/announcements/{id}/unpin` | RequireStaff |
   | GET | `/api/me/feed?page=&pageSize=` | RequireMember |

### Migración
`Etapa13_1_Announcements`.

### Aceptación (tests)
- Crear con un título de 2 caracteres → 400. Publicar dos veces → 409. Editar un archivado → 409.
- Fijar un cuarto anuncio → 409.
- El feed del miembro no muestra borradores, archivados ni caducados, y ordena los fijados primero.
- Con `ShowCommunity` apagado, el feed responde 403.
- Aislamiento entre gyms en el panel y en el feed.
- Suite completa en verde.

---

# 13.2 — Reacciones y estado de lectura (backend)

### Trabajo
1. **Domain:**
   - `AnnouncementReaction` (`ITenantEntity`): `AnnouncementId`, `MemberId`, `Kind` (`Like`, `Celebrate`, `Strong`) y `ReactedAt`; índice único `(announcement_id, member_id)`.
   - `MemberFeedState` (`ITenantEntity`): `MemberId` (único) y `AnnouncementsSeenAt`.
2. **Application:**
   - `ReactToAnnouncementHandler`: crear, cambiar o quitar según §3.2 de la especificación (con la misma reacción, la quita). Solo sobre anuncios publicados y no caducados (404 si no). Respeta `ShowCommunity`.
   - El feed (13.1) añade a cada anuncio los totales por tipo (proyección agregada, sin cargar las reacciones en memoria) y la reacción propia del miembro.
   - `MarkAnnouncementsSeenHandler` y el cálculo de no leídos de anuncios (`published_at > AnnouncementsSeenAt`).
   - El panel ve los totales por tipo en el listado y en el detalle.
3. **API**

   | Método | Ruta | Política |
   |---|---|---|
   | PUT | `/api/me/feed/{announcementId}/reaction` (cuerpo `{ kind }`) | RequireMember |
   | DELETE | `/api/me/feed/{announcementId}/reaction` | RequireMember |
   | POST | `/api/me/feed/seen` | RequireMember |
   | GET | `/api/me/feed/unread-count` | RequireMember |

   `unread-count` devuelve `{ announcements, notifications }`; las notificaciones valen 0 hasta 13.3.

### Migración
`Etapa13_2_ReactionsAndFeedState`: enum `communications.reaction_kind`.

### Aceptación
- Reaccionar `like` y después `strong` deja una sola fila con `strong`. Repetir `strong` la quita.
- Los totales del feed y del panel coinciden con las filas.
- El miembro no puede ver quién reaccionó: ningún endpoint de miembro devuelve identidades.
- `seen` pone a cero los anuncios no leídos, y un anuncio publicado después vuelve a contar.
- Suite completa en verde.

---

# 13.3 — Notificaciones por evento (backend)

### Trabajo
1. **Domain:**
   - `MemberNotification` (`ITenantEntity`): `MemberId`, `Type` (`ClassCancelled`, `MembershipExpiring`, `AchievementUnlocked`), `ReferenceId`, `Title`, `Body`, `Link`, `CreatedAt` y `ReadAt`.
   - Índice único `(member_id, type, reference_id)`, que da la idempotencia.
   - `MarkRead(now)`.
2. **Handlers** (en `Communications/Events/`):
   - `ClassSessionCancelledNotifiesMembersHandler`: una notificación por cada `AffectedMemberIds` activo. El título y el cuerpo incluyen el nombre de la clase (léelo por un reader de Classes; si no existe un `Public/IClassReader`, créalo **solo de lectura**) y la fecha con `GsmFormat`-equivalente en backend (`d MMM`, `es-CO`).
   - `AchievementUnlockedNotifiesMemberHandler`: "Desbloqueaste «{logro}»".
   - Ambos son idempotentes: si la fila ya existe por la clave única, se ignora. Captura el conflicto de unicidad o comprueba antes de insertar.
   - Un handler que falla se loguea y no tumba al publicador (regla del despachador).
3. **Application:** `ListMyNotificationsHandler` (paginado, las no leídas primero), `MarkNotificationReadHandler` y `MarkAllNotificationsReadHandler`. Las notificaciones se suman a `unread-count`.
4. **API**

   | Método | Ruta | Política |
   |---|---|---|
   | GET | `/api/me/notifications` | RequireMember |
   | POST | `/api/me/notifications/{id}/read` | RequireMember |
   | POST | `/api/me/notifications/read-all` | RequireMember |

### Migración
`Etapa13_3_MemberNotifications`: enum `communications.notification_type` con `SnakeCaseNameTranslator`.

### Aceptación
- Cancelar una sesión con 3 reservas crea 3 notificaciones. Si el evento llega dos veces, siguen siendo 3.
- Desbloquear un logro al hacer check-in crea su notificación.
- `read-all` vacía el contador.
- Suite completa en verde.

---

# 13.4 — Email: cola de envío, preferencias y proceso programado (backend)

### Trabajo
1. **Domain:**
   - `EmailOutboxItem` (`ITenantEntity`): `MemberId`, `ToAddress`, `Subject`, `Body`, `Kind` (anuncio o tipo de notificación), `ReferenceId`, `Status` (`Pending`, `Sent`, `Failed`), `Attempts`, `LastError`, `CreatedAt` y `SentAt`.
   - Índice único `(member_id, kind, reference_id)`, para no enviar dos veces el mismo correo.
   - `MemberCommunicationPreferences` (`ITenantEntity`): `MemberId` (único) y `EmailOptOut`.
2. **Encolado:**
   - `PublishAnnouncementHandler` con `send_email` encola un correo por cada miembro **activo, con email y sin baja** (método nuevo en `IMemberReader` que devuelva esos contactos para un gym).
   - El handler de clase cancelada también encola; el de logro **no** (ver la especificación, §3.3).
   - Todo se encola en la misma transacción que la escritura que lo origina, o, en los handlers, en su propia transacción. **Nunca se envía dentro de la petición.**
3. **Baja sin sesión:**
   - Token firmado con HMAC y una clave `Communications:UnsubscribeSigningKey` (opciones validadas al arrancar, ≥ 32 caracteres, igual que `Checkin:SigningKey`).
   - Endpoint `GET /api/public/unsubscribe?t=` (anónimo, con limitador de peticiones) que marca `EmailOptOut` y devuelve una página o un JSON simple.
   - El enlace va al pie de cada correo de comunicaciones.
4. **Preferencia desde la app:** `GET` y `PUT /api/me/communication-preferences` (RequireMember).
5. **Recordatorio de vencimiento:**
   - ajuste por gym `ReminderDaysBefore` (0–14, por defecto 3, 0 = desactivado), guardado en `communications.settings`, perezoso como los ajustes de check-in: sin fila, valores por defecto;
   - endpoints `GET/PUT /api/communications/settings` (GET RequireStaff, PUT RequireOwner).
6. **Proceso programado:**
   - `POST /internal/jobs/communications/run`, autenticado por la cabecera `X-Job-Secret` contra `Jobs:Secret` (comparación en tiempo constante). Sin JWT, con `[ApiExplorerSettings(IgnoreApi = true)]` y sin pasar por el filtro de bloqueo por impago (no es una petición de un gym).
   - `RunCommunicationsJobHandler`, ignorando el Global Query Filter **solo aquí**, en tres pasos:
     1. **Recordatorios:** para cada gym con `ReminderDaysBefore > 0`, las membresías activas con `EndDate` entre `Today` y `Today + ReminderDaysBefore` (inclusive) y sin notificación previa crean una `MemberNotification` (`MembershipExpiring`) y encolan su email.
     2. **Cola:** toma hasta N pendientes (configurable, por defecto 50) en orden de creación; envía con `IEmailSender`; marca `Sent` o incrementa `Attempts` (a los 3, `Failed`). Respeta una pausa entre envíos si el remitente es Resend.
     3. **Purga:** borra las notificaciones de más de 90 días.
   - Devuelve un resumen (`{ remindersCreated, emailsSent, emailsFailed, notificationsPurged }`) y lo loguea.
7. **Panel:** el detalle de un anuncio enviado por email muestra enviados, pendientes y fallidos (proyección sobre el outbox).

### Migración
`Etapa13_4_EmailOutboxAndPreferences`: enums `communications.email_status` y `communications.email_kind`, y tablas `email_outbox`, `member_communication_preferences` y `settings`.

### Configuración
- `Jobs:Secret` y `Communications:UnsubscribeSigningKey`: vacías en `appsettings.json`, con valores de desarrollo en `appsettings.Development.json`, y la API no arranca si faltan. En la factory de tests, valores de prueba.
- Añádelas a la tabla de variables de `docs/DEPLOY-DOKPLOY-PLAN.md` (fase D3) y a "Pendiente de configurar" del README.
- **Dokploy:** documenta en el runbook la tarea programada (cada 5–10 min) que llama al endpoint con la cabecera.

### Aceptación (tests; con `CapturingEmailSender`, que ya existe en los tests)
- Publicar con email a un gym con 5 miembros activos, 1 sin email, 1 con baja y 1 inactivo encola exactamente 2.
- Publicar sin `send_email` no encola nada.
- Ejecutar el proceso envía los pendientes y los marca como `Sent`. Ejecutarlo otra vez no reenvía.
- Un fallo del remitente reintenta, y al tercer fallo queda `Failed`.
- Recordatorios: con 3 días de antelación, una membresía que termina en 2 días genera el recordatorio. Ejecutar dos veces no lo duplica. Con `ReminderDaysBefore = 0`, no hay recordatorios.
- Sin la cabecera o con un secreto incorrecto → 401.
- El enlace de baja marca `EmailOptOut`. Un token manipulado → 400 o 422 (elige uno y fíjalo en el test).
- La purga borra las notificaciones de 91 días y conserva las de 89.
- Suite completa en verde.

---

# 13.5 — Panel del gym (frontend)

### Trabajo
1. **Regenerar el cliente** (pasos manuales en la sección Entorno de `docs/CHECKIN-QR-BUILD-PLAN.md`) y revisar el diff. Ojo con las **colecciones de enum** en las respuestas (los totales por tipo de reacción): es el defecto conocido de NSwag.
2. **`/communications`** (RequireStaff), en el grupo "Negocio" del menú:
   - `PageHeader` con "Nuevo anuncio";
   - lista con estado, fecha, fijado, totales de reacciones con icono y, si se envió por email, enviados y fallidos.
3. **Editor** (página o diálogo):
   - título, cuerpo (con contador de caracteres), fijar, caducidad (`MudDatePicker`) y "Enviar también por email" con el número de destinatarios;
   - vista previa tal como se verá en la app;
   - botones "Guardar borrador" y "Publicar". Publicar pide confirmación si va por email ("Se enviará a 28 miembros. Los correos no se pueden retirar.").
4. **Configuración**, pestaña "Comunicaciones" (solo Owner): días de antelación del recordatorio.

### Aceptación
- Verificado en el navegador, en escritorio y a 375 px: crear, publicar con email (la cola se ve en el log, con `LoggingEmailSender`), fijar, intentar fijar un cuarto (mensaje claro del 409) y archivar.
- Sin errores en la consola. Suite bUnit en verde.

---

# 13.6 — App del miembro (frontend)

### Trabajo
1. **Pestaña "Novedades"** en `MemberLayout` (quinto destino, con su icono), visible solo si `ShowCommunity`, con un **indicador de no leídos** (consulta `unread-count` al entrar y cada 60 s; el temporizador se libera en `Dispose`) y un nombre accesible ("Novedades, 3 sin leer").
2. **`/app/novedades`**, con dos secciones (control segmentado):
   - **Anuncios:** tarjetas con título, fecha relativa, cuerpo (respetando los saltos de línea, sin HTML) y una barra de reacciones. Las reacciones son botones con icono y contador, de al menos 44×44 px, con `aria-pressed` en la propia y actualización optimista que se revierte si la API falla.
   - **Para ti:** las notificaciones, las no leídas destacadas. Al tocar una, se marca como leída y se navega a su enlace.

   Al abrir cada sección se llama a `seen` o `read-all`, según corresponda.
3. **Preferencia de email** en la ficha del miembro (`/app`): interruptor "Recibir avisos del gym por email".
4. **En `GSM.Mobile`:** reutiliza las pantallas, porque viven en `GSM.Member.UI`. Compila `GSM.Mobile` aparte, como indica CLAUDE.md.

### Aceptación
- Verificado en el navegador a 375 px con un miembro sembrado (cuentas en la cabecera de `scripts/seed-demo.ps1`):
  - ver anuncios fijados primero, reaccionar, cambiar y quitar la reacción;
  - el contador de no leídos baja al abrir;
  - una clase cancelada desde el panel aparece en "Para ti" y su enlace lleva a `/app/clases`;
  - desactivar el email desde la app evita que se encolen nuevos correos.
- `GSM.Mobile` compila. No es verificable en un emulador en este entorno: dilo al cerrar.

---

## Al cerrar la etapa completa

1. **`CLAUDE.md`:**
   - Communications añadido a la tabla de módulos y a la lista de schemas (que pasan a once: `communications`);
   - "Community" sustituido por Communications con su alcance reducido;
   - la nota de que **existe la primera tarea periódica** (endpoint interno más tarea de Dokploy), por qué no es un scheduler interno, y que es la única ruta que ignora el Global Query Filter además de SuperAdmin;
   - las claves nuevas pendientes de configurar.
2. **Especificación técnica:** catálogo (§3), lectores públicos nuevos y eventos consumidos (`ClassSessionCancelled` y `AchievementUnlockedEvent` dejan de estar "sin consumidor"; `AnnouncementPublished` entra en esa lista).
3. **`scripts/seed-demo.ps1`:** sembrar 3–4 anuncios (uno fijado y uno caducado), algunas reacciones y un par de notificaciones, para revisar las pantallas.
4. **Este documento y la especificación:** estado "completado", con las desviaciones que haya habido.
