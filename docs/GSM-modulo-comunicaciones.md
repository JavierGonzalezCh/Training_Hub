# GSM — Módulo Comunicaciones (etapa 13)

> **Estado: construido (2026-10-06).** Decidido con el usuario el 2026-10-05.
>
> **Sustituye al módulo "Community"** del catálogo (§3 de la especificación técnica, "Feed, likes, comentarios"), **con un alcance reducido a propósito**: publica el gym y los miembros reaccionan. **Los miembros no publican ni comentan.** Una red social completa (publicaciones de miembros, comentarios, moderación) queda fuera, y se especificará aparte si algún día se pide.

---

## Replanteamiento v2 — construido

> Nada de esta sección está construido. Lo que viene después de ella describe **la v1, que es lo
> construido hoy**. Donde las dos se contradicen, manda esta sección una vez aprobada.

### Decisiones tomadas

| Tema | Decisión |
|---|---|
| Audiencia de un anuncio | **Segmentos**: todos, o por estado comercial, plan, clase o etiqueta (§A). |
| WhatsApp | **Enlaces desde el panel** con mensaje listo; el sistema no envía nada solo (§B). |
| Preferencias del miembro | **Por categoría y por canal** (§C). |
| Extras | **Imagen en anuncios**, **programar publicación**, **saludo de cumpleaños** y **horario sin avisos** (§D–§G). |

Heredadas: publicación automática en Novedades al crear una clase (`GSM-modulo-classes.md`, v2);
recordatorio de vencimiento alineado con la gracia (`GSM-modulo-memberships.md` §14); correo a los
Owners por máquina insegura (`GSM-modulo-equipment.md` §7). Los avisos **push** los entrega el módulo
nuevo Notifications (`GSM-modulo-notifications.md`); este módulo decide el contenido y la audiencia.

### A. Anuncios con audiencia

- El anuncio suma **`Audience`**: "Todos los miembros" o una combinación de filtros:
  - **estado comercial** (al día, por vencer, en gracia, vencidos, congelados, sin plan), que calcula
    Memberships;
  - **plan** (los que tienen hoy ese plan);
  - **clase** (con reserva o asistencia en esa clase en los últimos 30 días), de Classes;
  - **etiqueta** de la ficha (Members).
- Los filtros se combinan con "y": "vencidos **y** con etiqueta *natación*".
- Antes de publicar se ve **"Llegará a 84 miembros"**, con la lista si se quiere revisar.
- **Los destinatarios se fijan al publicar** (tabla `announcement_recipients`). Si un miembro renueva
  al día siguiente, el anuncio de "vencidos" que ya le llegó no desaparece de su feed, y quien se
  inscribe después no lo ve.
- El anuncio suma también **`Category`**: `gym_news` (novedades) o `promotions` (promociones). Define
  qué preferencia se respeta (§C). La publicación automática de clase nueva es `gym_news`.
- Los dados de baja nunca están en la audiencia.

### B. WhatsApp por enlace

- **Botón de WhatsApp** en la ficha del miembro, en la tarjeta de recepción y en las listas de "por
  vencer" y "vencidos". Abre `wa.me` en el WhatsApp del staff con un **mensaje listo para editar**. El
  sistema **no envía nada por su cuenta** y no tiene costo.
- **Plantillas por gym**, editables en Configuración → Comunicaciones, con variables (`{nombre}`,
  `{gym}`, `{vence}`, `{gracia_hasta}`, `{enlace}`):
  - Por vencer: "Hola {nombre}, tu plan en {gym} vence el {vence}. ¿Te lo renovamos?"
  - Vencido / en gracia: "Hola {nombre}, tu plan venció el {vence}. Si renuevas antes del
    {gracia_hasta} conservas tu fecha."
  - Invitación a la app: el enlace de registro de Identity (`GSM-modulo-identity.md` §4).
  - Cumpleaños (§F).
- **Se registra el clic** ("Ana contactó por WhatsApp · plantilla *por vencer* · hace 2 días"), y las
  listas lo muestran para no escribirle dos veces a la misma persona. El sistema no sabe si el mensaje
  realmente se envió.
- Sin teléfono válido en la ficha, el botón aparece deshabilitado con "Falta el celular".
- **Envío automático por WhatsApp Business API:** para después (exige cuenta verificada de Meta,
  plantillas aprobadas y costo por conversación).

### C. Preferencias por categoría y canal

| Categoría | Qué incluye | Push | Correo | Por defecto |
|---|---|---|---|---|
| **Mi membresía** | Por vencer, en gracia, renovación | ✓ | ✓ | Encendido |
| **Mis clases** | Recordatorio de 10 min, lista de espera, cancelaciones, penalización | ✓ | ✓ | Encendido |
| **Mi rutina** | Cambios en el plan: ejercicio reemplazado, quitado o restablecido, con motivo (Routines R12). Solo lo del entrenamiento, nada de logros | ✓ | ✓ | Encendido |
| **Mi progreso** | Logros, premios, ranking (separado de la rutina, decidido 2026-10-11) | ✓ | ✓ | Encendido |
| **Novedades del gym** | Anuncios `gym_news`, clase nueva, cumpleaños | ✓ | ✓ | Encendido |
| **Promociones** | Anuncios `promotions` | ✓ | ✓ | **Apagado** hasta que el miembro lo acepte |

- **Las notificaciones dentro de la app** (la bandeja) **siempre** reciben todo: apagar una categoría
  solo apaga su push y su correo. Así, lo de "mi membresía" y "mis clases" nunca se pierde del todo.
- **Promociones empieza apagado:** los mensajes comerciales necesitan la autorización previa del
  miembro. Se ofrece aceptarlo en el registro y en el perfil.
- **No son preferencias** (siempre se envían): los correos de seguridad y de cuenta (códigos, enlaces,
  bloqueo) y el aviso "se registró tu entrada con tu documento".
- Tabla `member_notification_preferences` (`MemberId`, `Category`, `Push`, `Email`). Reemplaza al
  interruptor único `EmailOptOut`.
- **Migración:** quien tenía `EmailOptOut = true` queda con el correo apagado en todas las categorías.
- El enlace de baja de los correos apaga **el correo de esa categoría**, no todos.
- La pantalla vive en "Mi perfil" de la app (`GSM-modulo-identity.md` §9).

### D. Imagen en anuncios

- Una imagen por anuncio (JPG, PNG o WebP, hasta 5 MB), subida a R2. Se ve en el feed, en el correo y,
  donde el teléfono lo permita, en el push.
- Depende de configurar R2.

### E. Programar la publicación

- El anuncio suma `ScheduledFor` y un estado nuevo, `scheduled`. Se puede editar o cancelar hasta que
  se publique.
- Lo publica la tarea periódica: **puede salir unos minutos después de la hora elegida**, nunca antes.
- La audiencia se calcula **al publicar**, no al programar.
- Si la hora elegida cae dentro del horario sin avisos (§G), el anuncio se publica a esa hora pero su
  push espera a que termine.

### F. Cumpleaños

- El gym lo enciende en Configuración, con su texto ("¡Feliz cumpleaños, {nombre}! Hoy tu entrenamiento
  va por cuenta de la casa 🎉"), que puede incluir un regalo.
- Se envía el día del cumpleaños **a las 8:00 a. m.**, como notificación en la app y push (categoría
  "Novedades del gym"). Los nacidos el 29 de febrero lo reciben el 28 en años no bisiestos.
- Solo a miembros activos.
- **Panel:** el dashboard muestra "Cumpleaños de hoy" y la tarjeta de recepción muestra "Hoy cumple
  años", para que el staff lo salude en persona. El botón de WhatsApp ofrece la plantilla de cumpleaños.

### G. Horario sin avisos

- Por gym, **21:00 a 7:00 por defecto** (configurable).
- Aplica a los push de **Novedades del gym, Promociones y cumpleaños**: los que caen en ese horario se
  guardan y salen al terminar.
- **No aplica** a lo que tiene hora propia o es urgente: recordatorio de clase, oferta de la lista de
  espera, avisos al staff (máquina insegura, intentos rechazados) y los de seguridad.
- Los correos de novedades y promociones también esperan.
- **Nota legal:** en Colombia, la Ley 2300 de 2023 limita los horarios y la frecuencia de los contactos
  de cobranza y publicidad. Cómo aplica a un gimnasio debe confirmarlo un abogado; este horario es una
  base, no una garantía de cumplimiento.

### H. Recordatorios de vencimiento

Se alinean con la gracia de Memberships. Categoría "Mi membresía":

1. **N días antes del vencimiento** (`ReminderDaysBefore`, 3 por defecto, como hoy).
2. **El día que vence:** "Tu plan vence hoy".
3. **El último día de gracia:** "Hoy es tu último día para renovar sin perder tu fecha".

No se envían a tiqueteras sin gracia en el paso 3. Siguen siendo idempotentes por miembro, tipo y
referencia.

### Lo que cambia en otros módulos

- **Memberships, Classes, Members:** se leen para calcular las audiencias (`IMembershipReader`,
  `IClassReader`, `IMemberReader`).
- **Notifications:** entrega los push respetando preferencias y horario sin avisos.
- **Attendance:** la tarjeta de recepción muestra el botón de WhatsApp y "Hoy cumple años".
- **Identity:** la plantilla de invitación usa su enlace; los correos de seguridad no pasan por
  preferencias.

### Sub-etapas (construidas)

| Sub-etapa | Qué |
|---|---|
| **CO1** | Preferencias por categoría y canal, con migración de `EmailOptOut`; categoría de anuncio. |
| **CO2** | Audiencia por segmentos, destinatarios fijados al publicar. |
| **CO3** | WhatsApp por enlace: botones, plantillas por gym, registro de contacto. |
| **CO4** | Imagen y programación de anuncios. |
| **CO5** | Cumpleaños. |
| **CO6** | Horario sin avisos (con Notifications). |
| **CO7** | Recordatorios de vencimiento alineados con la gracia. |

## 1. Qué resuelve

El gym necesita hablar con sus miembros: horarios de festivos, una máquina nueva, un evento, un cambio de tarifa. Hoy no hay ningún canal dentro del producto. Además, el sistema ya produce hechos que el miembro debería conocer y que nadie le cuenta: su clase se canceló, su membresía está por vencer, ganó un logro.

## 2. Alcance

| Incluido | Fuera de alcance |
|---|---|
| Anuncios que publican Owner y Staff | Publicaciones de miembros |
| Reacciones de los miembros a los anuncios | Comentarios y moderación |
| Notificaciones automáticas: clase cancelada, membresía por vencer, logro desbloqueado | Notificaciones push al teléfono (FCM/APNs) |
| Entrega dentro de la app (`/app`) y por email (Resend) | SMS o WhatsApp |
| Preferencia del miembro para no recibir emails | Segmentar anuncios por plan o por clase (posible ampliación) |

## 3. Conceptos

### 3.1 Anuncio (`Announcement`)

Mensaje del gym a todos sus miembros.

- **Campos:** título (3–120), cuerpo (1–4000, texto plano con saltos de línea; sin HTML), autor (`user_id`), estado (`draft` · `published` · `archived`), `published_at`, `pinned` (fijado arriba), `expires_on` opcional (deja de mostrarse después de ese día de negocio) y `send_email` (si al publicarlo se envía también por email).
- **Ciclo:** se crea como borrador; **publicar** fija `published_at` y, si `send_email`, encola los emails. **Publicar es irreversible en cuanto a los emails:** archivarlo lo oculta del feed, pero los correos ya enviados no se recuperan. La interfaz lo avisa antes de publicar.
- **Editar un anuncio publicado** cambia lo que se ve en la app, pero **no reenvía** emails.
- **Solo puede haber 3 anuncios fijados** a la vez por gym (409 al fijar un cuarto): fijarlo todo equivale a no fijar nada.

### 3.2 Reacción (`AnnouncementReaction`)

- Un conjunto **fijo** de reacciones, sin emojis libres: `like` (me gusta), `celebrate` (celebrar) y `strong` (fuerza). Se muestran con iconos, nunca con emojis como icono.
- **Una reacción por miembro y anuncio:** reaccionar de nuevo con otra la cambia, y con la misma la quita.
- El panel ve los totales por tipo. **El miembro ve los totales, pero no quién reaccionó.**

### 3.3 Notificación (`MemberNotification`)

Aviso personal a **un** miembro, generado por el sistema.

| Tipo | Disparador | En la app | Email |
|---|---|---|---|
| `class_cancelled` | Evento `ClassSessionCancelled` (ya existe; trae `AffectedMemberIds`) | Sí | Sí |
| `membership_expiring` | Tarea diaria: la membresía termina dentro de `ReminderDaysBefore` días (por defecto 3) | Sí | Sí |
| `achievement_unlocked` | Evento `AchievementUnlockedEvent` (ya existe) | Sí | **No**: un correo por cada logro sería ruido |

- **Campos:** tipo, título, cuerpo, enlace interno (ruta de la app, p. ej. `/app/clases`), `created_at` y `read_at`.
- **Idempotencia:** clave única por `(member_id, type, reference_id)`. `reference_id` es la sesión de clase, la membresía o el logro de miembro. Un handler o una tarea que se repite no duplica avisos.
- Las notificaciones se conservan **90 días**. La limpieza la hace la tarea diaria.

### 3.4 Preferencias del miembro

- `email_opt_out` (por defecto `false`). Lo cambia el miembro desde su app **y** desde un enlace de baja en cada email, que funciona sin iniciar sesión (token firmado, de un solo uso).
- **Los emails transaccionales no dependen de esta preferencia:** invitación, restablecer contraseña y, en el futuro, recibos. Solo afecta a los de comunicaciones.
- Un miembro **sin email** o **inactivo** no recibe emails. Un miembro inactivo tampoco recibe notificaciones nuevas.

## 4. Entrega por email

- **Cola de envío (`email_outbox`).** Los casos de uso y los handlers **encolan**: nunca envían dentro de la petición. Así publicar un anuncio para 500 miembros responde al instante, y un fallo de Resend no tumba la operación.
- **Quién vacía la cola:** el proceso programado de §5, en lotes, respetando el límite de peticiones de Resend. Cada envío pasa a `sent` o, tras 3 intentos, a `failed` con el error. Los fallidos se ven en el panel.
- **Sin Resend configurado** (como hoy en local), el remitente es `LoggingEmailSender`: la cola se vacía al log y el flujo se puede probar entero.
- **Contenido:** asunto y cuerpo en texto plano con el nombre del gym como remitente visible ("{Gym} vía GSM"), más el enlace de baja. Plantilla mínima, en español y con la voz del producto (directa, sin relleno motivacional).

## 5. Proceso programado (primera tarea periódica del sistema)

**Hasta ahora GSM no tenía scheduler**, y todo se resolvía de forma perezosa. Este módulo necesita trabajo que nadie dispara: recordatorios de vencimiento, vaciar la cola de emails y limpiar notificaciones viejas.

**Decisión:** no meter un scheduler dentro de la API (si hubiera dos réplicas, se ejecutaría dos veces). El sistema expone **un endpoint interno idempotente** y lo invoca una **tarea programada de Dokploy** (ver `docs/DEPLOY-DOKPLOY-PLAN.md`):

- `POST /internal/jobs/communications/run`, protegido por una cabecera con un secreto compartido (`Jobs:Secret`, distinto de las otras claves). No usa JWT ni rol, y **no se expone en OpenAPI**.
- Cada ejecución:
  1. genera los recordatorios `membership_expiring` pendientes de todos los gyms;
  2. vacía la cola de emails, en un lote acotado por ejecución;
  3. purga las notificaciones de más de 90 días.
- **Idempotente y tolerante a ejecuciones perdidas:** si un día no corre, al siguiente recupera los recordatorios que faltaban, siempre que la membresía no haya vencido todavía. Si corre dos veces, no duplica (clave única de §3.3 y estado del outbox).
- **Frecuencia:** cada 5–10 minutos. Los recordatorios se generan una vez por membresía, aunque la tarea corra muchas veces al día.
- **Varios gyms a la vez:** el proceso no tiene un usuario con `GymId`. Trabaja ignorando el Global Query Filter **solo dentro de este proceso** y fijando `gym_id` explícitamente en cada escritura, igual que hoy el filtro se desactiva para SuperAdmin. Cualquier otra ruta sigue filtrada.
- **En local:** se lanza a mano con `curl` (comando en el README).

## 6. Pantallas

### Panel del gym (Owner y Staff)

- **`/communications`**, nuevo ítem en el grupo "Negocio": lista de anuncios (borradores, publicados y archivados), con totales de reacciones y, si se envió por email, enviados y fallidos.
- **Editor:** título, cuerpo, fijar, caducidad y "Enviar también por email", con el número de destinatarios ("Se enviará a 28 miembros con email"). Vista previa de cómo se verá en la app.
- **Configuración** (`/settings`, solo Owner): días de antelación del recordatorio de vencimiento (0 = desactivado, 1–14, por defecto 3).

### App del miembro (`/app`)

- **Pestaña "Novedades"** en la barra inferior (pasa a 5 destinos, el máximo recomendado), con dos secciones:
  - **Anuncios:** los fijados primero y luego por fecha, con las reacciones;
  - **Para ti:** las notificaciones personales.
- **Indicador de no leídos** en la pestaña: anuncios publicados después de la última visita más notificaciones sin leer. Abrir la sección marca como leído.
- **Preferencia de email** en la ficha del miembro.
- La pestaña depende del interruptor `ShowCommunity` del gym (ya existe y hoy no controla nada); se reutiliza con el significado "mostrar comunicaciones". `CommunityComments` sigue sin uso.

## 7. Encaje en la arquitectura

- **Módulo nuevo `Communications`**, schema `communications`.
- **Lecturas** por `Public/`:
  - `IMemberReader` (nombre, email, estado; hace falta un método nuevo que devuelva los contactos de los miembros activos de un gym);
  - `IMembershipReader` (membresías que vencen en N días: ya existe `GetExpiringAsync`; revisar si sirve sin `gymId` para el proceso multi-gym o si hace falta una variante);
  - `IGymReader` (nombre del gym y flags);
  - `IAchievementReader` (nombre del logro).
- **Reacciones por evento:** handlers de `ClassSessionCancelled` y de `AchievementUnlockedEvent` dentro de Communications. **Sin escrituras cruzadas nuevas.**
- **Publica** `AnnouncementPublished` (sin consumidor; queda para push).
- **Ningún módulo existente depende de Communications.**

## 8. Errores

| Situación | Código |
|---|---|
| Título o cuerpo fuera de rango | 400 (FluentValidation) / 422 (dominio) |
| Publicar algo ya publicado, fijar un cuarto anuncio, editar un archivado | 409 |
| Miembro reacciona con el módulo apagado (`ShowCommunity = false`) | 403 |
| Llamar al endpoint interno sin el secreto o con un secreto incorrecto | 401 |
| Anuncio de otro gym | 404 (lo filtra el Global Query Filter) |
