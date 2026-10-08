# Módulo Classes — Especificación

> Fase 2, segundo módulo fuera del MVP. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`; **si algo aquí contradice a esos dos, ellos mandan.** Sigue las convenciones transversales de `docs/BACKEND-MVP-PLAN.md`.

**Schema:** `classes` · **Toca también:** `attendance` (check-in por evento), `achievements` (tipo de logro) y `gyms` (flag ya existente)

---

## Replanteamiento v2 — en revisión (2026-10-07)

> Nada de esta sección está construido. Lo que viene después de ella describe **la v1, que es lo
> construido hoy**. Donde las dos se contradicen, manda esta sección una vez aprobada.

### Decisiones tomadas

| Tema | Decisión |
|---|---|
| Horario | Las clases **solo se agendan dentro del horario del gym** (viene de Gyms). |
| Estar al día | **Reservar y asistir exige estar al día**, sin "Dejar pasar" (viene de Attendance). |
| Planes | **Todos los planes incluyen clases**: los planes no tienen restricciones (viene de Memberships). |
| Instructor | Se elige de la **lista de entrenadores** (staff con permiso `Trainer`), con la opción **"Otro"** para escribir un nombre libre. |
| Clase llena | **Lista de espera con confirmación.** |
| Reglas de reserva | **Plazo de cancelación, penalización por no-show, ventana de reserva y máximo de reservas activas**, configurables por gym. |
| Serie | Una clase recurrente puede tener **fecha de fin**. |
| Cupo | Se puede **ligar a máquinas** del inventario. |
| Asistencia | El **miembro** puede marcar su propia asistencia desde la app, además del instructor y el staff. |
| Recordatorio | **Push 10 minutos antes de la clase** a quien tiene reserva, aunque ya esté en el gym. |
| Clase nueva | **Aviso a todos los miembros** del gym cuando se crea una clase: publicación en Novedades y push. |

### Las clases solo se agendan dentro del horario del gym

- Crear o editar una clase **falla** (422) si algún tramo de la clase (inicio + duración) cae fuera del
  horario de atención de ese día de la semana (`GSM-modulo-gyms.md` §3.1). Hoy no se valida.
- **Festivos y días con otro horario:** el gym abre todos los días, pero un festivo puede tener un
  horario más corto. Una ocurrencia que ese día cae fuera del horario **no se puede reservar ni marcar
  asistencia**. Lo mismo si el gym cierra un día completo como excepción (`GSM-modulo-gyms.md` §3.1).
  Sale en el horario como "Gym cerrado". Si ya tenía reservas, se tratan como una sesión cancelada: las reservas se cancelan y el aviso a los miembros va por el evento que ya
  existe (`ClassSessionCancelled`).
- **Si el gym cambia su horario** y una clase recurrente queda por fuera, el cambio de horario no se
  bloquea: el panel muestra la lista de clases afectadas para que el staff las mueva. Mientras tanto,
  sus ocurrencias fuera del horario no se pueden reservar.
- Como consecuencia, la asistencia a una clase nunca cae con el gym cerrado, y la regla `gym_closed`
  de Attendance aplica también a las clases, sin excepción.

### Reservar y asistir exige estar al día

- **Hoy no se exige:** reservar con la membresía vencida funciona, y la respuesta solo trae
  `membershipExpired = true` como aviso (`ClassBookingHandlers`).
- **Reservar:** el miembro debe tener cobertura **el día de la sesión**, según el `AccessStatus` de
  Memberships (al día, por vencer o en gracia). Si su membresía vence antes de la clase: "Tu plan
  vence el 7 oct; renueva para reservar esta clase".
  - Las tiqueteras deben tener entradas y vigencia ese día. **Reservar no consume la entrada**: la
    consume la asistencia.
  - Si al llegar el día de la clase el miembro ya no está al día (por ejemplo, se le venció y no
    renovó), la reserva sigue, pero **no se le puede marcar asistencia** hasta que renueve.
- **Marcar asistencia** (staff, desde el roster): pasa por las mismas verificaciones que la entrada al
  gym (`GSM-modulo-attendance.md` §3). Un miembro vencido fuera de la gracia **no** puede asistir a una
  clase, y aquí **no hay "Dejar pasar"**: tiene que renovar primero.
- La asistencia sigue registrando la entrada del día por evento (`ClassAttendanceRegistered`), y en
  tiqueteras consume una entrada.

### Instructor

- `GymClass` y `ClassSession` suman `InstructorUserId` (nulo) junto al `InstructorName` que ya existe.
- El formulario muestra **la lista de entrenadores del gym** (accesos de staff con `Trainer`, y Owners
  marcados como entrenadores) y al final **"Otro"**, que abre un campo de texto libre para un
  instructor externo u ocasional.
- Con un entrenador elegido, `InstructorName` se copia de su cuenta. Con "Otro", `InstructorUserId`
  queda nulo.
- **El entrenador ve "Mis clases"** en el panel (sus sesiones de hoy y de la semana) y puede **marcar
  asistencia desde su teléfono**. Un instructor "Otro" no tiene cuenta: su clase la maneja el staff.
- Si la cuenta del entrenador se desactiva o pierde `Trainer`, sus clases futuras siguen con su
  nombre y el panel las lista como "sin entrenador activo" para reasignarlas.

### Lista de espera con confirmación

Tabla nueva `classes.waitlist_entries`: `SessionId`, `MemberId`, `Position`, `Status` (`waiting`,
`offered`, `confirmed`, `expired`, `left`), `OfferedAt`, `OfferExpiresAt`, `CreatedAt`.

- **Cuándo:** solo en clases con cupo, cuando están llenas. El miembro presiona "Anotarme en espera" y
  ve su posición ("Eres el 2 en la lista").
- **Se exige lo mismo que para reservar** (estar al día ese día, ventana de reserva, no estar
  penalizado), y **cuenta para el máximo de reservas activas**.
- **Al liberarse un cupo** (alguien cancela, o el staff sube el cupo), el primero de la lista recibe
  una **oferta**: "Se liberó un cupo en Spinning de mañana 6:00. Confirma antes de las 18:30". El cupo
  queda apartado para él.
  - **Tiempo para confirmar:** configurable por gym, 60 minutos por defecto, y nunca más allá del
    inicio de la clase. Si queda menos tiempo que eso para la clase, la oferta dura hasta 15 minutos
    antes de que empiece.
  - **Confirma** → pasa a reservado. **No confirma a tiempo o la rechaza** → la oferta vence y pasa al
    siguiente de la lista. Si venció sin respuesta, el miembro recibe "No confirmaste a tiempo; el cupo
    pasó al siguiente" y sale de la lista.
- **Cómo se vence una oferta sin scheduler:** la revisa la tarea periódica que ya existe
  (`/internal/jobs/…`, cada 5–10 minutos), y también cualquier consulta de esa sesión. Por eso una
  oferta puede durar unos minutos más de lo anunciado, nunca menos.
- **Cómo se avisa:** notificación dentro de la app (la que ya tiene Comunicaciones) y push cuando
  exista Notifications. El correo llega tarde para una oferta de una hora, así que no se usa para esto.
- La lista se cierra cuando empieza la clase; las entradas que quedaban pasan a `expired`.
- El roster del staff muestra la lista de espera con su orden.

### Reglas de reserva

Ajustes por gym en `classes.class_settings` (perezosos, como `checkin_settings`):

| Regla | Ajuste | Por defecto |
|---|---|---|
| **Plazo de cancelación** | `cancel_deadline_hours` | 2 h antes |
| **Penalización por no-show** | `no_show_limit` en `no_show_window_days` → bloqueo de `no_show_block_days` | 3 en 30 días → 7 días sin reservar |
| **Ventana de reserva** | `booking_window_days` | Las reservas abren 7 días antes |
| **Máximo de reservas activas** | `max_active_bookings` | 5 (reservas futuras + lista de espera) |
| Tiempo para confirmar una oferta | `waitlist_offer_minutes` | 60 min |

- **Cancelar después del plazo** sí se puede, para liberar el cupo a la lista de espera, pero **cuenta
  como no-show** para la penalización. La app lo advierte antes de confirmar.
- **No-show:** una reserva cuya asistencia el instructor o el staff marcó como "no vino", o una
  cancelación tardía. **Una reserva que nadie marcó no cuenta como no-show**: si el staff no tomó
  asistencia, no se castiga al miembro por eso.
- **Al llegar al límite**, el miembro ve "No puedes reservar hasta el 15 oct por inasistencias" y
  recibe una notificación. Sus reservas ya hechas se mantienen.
- **El staff puede levantar la penalización** desde la ficha, con motivo.

### Fecha de fin de la serie

- `GymClass.EndsOn` (opcional). Una clase recurrente se proyecta hasta esa fecha. Sirve para
  temporadas o cursos ("Natación, 8 semanas").
- Sin fecha de fin funciona como hoy: hasta que se da de baja.

### Cupo ligado a máquinas

- Una clase puede ligarse a **máquinas del inventario** (`GSM-modulo-equipment.md`): por ejemplo, las
  15 bicicletas de la zona de spinning. Tabla nueva `classes.class_equipment_items` (`ClassId`,
  `EquipmentItemId`), validada contra `IEquipmentReader`. Classes lee a Equipment; Equipment no sabe
  nada de clases.
- **Cupo efectivo de una sesión** = el menor entre el cupo de la clase y las máquinas ligadas que
  están **disponibles ese día**.
- Si una máquina sale de servicio cuando la sesión ya está llena: **no se cancela a nadie**. El roster
  y la lista de "Mis clases" muestran "18 reservas y 17 bicicletas disponibles" para que el staff
  decida. Las nuevas reservas sí respetan el cupo efectivo.
- Una clase sin máquinas ligadas usa solo su cupo, como hoy.

### Asistencia marcada por el miembro

- Con una reserva (o en una clase sin cupo), el miembro ve **"Estoy en clase"** en la app.
- **Funciona solo si:** ya registró su entrada al gym hoy (eso prueba que está en el gym, incluida la
  geolocalización con QR fijo) y es entre 15 minutos antes y 15 minutos después del inicio de la clase.
- Marca la reserva como `attended`. Como ya entró al gym, no se registra otra entrada.
- El instructor y el staff siguen pudiendo marcar o corregir la asistencia desde el roster.

### Avisos de clases

Decidido 2026-10-07. Todos dependen del módulo nuevo **Notifications** (`GSM-modulo-notifications.md`).

**Recordatorio 10 minutos antes**

- **Push a cada miembro con reserva confirmada**, 10 minutos antes del inicio: "Spinning empieza en
  10 minutos · Salón 2 · con Carlos".
- **Llega aunque el miembro ya esté en el gym.** No se omite por haber registrado la entrada.
- No llega si la reserva se canceló, si la sesión se canceló o si el miembro ya marcó "Estoy en
  clase".
- También al instructor que es entrenador del gym: "Tu clase de Spinning empieza en 10 minutos · 14
  reservas".
- **Precisión:** exige que Notifications revise los avisos programados **cada minuto**. La tarea
  periódica actual corre cada 5–10 minutos y no alcanza para esto (ver Notifications).

**Aviso de clase nueva**

- Al **crear** una clase (recurrente o de una fecha), se avisa a **todos los miembros activos** del
  gym (no los dados de baja).
- Dos canales:
  1. **Publicación automática en Novedades** (el feed de Comunicaciones): "Nueva clase: Yoga
     restaurativo · martes y jueves 7:00 p. m. · con Laura", con botón "Ver horario". Los miembros
     pueden reaccionar como a cualquier aviso.
  2. **Push** con el mismo texto.
- Solo al crear, **no al editar**. Si cambia la hora de una clase existente, avisar es decisión del
  staff, con un anuncio normal.
- El staff puede desmarcar "Avisar a los miembros" al crear la clase (por ejemplo, si está armando el
  horario de la temporada y va a avisar todo junto).
- Respeta las preferencias de comunicación del miembro: quien apagó "Novedades del gym" no recibe el
  push, aunque la publicación sí queda en el feed.
- Classes publica `ClassCreated`; Comunicaciones lo consume para la publicación y Notifications para
  el push. Sin escrituras cruzadas.

### Lo que cambia en otros módulos

- **Identity:** la lista de entrenadores sale de los accesos con `Trainer`.
- **Equipment:** Classes lee la disponibilidad de las máquinas ligadas.
- **Communications / Notifications:** avisos de oferta de la lista de espera y de penalización por
  no-show; recordatorio 10 minutos antes; publicación y push de clase nueva (`ClassCreated`).
- **Members:** la ficha muestra las penalizaciones vigentes y permite levantarlas.

### Propuesta de sub-etapas

| Sub-etapa | Qué |
|---|---|
| **C1** | Horario del gym y estar al día (secciones de arriba). |
| **C2** | Instructor de la lista de entrenadores o "Otro"; "Mis clases" y asistencia desde el teléfono. |
| **C3** | Reglas de reserva: plazo de cancelación, ventana, máximo de reservas, penalización por no-show. |
| **C4** | Lista de espera con confirmación (depende de C3 para el máximo de reservas). |
| **C5** | Fecha de fin de la serie. |
| **C6** | Cupo ligado a máquinas (depende de E1 y E2 de Equipment). |
| **C7** | Asistencia marcada por el miembro. |
| **C8** | Avisos: recordatorio 10 minutos antes y aviso de clase nueva (depende de Notifications). |

### Decisiones abiertas de la v1, resueltas

1. No-show repetido → penalización configurable (arriba).
2. Plazo de cancelación → configurable, 2 h por defecto.
3. Aviso de cancelación al miembro → ya lo hace Comunicaciones; push cuando exista Notifications.
4. Excepciones de horario de una ocurrencia → siguen fuera: no hay caso de uso.
5. Clases de pago aparte → no: todos los planes incluyen clases.

### Pendiente de decidir

- Nada por ahora.

---

## Decisiones de producto

1. **La recurrencia es opcional.** Una clase se repite **semanalmente sin fecha de fin** hasta que el gym la da de baja, o existe una sola vez en una fecha concreta.
2. **El cupo es opcional.** Con cupo, el miembro reserva y la clase se llena. Sin cupo, no hay reserva: se entra por orden de llegada.
3. **El instructor es un nombre de texto.** Sin entidad ni cuenta.
4. **Asistir a una clase cuenta como check-in del día y además suma en un contador propio** de clases para logros específicos.

---

## Entidades

### `GymClass` — la definición

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `name` | `string(120)` | "Spinning", "Yoga restaurativo". |
| `description` | `string(1000)?` | |
| `instructor_name` | `string(120)?` | Texto libre. |
| `capacity` | `int?` | **`null` = sin reserva**, entrada libre por orden de llegada. Con valor, ≥ 1. |
| `start_time` | `TimeOnly` | Hora local del gym. |
| `duration_minutes` | `int` | 5 a 480. |
| `is_recurring` | `bool` | |
| `days_of_week` | `day_of_week[]` | Solo si `is_recurring`: al menos un día. Vacío si no. |
| `scheduled_date` | `DateOnly?` | Solo si **no** es recurrente: la fecha única. |
| `starts_on` | `DateOnly` | Desde cuándo aplica la recurrencia (día de negocio del alta). |
| `is_active` | `bool` | Baja lógica. |
| `deactivated_on` | `DateOnly?` | Cierra la recurrencia; las sesiones pasadas se conservan. |

**Invariantes:** recurrente ⟺ `days_of_week` no vacío y `scheduled_date` null. No recurrente ⟺ `scheduled_date` con valor y `days_of_week` vacío.

**Índices:** `(gym_id, is_active)`, `(gym_id, scheduled_date)`.

### `ClassSession` — una ocurrencia concreta

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `class_id` | `Guid` | FK interna. |
| `session_date` | `DateOnly` | Día de negocio de la ocurrencia. |
| `start_time` · `duration_minutes` | | **Copia** de la definición al materializar. |
| `instructor_name` | `string(120)?` | Copia. |
| `capacity` | `int?` | Copia. |
| `status` | enum `class_session_status` | `scheduled` · `cancelled`. |
| `cancellation_reason` | `string(200)?` | |

**Índice único:** `(class_id, session_date)`.

> Los campos copiados son deliberados: si el gym cambia el instructor o el cupo, las sesiones ya ocurridas conservan lo que realmente pasó. Sin la copia, el histórico se reescribiría solo.

### `ClassBooking` — reserva y asistencia

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `session_id` | `Guid` | FK interna. |
| `member_id` | `Guid` | Validado contra `IMemberReader`. |
| `status` | enum `class_booking_status` | `booked` · `cancelled` · `attended` · `no_show`. |
| `booked_at` · `cancelled_at` · `attended_at` | `DateTimeOffset?` | |

**Índice único:** `(session_id, member_id)` — una fila por miembro y sesión; reservar de nuevo tras cancelar **reactiva** la misma fila.

> Reserva y asistencia viven en la misma entidad porque son estados del mismo hecho. En una clase **sin cupo** no hay reserva previa: marcar la asistencia crea la fila directamente en `attended`.

---

## Recurrencia y materialización

Una clase recurrente no tiene fin, así que **sus sesiones no se generan por adelantado**: se calculan.

**El horario es una proyección.** Para un rango de fechas se combinan las ocurrencias calculadas (por `days_of_week`, entre `starts_on` y `deactivated_on`) con las sesiones ya materializadas, que mandan siempre — una sesión cancelada o con instructor distinto pisa a la calculada.

**Una sesión se materializa solo cuando hay algo que guardar:** la primera reserva, la primera marca de asistencia, o una cancelación puntual. Mientras nadie toque una ocurrencia, no existe fila.

Es el mismo patrón perezoso del congelador de racha, y por el mismo motivo: **no hay scheduler en el sistema** y no se va a introducir uno para esto. La consecuencia conocida —una escritura durante lo que el cliente vive como una reserva— es aceptable, y el índice único `(class_id, session_date)` la hace idempotente frente a dos reservas simultáneas.

**Dar de baja una clase** fija `deactivated_on` y detiene la recurrencia desde esa fecha. Las sesiones pasadas y sus asistencias se conservan intactas.

**Cancelar una ocurrencia puntual** (festivo, instructor enfermo) materializa la sesión con `status = cancelled`. Las reservas existentes pasan a `cancelled`.

---

## Reservas y cupo

**Con cupo:** el miembro reserva; se rechaza con `409` cuando las reservas en estado `booked` igualan el cupo de la sesión. Puede cancelar libremente **hasta la hora de inicio**.

**Sin cupo** (`capacity = null`): no existe la operación de reservar — devuelve `422`. El miembro simplemente asiste.

**La membresía vencida no bloquea**, igual que en el check-in de la etapa 6: se reserva y se marca `membershipExpired` en la respuesta para que recepción cobre. Un gym real no deja a nadie fuera de la clase por software.

**Marcar asistencia** es cosa del staff, sobre una sesión concreta: los miembros con reserva pasan a `attended`, y los que no aparecieron a `no_show`. En una clase sin cupo se añaden miembros directamente.

---

## Integración con módulos existentes

### Attendance — por evento, no por escritura cruzada

Marcar asistencia a una clase publica **`ClassAttendanceRegistered(MemberId, GymId, SessionDate)`**, y un handler en Attendance registra el check-in del día.

**No es una escritura cruzada síncrona**, y es deliberado: `CLAUDE.md` exige agotar el camino normal antes de añadir una tercera excepción, y aquí el camino normal basta — el miembro no necesita ver el check-in en la respuesta de la clase. Es reacción, no parte del resultado.

El check-in ya es **idempotente por día** desde la etapa 6: si el miembro había fichado al entrar, el handler encuentra el registro existente y no duplica. No hace falta tocar nada de Attendance salvo añadir el handler.

### Achievements — el tipo que la especificación ya reservaba

Nuevo `condition_type` **`classes`** (el que `docs/GSM-especificacion-tecnica.md` listaba desde el principio y se dejó fuera por no existir el módulo):

- `ALTER TYPE achievements.condition_type ADD VALUE 'classes'` — recordar que `ADD VALUE` **no se revierte**.
- `IDomainEventHandler<ClassAttendanceRegistered>` que dispare la evaluación.
- `AchievementService` lee también `IClassReader`.

El nombre CLR es `Classes`; con el traductor snake_case del enum el label sale `classes` sin sorpresas.

### Gyms

`show_classes` **ya existe** desde la etapa 2. Sin migración. Con el flag apagado, los endpoints del miembro responden `403` y los de staff siguen abiertos.

---

## Comunicación

**`Public/IClassReader`:**

- `GetAttendedCountAsync(memberId)` — clases a las que asistió.
- `GetUpcomingBookingsAsync(memberId, limit)`
- `GetTodaySessionCountAsync(gymId)` — para el dashboard.

**Eventos:**

- `ClassAttendanceRegistered(MemberId, GymId, SessionDate)`
- `ClassSessionCancelled(ClassId, GymId, SessionDate, AffectedMemberIds)` — hoy sin consumidores; cuando exista push, es el disparador natural del aviso.

**Consume:** `IMemberReader`, `IGymReader` (flag), `IMembershipReader` (avisar de membresía vencida).

---

## Endpoints

**Staff**

```
GET    /api/classes                          definiciones
POST   /api/classes
PUT    /api/classes/{id}
DELETE /api/classes/{id}                     baja lógica, fija deactivated_on
GET    /api/classes/schedule?from=&to=       horario proyectado + sesiones materializadas
POST   /api/classes/{id}/sessions/{date}/cancel
GET    /api/classes/{id}/sessions/{date}/roster       reservas y asistencia
POST   /api/classes/{id}/sessions/{date}/attendance   marcar asistentes y ausentes
```

**Miembro**

```
GET    /api/me/classes?from=&to=             horario del gym con el estado de sus reservas
POST   /api/classes/{id}/sessions/{date}/book
DELETE /api/classes/{id}/sessions/{date}/book         cancelar su reserva
GET    /api/me/classes/history
```

Un rol `member` solo alcanza sus propios datos; se reutiliza `MemberSelfAccess`.

---

## Criterios de aceptación

1. Una clase recurrente de lunes y miércoles aparece en el horario de las semanas siguientes **sin haber materializado ninguna sesión**.
2. Reservar materializa la sesión una sola vez; dos reservas simultáneas no crean dos sesiones.
3. Con cupo 10 y 10 reservas activas, la undécima recibe `409`. Si una se cancela, vuelve a haber sitio.
4. En una clase sin cupo, reservar devuelve `422`; marcar asistencia funciona igual.
5. Cancelar una ocurrencia deja `status = cancelled` y pasa sus reservas a `cancelled`, sin tocar el resto de la serie.
6. Dar de baja la clase detiene la recurrencia desde ese día y **conserva** las sesiones y asistencias pasadas.
7. Marcar asistencia registra el check-in del día; si el miembro **ya** había fichado, no se duplica (índice único de Attendance).
8. Asistir a clases desbloquea un logro de tipo `classes`.
9. La sesión conserva instructor y cupo aunque después se editen en la definición.
10. Con `show_classes = false`, el miembro recibe `403` y el staff sigue operando.
11. Aislamiento entre gyms en clases, sesiones y reservas.

---

## Decisiones abiertas

1. **No-show repetido.** No hay penalización: quien reserva y no va solo queda marcado. Si se quiere bloquear al reincidente, es una regla sobre este mismo modelo.
2. **Plazo de cancelación.** Se puede cancelar hasta la hora de inicio. Un plazo mayor (2 h antes) sería un parámetro por gym.
3. **Aviso de cancelación al miembro.** El evento existe, pero sin push (fuera del MVP) el miembro se entera al abrir la app. Con Resend ya integrado, el correo sería viable si se quiere.
4. **Excepciones de horario** (cambiar la hora de una ocurrencia sin cancelarla) no entran: el modelo lo permitiría materializando con otra `start_time`, pero no hay caso de uso.
5. **Clases de pago aparte** (bono de 10 sesiones) no entran; hoy la clase va incluida en la membresía.
