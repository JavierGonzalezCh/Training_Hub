# Módulo Classes — Especificación

> Fase 2, segundo módulo fuera del MVP. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`; **si algo aquí contradice a esos dos, ellos mandan.** Sigue las convenciones transversales de `docs/BACKEND-MVP-PLAN.md`.

**Schema:** `classes` · **Toca también:** `attendance` (check-in por evento), `achievements` (tipo de logro) y `gyms` (flag ya existente)

---

## Decisiones de producto

1. **La recurrencia es opcional.** Una clase se repite **semanalmente sin fecha de fin** hasta que el gym la da de baja, o existe una sola vez en una fecha concreta.
2. **El cupo es opcional.** Con cupo, el socio reserva y la clase se llena. Sin cupo, no hay reserva: se entra por orden de llegada.
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

**Índice único:** `(session_id, member_id)` — una fila por socio y sesión; reservar de nuevo tras cancelar **reactiva** la misma fila.

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

**Con cupo:** el socio reserva; se rechaza con `409` cuando las reservas en estado `booked` igualan el cupo de la sesión. Puede cancelar libremente **hasta la hora de inicio**.

**Sin cupo** (`capacity = null`): no existe la operación de reservar — devuelve `422`. El socio simplemente asiste.

**La membresía vencida no bloquea**, igual que en el check-in de la etapa 6: se reserva y se marca `membershipExpired` en la respuesta para que recepción cobre. Un gym real no deja a nadie fuera de la clase por software.

**Marcar asistencia** es cosa del staff, sobre una sesión concreta: los socios con reserva pasan a `attended`, y los que no aparecieron a `no_show`. En una clase sin cupo se añaden socios directamente.

---

## Integración con módulos existentes

### Attendance — por evento, no por escritura cruzada

Marcar asistencia a una clase publica **`ClassAttendanceRegistered(MemberId, GymId, SessionDate)`**, y un handler en Attendance registra el check-in del día.

**No es una escritura cruzada síncrona**, y es deliberado: `CLAUDE.md` exige agotar el camino normal antes de añadir una tercera excepción, y aquí el camino normal basta — el socio no necesita ver el check-in en la respuesta de la clase. Es reacción, no parte del resultado.

El check-in ya es **idempotente por día** desde la etapa 6: si el socio había fichado al entrar, el handler encuentra el registro existente y no duplica. No hace falta tocar nada de Attendance salvo añadir el handler.

### Achievements — el tipo que la especificación ya reservaba

Nuevo `condition_type` **`classes`** (el que `docs/GSM-especificacion-tecnica.md` listaba desde el principio y se dejó fuera por no existir el módulo):

- `ALTER TYPE achievements.condition_type ADD VALUE 'classes'` — recordar que `ADD VALUE` **no se revierte**.
- `IDomainEventHandler<ClassAttendanceRegistered>` que dispare la evaluación.
- `AchievementService` lee también `IClassReader`.

El nombre CLR es `Classes`; con el traductor snake_case del enum el label sale `classes` sin sorpresas.

### Gyms

`show_classes` **ya existe** desde la etapa 2. Sin migración. Con el flag apagado, los endpoints del socio responden `403` y los de staff siguen abiertos.

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

**Socio**

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
7. Marcar asistencia registra el check-in del día; si el socio **ya** había fichado, no se duplica (índice único de Attendance).
8. Asistir a clases desbloquea un logro de tipo `classes`.
9. La sesión conserva instructor y cupo aunque después se editen en la definición.
10. Con `show_classes = false`, el socio recibe `403` y el staff sigue operando.
11. Aislamiento entre gyms en clases, sesiones y reservas.

---

## Decisiones abiertas

1. **No-show repetido.** No hay penalización: quien reserva y no va solo queda marcado. Si se quiere bloquear al reincidente, es una regla sobre este mismo modelo.
2. **Plazo de cancelación.** Se puede cancelar hasta la hora de inicio. Un plazo mayor (2 h antes) sería un parámetro por gym.
3. **Aviso de cancelación al socio.** El evento existe, pero sin push (fuera del MVP) el socio se entera al abrir la app. Con Resend ya integrado, el correo sería viable si se quiere.
4. **Excepciones de horario** (cambiar la hora de una ocurrencia sin cancelarla) no entran: el modelo lo permitiría materializando con otra `start_time`, pero no hay caso de uso.
5. **Clases de pago aparte** (bono de 10 sesiones) no entran; hoy la clase va incluida en la membresía.
