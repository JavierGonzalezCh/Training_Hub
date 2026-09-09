# Plan de construcción — Módulo Classes (Etapa 10)

> Documento de trabajo para Claude Code. La **especificación funcional** está en `docs/GSM-modulo-classes.md`; este archivo dice en qué orden se construye. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`: **si algo aquí los contradice, ellos mandan.**
>
> **Ritmo:** una sub-etapa por vez, sin empezar la siguiente sin confirmación explícita. Cada una termina compilando, con su migración aplicada, sus endpoints respondiendo y la suite completa en verde.

---

## Punto de partida

MVP (etapas 0–7) y Routines (etapa 9) completos. Convenciones que este módulo hereda **sin excepción**:

- `IClock` para toda lectura de tiempo: `UtcNow` para instantes, `Today` para el día de negocio. Nunca `DateTimeOffset.UtcNow` suelto en Application/Domain.
- `LowerCaseNameTranslator.Instance` (nunca `new`) al mapear enums de Postgres. Registrar cada enum en **los tres sitios**: `GsmDbContext`, `DependencyInjection` y `GsmDbContextFactory`, o las migraciones fallan.
- Un caso de uso por archivo, con `HandleAsync`, inyectado directo en el controller. Sin bus de comandos.
- Comunicación entre módulos solo por `Public/I{Módulo}Reader` (lectura síncrona) o eventos in-process (reacción asíncrona). Nada de escritura cruzada nueva: la de Attendance se resuelve por evento (ver spec).
- Errores por la jerarquía de `DomainException`: 422 invariante, 409 conflicto, 403 prohibido, 404 no encontrado. 400 solo para forma del request (FluentValidation).
- Migración por sub-etapa, nombrada `Etapa10_{n}_{Nombre}`.
- Multi-tenancy: toda entidad de negocio implementa `ITenantEntity` y entra en el Global Query Filter. (A diferencia de `Exercise` en Routines, aquí no hay catálogo de sistema.)

**Riesgo transversal a vigilar:** la materialización perezosa de sesiones (una escritura durante lo que el cliente vive como una lectura/reserva) y su idempotencia frente a concurrencia, garantizada por el índice único `(class_id, session_date)`.

---

# 10.1 — Definiciones de clase (`GymClass`)

**Objetivo:** el staff crea, edita, lista y da de baja definiciones de clase. Sin sesiones ni reservas todavía.

### Trabajo

1. **Domain** — `GymClass` (`ITenantEntity`) + enum CLR `DayOfWeek` propio del módulo (o `System.DayOfWeek` mapeado). `GymClassDraft` para crear/reemplazar. Invariantes en el factory:
   - `name` 2–120, `description` ≤ 1000, `instructor_name` ≤ 120.
   - `capacity`: `null` o ≥ 1.
   - `duration_minutes` 5–480.
   - **recurrente ⟺ `days_of_week` no vacío y `scheduled_date` null**; **no recurrente ⟺ `scheduled_date` con valor y `days_of_week` vacío**.
   - `days_of_week` sin repetidos.
   - `Deactivate(on)` fija `is_active = false` y `deactivated_on`.
2. **Infrastructure** — `GymClassConfiguration` (schema `classes`, tabla `gym_classes`, `days_of_week` como `classes.day_of_week[]`, índices `(gym_id, is_active)` y `(gym_id, scheduled_date)`). Registrar el enum `day_of_week` en los tres sitios. `ClassRepository` (lecturas por gym vía Global Query Filter).
3. **Application** — `Classes/` con contratos, `IClassRepository`, `ClassMapper` (manual, como `RoutineMapper`), validators FluentValidation y casos de uso: `ListClassesHandler`, `GetClassHandler`, `CreateClassHandler`, `UpdateClassHandler`, `DeactivateClassHandler`. `DeactivateClassHandler` usa `clock.Today` para `deactivated_on`.
4. **API** — `ClassesController` bajo `/api/classes`, `[Authorize(Policy = RequireStaff)]`:
   `GET /api/classes` · `POST` · `PUT /{id}` · `DELETE /{id}`.
5. **DI** — registrar `IClassRepository` en `Infrastructure/DependencyInjection`.

### Migración
`Etapa10_1_GymClasses` — enum `classes.day_of_week`, tabla `classes.gym_classes`.

### Aceptación
- Crear una clase recurrente sin `days_of_week`, o una puntual sin `scheduled_date`, devuelve 422.
- `capacity = 0` devuelve 422; `capacity = null` se acepta.
- Dar de baja fija `deactivated_on` y saca la clase del listado activo.
- Aislamiento entre gyms en el listado (test).
- La suite completa (37 tests) sigue en verde.

---

# 10.2 — Horario proyectado y sesiones (`ClassSession`)

**Objetivo:** el staff y el socio ven el horario de un rango de fechas; una ocurrencia puntual se puede cancelar.

### Trabajo

1. **Domain** — `ClassSession` (`ITenantEntity`), enum `class_session_status` (`scheduled` · `cancelled`). Campos copiados de la definición al materializar (`start_time`, `duration_minutes`, `instructor_name`, `capacity`). `Cancel(reason)`.
2. **Application**
   - `ClassSchedule` — función pura: para `[from, to]` combina las ocurrencias calculadas (recurrentes por `days_of_week` entre `starts_on` y `deactivated_on`; puntuales por `scheduled_date`) con las sesiones ya materializadas, **que mandan siempre**.
   - `GetScheduleHandler` (staff) y `GetMyClassesHandler` (socio, con `show_classes` gate y el estado de sus reservas — reservas llegan en 10.3, de momento sin estado).
   - `MaterializeSession` — helper compartido: crea la fila `ClassSession` copiando la definición solo cuando hace falta (idempotente por `(class_id, session_date)`).
   - `CancelSessionHandler` — materializa con `status = cancelled`, publica `ClassSessionCancelled`. Las reservas afectadas se tratan en 10.3.
3. **Infrastructure** — `ClassSessionConfiguration` (índice único `(class_id, session_date)`), `ClassSessionRepository`.
4. **API** — `GET /api/classes/schedule?from=&to=`, `POST /api/classes/{id}/sessions/{date}/cancel`, `GET /api/me/classes?from=&to=`.

### Migración
`Etapa10_2_ClassSessions` — enum `class_session_status`, tabla `classes.class_sessions`.

### Aceptación
- Una clase recurrente L-X aparece en el horario de semanas futuras **sin materializar ninguna sesión**.
- Cancelar una ocurrencia deja `status = cancelled` sin tocar el resto de la serie.
- Dar de baja la clase detiene la recurrencia desde `deactivated_on` y conserva las sesiones pasadas.

---

# 10.3 — Reservas y asistencia (`ClassBooking`)

**Objetivo:** el socio reserva y cancela; el staff pasa lista. Se integra con Attendance por evento.

### Trabajo

1. **Domain** — `ClassBooking` (`ITenantEntity`), enum `class_booking_status` (`booked` · `cancelled` · `attended` · `no_show`). Transiciones: `Book` (reactiva desde `cancelled`), `Cancel`, `MarkAttended`, `MarkNoShow`. Índice único `(session_id, member_id)`.
2. **Application**
   - `BookClassHandler` — sin cupo (`capacity = null`) → 422. Con cupo, si `count(booked) == capacity` → 409. Materializa la sesión. Valida el socio contra `IMemberReader`. `membershipExpired` en la respuesta vía `IMembershipReader` (no bloquea). Gate `show_classes`.
   - `CancelBookingHandler` — permitido hasta la hora de inicio de la sesión.
   - `GetRosterHandler` (staff) — reservas y asistencia de una sesión.
   - `MarkAttendanceHandler` (staff) — reservados → `attended` / `no_show`; en clase sin cupo añade socios directamente en `attended`. Publica `ClassAttendanceRegistered(MemberId, GymId, SessionDate)` por cada asistente.
   - `GetMyClassHistoryHandler` (socio).
   - `CancelSessionHandler` (de 10.2) pasa las reservas `booked` a `cancelled`.
3. **Attendance** — `IDomainEventHandler<ClassAttendanceRegistered>` que registra el check-in del día (idempotente por día, ya lo es desde la etapa 6). Attendance **no** referencia Classes: solo añade el handler.
4. **Public** — `Classes/Public/IClassReader`: `GetAttendedCountAsync(memberId)`, `GetUpcomingBookingsAsync(memberId, limit)`, `GetTodaySessionCountAsync(gymId)`.
5. **API** — `POST/DELETE /api/classes/{id}/sessions/{date}/book`, `GET /api/classes/{id}/sessions/{date}/roster`, `POST /api/classes/{id}/sessions/{date}/attendance`, `GET /api/me/classes/history`. `MeController` gana los endpoints del socio; `MemberSelfAccess` reutilizado.

### Migración
`Etapa10_3_ClassBookings` — enum `class_booking_status`, tabla `classes.class_bookings`.

### Aceptación
- Reservar materializa la sesión una sola vez; dos reservas simultáneas no crean dos sesiones (índice único).
- Cupo 10, 10 reservas activas → la 11.ª recibe 409; si una cancela, vuelve a haber sitio.
- Clase sin cupo: reservar → 422; marcar asistencia funciona.
- Cancelar la ocurrencia pasa sus reservas a `cancelled`.
- Marcar asistencia registra el check-in del día; si el socio ya había fichado, no se duplica.
- Con `show_classes = false` el socio recibe 403 y el staff sigue operando.
- Aislamiento entre gyms en clases, sesiones y reservas.

---

# 10.4 — Gamificación

**Objetivo:** asistir a clases puntúa para logros.

### Trabajo

1. **Achievements** — nuevo `condition_type` **`classes`** (CLR `Classes`, label `classes` por el traductor snake_case):
   - `ALTER TYPE achievements.condition_type ADD VALUE 'classes'` — **`ADD VALUE` no se revierte**: el `Down` no puede quitarlo, documentarlo en el archivo.
   - `IDomainEventHandler<ClassAttendanceRegistered>` que dispara `AchievementService.EvaluateForMemberAsync`.
   - `AchievementService` / `AchievementMetrics` leen también `IClassReader` (`GetAttendedCountAsync`).
   - `Achievement.IsMetBy` gana la métrica de clases.
2. **API** — sin endpoints nuevos; el dashboard puede consumir `GetTodaySessionCountAsync`.

### Migración
`Etapa10_4_ClassesAchievements` — valor del enum `condition_type`.

### Aceptación
- Asistir a clases desbloquea un logro de tipo `classes`.
- Los logros existentes (visits, streak, routine_sessions) siguen funcionando.

---

## Resumen de migraciones

| Sub-etapa | Migración | Contenido |
|---|---|---|
| 10.1 | `Etapa10_1_GymClasses` | enum `day_of_week`, tabla `gym_classes` |
| 10.2 | `Etapa10_2_ClassSessions` | enum `class_session_status`, tabla `class_sessions` |
| 10.3 | `Etapa10_3_ClassBookings` | enum `class_booking_status`, tabla `class_bookings` |
| 10.4 | `Etapa10_4_ClassesAchievements` | valor `classes` en `condition_type` |

## Orden y dependencias

```
10.1 definiciones ──> 10.2 horario+sesiones ──> 10.3 reservas+asistencia ──> 10.4 gamificación
```

Estrictamente secuencial. El corte natural para pausar es **al final de 10.3**: en ese punto el módulo ya es usable end-to-end y 10.4 es solo gamificación.
