# Módulo Notifications — especificación

> **Estado:** construido en la v2 (rama `v2`, verificado con tests el 2026-10-20). Describe el diseño vigente; lo que quedó sin hacer o sin verificar está en `V2-PENDIENTES.md`. Las frases «hoy…» que contrastan con lo anterior describen la v1, previa a esta versión. Módulo nuevo de la v2.

**Schema:** `notifications` · **Lo alimentan:** todos los módulos, por eventos · **Consume:**
Identity (destinatarios, permisos), Members, Gyms (zona horaria y horario sin avisos)

## 1. Propósito

**Entregar avisos** a miembros, staff y Owners por tres canales: la **bandeja** dentro de la app o del
panel, el **push** al teléfono o al navegador, y el **correo**. Decide a quién llega cada aviso, por qué
canal, cuándo (respetando preferencias y horario sin avisos) y lleva el registro de lo enviado.

**No decide el contenido de negocio.** Los demás módulos publican eventos ("se reportó una falla",
"se confirmó una reserva") y Comunicaciones sigue siendo dueño de los anuncios, el feed, las
reacciones, las plantillas de WhatsApp y los recordatorios.

## 2. Decisiones tomadas (2026-10-07)

| Tema | Decisión |
|---|---|
| Motor | **Un proceso en segundo plano dentro de la API** revisa la cola cada 30 segundos. Con varias copias de la API, un candado en Postgres evita envíos dobles (§5). |
| Plataformas | **Android nativo, app web del miembro (PWA) y panel en el navegador**, con Firebase Cloud Messaging. **iOS después**, cuando haya cuenta de Apple Developer y una Mac (§6). |
| Push para Owner y staff | **El panel se puede instalar en el celular como app web** y recibe push aunque esté cerrado. Cada persona elige qué avisos quiere en su teléfono (§8). |
| Avisos atendidos | **Según el tipo**: los que piden una acción quedan "atendido por Ana" para todos; los informativos se marcan como leídos por cada persona (§7). |

## 3. Qué cambia respecto de lo construido

Hoy los avisos están repartidos: la bandeja del miembro y la cola de correos viven en Comunicaciones
(etapa 13), y la campana del panel es de Attendance (intentos rechazados). **Notifications pasa a ser
dueño de la entrega**:

| Pieza | Hoy | v2 |
|---|---|---|
| Bandeja del miembro (`member_notifications`) | Communications | **Notifications** |
| Cola de correos (`email_outbox`) | Communications | **Notifications** (es un canal más) |
| Preferencias del miembro | Communications (un interruptor) | **Notifications**, por categoría y canal |
| Campana del panel | Attendance (solo intentos rechazados) | **Notifications**, bandeja general del staff |
| Anuncios, feed, reacciones, WhatsApp, cumpleaños, recordatorios | Communications | Communications (sin cambio de dueño) |

La migración mueve esas tablas al schema `notifications` sin perder datos. Comunicaciones deja de
escribir notificaciones y correos: publica eventos (§4.3).

## 4. Modelo

### 4.1 `Notification` — un aviso a una persona

| Campo | Notas |
|---|---|
| `GymId` | |
| `RecipientUserId` / `RecipientMemberId` | Uno de los dos: staff y Owners por cuenta; miembros por ficha (aunque no tengan cuenta, para el correo). |
| `Type` | Tipo de aviso (§9). |
| `Category` | Para las preferencias del miembro (`GSM-modulo-comunicaciones.md` v2 §C). |
| `Title`, `Body`, `ImageUrl`, `Link` | `Link` es un enlace interno: `/app/clases?sesion=…`, `/equipment/issues/…`. |
| `Priority` | `normal` o `urgent`. |
| `ScheduledFor` | Cuándo debe salir. Por defecto, ya. |
| `Status` | `pending`, `held` (esperando que termine el horario sin avisos), `sent`, `failed`, `cancelled`, `skipped` (apagado por preferencia). |
| `ReferenceId` | Lo que la originó (la reserva, el reporte…). **Índice único `(Type, Recipient, ReferenceId)`**: el mismo aviso no sale dos veces aunque el evento se publique dos veces. |
| `ReadAt` | Bandeja: leída por esa persona. |
| `ResolvedAt`, `ResolvedBy` | Solo avisos de acción del staff (§7). |
| `CreatedAt`, `SentAt` | |

### 4.2 `NotificationDelivery` — un envío por canal

`NotificationId`, `Channel` (`push`, `email`), `DeviceId` (en push), `Status`, `Attempts`,
`LastError`, `SentAt`. La bandeja no necesita entrega: el aviso aparece en ella en cuanto llega su hora.

### 4.3 Cómo se crea un aviso

- **Por evento:** un manejador de Notifications escucha el evento del módulo dueño
  (`CheckInRejected`, `EquipmentIssueReported`, `WaitlistOfferMade`, `ClassCreated`…), resuelve los
  destinatarios y crea las filas.
- **Comunicaciones también avisa por evento** (decidido 2026-10-07). No hay interfaz de escritura
  hacia Notifications, así que no se agrega una cuarta excepción a la regla de `CLAUDE.md`:
  - `AnnouncementPublished` (anuncio, categoría y lista de destinatarios ya calculada),
  - `BirthdayGreetingDue` (miembro, texto y hora de envío),
  - `MembershipReminderDue` (miembro, tipo de recordatorio y fechas).

  Notifications los consume y crea los avisos. Si el manejador falla, se loguea y el anuncio queda
  publicado igual: es la regla general de los eventos del sistema.
- **Ningún módulo escribe directo en Notifications.** Notifications tampoco escribe en los demás.

### 4.4 Avisos programados y cancelables

- El recordatorio de clase se crea al confirmar la reserva, con `ScheduledFor` = inicio − 10 minutos.
- Si la reserva o la sesión se cancela, el aviso pasa a `cancelled` (Notifications escucha esos
  eventos). Si cambia la hora, se cancela y se crea otro.
- Los cumpleaños los genera Comunicaciones la noche anterior con `ScheduledFor` = 8:00.

## 5. Motor de envío

- **`NotificationDispatcher`:** un `BackgroundService` de la API que **cada 30 segundos**:
  1. toma los avisos con `ScheduledFor` ≤ ahora en estado `pending` o `held`;
  2. aplica preferencias (§10) y horario sin avisos: si no puede salir, pasa a `held` hasta la hora de
     fin;
  3. los deja en la bandeja y envía push y correo según el tipo;
  4. reintenta los envíos fallidos hasta 3 veces con espera creciente; después, `failed`.
- **Candado:** antes de cada vuelta toma un `pg_try_advisory_lock`. Si hay dos copias de la API, solo
  una procesa; si esa se cae, la otra toma el relevo en la vuelta siguiente.
- **Precisión:** un aviso sale entre 0 y 30 segundos después de su hora. El recordatorio de 10 minutos
  queda bien cubierto.
- **El primer proceso en segundo plano del sistema.** Hasta ahora la regla era "no hay scheduler". La
  tarea de Dokploy (`/internal/jobs/communications/run`) se mantiene para lo que no necesita precisión
  (generar recordatorios, purgas, vencer ofertas de la lista de espera, anonimizar); el envío de correos
  pasa al dispatcher. Mover el resto al dispatcher queda como opción futura.
- Toda lectura de tiempo pasa por `IClock`, como en el resto del sistema.
- **Purga:** los avisos del miembro de más de 90 días se borran, leídos o no (decidido 2026-10-11). Los del staff, solo si están leídos o atendidos: una acción pendiente no se pierde.

## 6. Push: plataformas

- **Firebase Cloud Messaging (FCM), API HTTP v1**, para todo: Android nativo, la PWA del miembro y el
  panel instalado. Un proyecto de Firebase por ambiente; la cuenta de servicio va en la configuración
  (`Notifications:Fcm:*`), nunca en el código.
- **Dispositivos** — `PushDevice`: `UserId`, `Token`, `Platform` (`android`, `web_member`,
  `web_panel`; `ios` más adelante), `CreatedAt`, `LastSeenAt`, `RevokedAt`.
  - Se registra al iniciar sesión, si la persona dio permiso; se borra al cerrar sesión.
  - Si FCM responde que el token ya no sirve, se revoca solo.
  - **El dispositivo es de la persona, no del gym**: alguien que es miembro en un gym y entrenador en
    otro recibe los avisos de ambos. El aviso lleva su `GymId`; al tocarlo, la app cambia de gym si hace
    falta (`GSM-modulo-identity.md` §3.2).
- **Cuándo se pide el permiso:** nunca al abrir la app por primera vez. Se pide en el momento en que
  tiene sentido, con una explicación: al reservar la primera clase ("Activa los avisos para que te
  recordemos tu clase"), o al staff al entrar al panel instalado.
- **iPhone hoy:** la app web recibe push solo si el usuario la instala en la pantalla de inicio (iOS
  16.4 o superior). La app muestra cómo hacerlo. **iOS nativo** queda para cuando exista la cuenta de
  Apple Developer y una Mac para compilar.
- **Sin push** (no dio permiso o no tiene la app): el aviso queda en la bandeja y, si el tipo lo prevé
  (§9), sale por correo.
- **Riesgo conocido:** la app Android (MAUI) nunca se ha probado en un teléfono real; la integración con
  FCM tampoco podrá probarse sin un dispositivo o emulador.

## 7. Bandeja del staff (campana del panel)

- Reemplaza a la campana actual, que solo mostraba intentos de entrada rechazados.
- **Avisos de acción** — quedan atendidos **para todos** cuando alguien los atiende ("Atendido por Ana ·
  6:12"):
  - intento de entrada rechazado (se atiende al verlo, como hoy);
  - falla de máquina reportada (se atiende al pasarla a "en revisión" o al descartarla);
  - máquina insegura (igual, y sale arriba y en rojo);
  - solicitud de anulación de un pago (se atiende al aprobarla o rechazarla).
- **Avisos informativos** — cada persona los marca como leídos:
  - recordatorio de clase al instructor;
  - anonimizaciones próximas (Owners);
  - caja olvidada abierta (Owners);
  - rutinas por revisar de mis miembros (entrenadores), como un resumen diario, no uno por rutina.
- **Quién recibe cada uno** se resuelve por rol y permiso: los avisos de anulación, a los Owners y a
  quien tenga `VoidPayments`; los de la caja, a los Owners y a quien tenga `ViewFinances`; los de
  clases, al instructor.
- Un aviso de acción se crea **una vez por gym** (no una fila por persona) y se le muestra a todos los
  que corresponde; los informativos, una fila por persona.

## 8. Push para Owner y staff

- **El staff también usa la app nativa** (`GSM-apps-y-vistas.md`): en Android recibe los push en la
  app. **El panel instalable** como app web (manifiesto y service worker propios) queda para quien no
  instale la app y para iPhone mientras no exista la app de iOS.
- **Cada persona elige** en `/perfil` qué avisos quiere en su teléfono. Por defecto:

| Aviso | Push por defecto |
|---|---|
| Máquina insegura | Encendido (no se puede apagar para los Owners) |
| Falla de máquina | Encendido |
| Solicitud de anulación | Encendido (a quien puede aprobarla) |
| Recordatorio de mi clase (instructor) | Encendido |
| Intento de entrada rechazado | **Apagado** (puede haber muchos al día) |
| Informativos | Apagado |

- El horario sin avisos del gym no aplica al staff: cada persona apaga lo que no quiere.

## 9. Catálogo de avisos

| Tipo | Para | Categoría | Bandeja | Push | Correo | Urgente | Horario sin avisos |
|---|---|---|---|---|---|---|---|
| Por vencer (N días antes) | Miembro | Mi membresía | ✓ | ✓ | ✓ | | No aplica (horario de envío 8:00) |
| Vence hoy | Miembro | Mi membresía | ✓ | ✓ | ✓ | | ídem |
| Último día de gracia | Miembro | Mi membresía | ✓ | ✓ | ✓ | | ídem |
| Recordatorio de clase (10 min) | Miembro con reserva | Mis clases | ✓ | ✓ | | | Exento |
| Oferta de la lista de espera | Miembro | Mis clases | ✓ | ✓ | | ✓ | Exento |
| Clase cancelada | Miembros con reserva | Mis clases | ✓ | ✓ | ✓ | | Exento |
| Penalización por inasistencias | Miembro | Mis clases | ✓ | ✓ | | | No aplica |
| Tu oferta de la lista de espera venció | Miembro | Mis clases | ✓ | ✓ | | | Exento |
| Logro desbloqueado / premio | Miembro | Mi progreso | ✓ | ✓ | | | No aplica |
| Tu premio vence en 3 días | Miembro con premio pendiente | Mi progreso | ✓ | ✓ | | | No aplica |
| Clase nueva | Miembros activos | Novedades | ✓ | ✓ | | | Aplica |
| Anuncio | Su audiencia | Novedades o Promociones | ✓ | ✓ | Si el anuncio lo pide | | Aplica |
| Anuncio **importante** | Su audiencia | La del anuncio, sin preferencia | ✓ (feed, fijo arriba) | ✓ | Si el anuncio lo pide | | Exento |
| Cumpleaños | Miembro | Novedades | ✓ | ✓ | | | Sale a las 8:00 |
| Entrada con tu documento | Miembro | — (seguridad, sin preferencia) | ✓ | ✓ | ✓ | | Exento |
| La máquina que reportaste ya funciona | Miembro que reportó | Novedades | ✓ | ✓ | | | Aplica |
| Tu rutina cambió (ejercicio reemplazado, quitado o restablecido, con motivo) — Routines R12 | Miembro | Mi rutina | ✓ | ✓ | | | No aplica |
| Intento rechazado | Staff | — | ✓ (acción) | Opcional | | | — |
| Falla de máquina | Owner y staff | — | ✓ (acción) | ✓ | | | — |
| Máquina insegura | Owner y staff | — | ✓ (acción) | ✓ | ✓ (Owners) | ✓ | — |
| Solicitud de anulación | Owners y `VoidPayments` | — | ✓ (acción) | ✓ | | | — |
| Recordatorio de mi clase | Instructor | — | ✓ | ✓ | | | — |
| Anonimizaciones próximas | Owners | — | ✓ | | ✓ | | — |
| Caja olvidada | Owners y `ViewFinances` | — | ✓ | | | | — |
| Rutinas por revisar (resumen diario) | Entrenador | — | ✓ | | | | — |
| Suscripción del gym (prueba, vencimiento, mora, inhabilitación, pago registrado, límite de miembros) | Owners | — | ✓ | ✓ | ✓ | Inhabilitación y suspensión | — |

**Correos de cuenta** (Identity, `GSM-modulo-identity.md`). Son solo correo, sin preferencias ni
horario sin avisos, y salen por la misma cola:

| Tipo | Para |
|---|---|
| Activación de cuenta (enlace) | Owner o staff invitado |
| Invitación a la app (enlace) | Miembro |
| Bienvenida | Miembro, al registrarse |
| Restablecer contraseña (enlace) | Cualquiera |
| Código de acceso (6 dígitos) | Miembro |
| Verificación de cambio de email | Cualquiera |
| Cuenta bloqueada por intentos fallidos | Cualquiera |

**Correo como respaldo:** solo donde la tabla lo marca. La oferta de la lista de espera y el
recordatorio de clase **no** usan correo: llegarían tarde.

## 10. Preferencias y horario sin avisos

- **Miembros:** por categoría y canal, como lo definió Comunicaciones (v2 §C). La bandeja siempre
  recibe todo. Los avisos de seguridad no tienen preferencia.
- **Staff:** por tipo de aviso, solo para el push (§8).
- **Anuncio importante** (decidido 2026-10-11): casilla al publicar, **solo el Owner**, y queda registrado quién la marcó. Se salta el horario sin avisos y las preferencias de Novedades/Promociones (como los avisos de seguridad), y queda fijo arriba en el feed hasta su fecha. No entra en la bandeja (pregunta 8).
- **Horario sin avisos** del gym (21:00–7:00 por defecto): los avisos de las categorías que aplican
  quedan en `held` y salen al terminar. Lo exento sale siempre.

## 11. Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **N1** | Schema `notifications`, migración de la bandeja y la cola de correos desde Comunicaciones, dispatcher con candado, bandeja general del staff (reemplaza la campana de intentos rechazados), avisos de acción compartidos. Sin push todavía. |
| **N1b** | Avisos de acción filtrados por permiso (decidido 2026-10-12): la solicitud de anulación les llega al Owner, a quien tiene `VoidPayments` y a quien tiene `ViewFinances` (este último solo la ve, no la resuelve). Sirve de base para otros avisos sensibles. Hoy el aviso es una fila por gym que ve todo el staff. |
| **N2** | Push web: Firebase, registro de dispositivos, PWA del miembro y panel instalable, pedido de permiso en contexto, preferencias de push del staff. Incluye el **anuncio importante** (§9, decidido 2026-10-11). |
| **N3** | Push en Android nativo (MAUI). Requiere probar en un teléfono o emulador real. |
| **N4** | Preferencias del miembro por categoría y canal, y horario sin avisos (con Comunicaciones CO1 y CO6). |
| **N5** | Avisos programados y cancelables: recordatorio de clase, cumpleaños, recordatorios de vencimiento. |
| **Después** | iOS nativo (APNs a través de FCM). Código por WhatsApp. |
