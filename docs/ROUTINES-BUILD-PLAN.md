# Plan de construcción — Módulo Routines (Etapa 9)

> **Estado: completado.** Todas las sub-etapas de la etapa 9 están construidas y validadas; este documento se conserva como registro de cómo se hizo. El «Punto de partida» describe el proyecto al arrancar esta etapa, no el actual; el estado vigente está en `CLAUDE.md`.

> Documento de trabajo para Claude Code. La **especificación funcional** está en `docs/GSM-modulo-routines.md`; este archivo dice en qué orden se construye. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`: **si algo aquí los contradice, ellos mandan.**
>
> **Ritmo:** una sub-etapa por vez, sin empezar la siguiente sin confirmación explícita. Cada una termina compilando, con su migración aplicada, sus endpoints respondiendo y sus tests en verde.

---

## Punto de partida

Etapas 0–8 completas salvo el despliegue (etapa 8, punto 4). 17 tests de integración en verde. Convenciones ya asentadas que este módulo hereda **sin excepción**:

- `IClock` para toda lectura de tiempo: `UtcNow` para instantes, `Today` para el día de negocio. Nunca `DateTime.UtcNow` suelto.
- `LowerCaseNameTranslator.Instance` (nunca `new`) al mapear enums de Postgres.
- Un caso de uso por archivo, con `HandleAsync`, inyectado directo en el controller.
- Comunicación entre módulos solo por `Public/` o eventos. La excepción de escritura cruzada está documentada en `CLAUDE.md`.
- Migración por sub-etapa, nombrada `Etapa09_{n}_{Nombre}`.
- Errores por la jerarquía de `DomainException`: 422 invariante, 409 conflicto, 403 prohibido, 404 no encontrado. 400 solo para forma del request.

**Riesgo transversal a vigilar:** este módulo es el primero que toca el Global Query Filter (con `Exercise`) y el primero que añade dependencias hacia módulos del MVP ya cerrados. Cada sub-etapa debe re-correr la suite completa, no solo sus tests.

---

# 9.1 — Catálogo de ejercicios

**Objetivo:** el catálogo híbrido existe, se puede consultar y administrar, y respeta el aislamiento entre gyms.

### Trabajo

1. **Domain** — `Exercise` (con `GymId` nullable), enums `Muscle` y `Equipment`. Invariantes en el factory: `primary_muscle` obligatorio, `secondary_muscles` sin repetir el primario, nombre de 2 a 120 caracteres.
2. **Infrastructure** — `ExerciseConfiguration` con los enums de Postgres como arrays y el índice único `(gym_id, name)`. Registrar los dos enums en `GsmDbContext` **y** en `DependencyInjection` **y** en `GsmDbContextFactory` (los tres sitios, o las migraciones fallan).
3. **`ExerciseRepository` con el filtro híbrido explícito.** `Exercise` **no** implementa `ITenantEntity`: un único método privado `VisibleTo(gymId)` aplica `gym_id IS NULL OR gym_id = @gymId`, y **toda** consulta del repositorio pasa por él.
4. **Application** — casos de uso de listar (con filtros `search`, `primaryMuscle`, `equipment`), obtener, crear, actualizar y desactivar. La autorización de "global vs del gym" se resuelve **en el caso de uso**, no solo con la policy del controller: un `staff` que toque una fila con `gym_id = null` recibe `403`.
5. **API** — `ExercisesController`, rutas bajo `/api/exercises`.
6. **Migración de datos con ~40 ejercicios del sistema.** Va en la migración, no en `DevSeeder`: tiene que existir en producción. Cubrir todos los grupos musculares principales, con `gym_id = null`.

### Migración
`Etapa09_1_ExerciseCatalog` — enums `muscle` y `equipment`, tabla `routines.exercises`, seed de datos.

### Aceptación
- Un gym ve los globales y los suyos; **nunca** los de otro gym (test automatizado).
- Un `staff` que intenta editar un ejercicio global recibe `403`; un `superadmin`, `200`.
- Los ~40 ejercicios del sistema existen tras aplicar la migración en una base limpia.
- La suite completa sigue en verde: `Exercise` fuera de `ITenantEntity` no debe alterar el filtro de los demás módulos.

---

# 9.2 — Rutinas, días y ejercicios

**Objetivo:** el entrenador arma la rutina de un miembro y el miembro la ve.

### Trabajo

1. **Domain** — `Routine`, `RoutineDay`, `RoutineExercise`. Invariantes: al menos un día por rutina; `position` consecutiva desde 1; un día con `is_rest = true` no admite ejercicios; `target_sets >= 1`.
2. **Infrastructure** — configuraciones con FK internas en cascada e índices `(routine_id, position)` y `(routine_day_id, order)`.
3. **Application**
   - `AssignRoutineHandler` — crea rutina, días y líneas **en una transacción** (`IUnitOfWork.ExecuteInTransactionAsync`). Valida el miembro contra `IMemberReader` y **cada** `exercise_id` contra el catálogo visible del gym. `assigned_by` de `ICurrentUserService`, `self_assigned = false`, `starts_on = clock.Today`.
   - `UpdateRoutineHandler` — reemplaza días y líneas, también transaccional.
   - `DeactivateRoutineHandler` — baja lógica, fija `deactivated_on`.
   - `GetRoutineHandler`, `ListMemberRoutinesHandler`.
   - `GetMyRoutineHandler` — rutina activa del miembro **con qué toca hoy**, resolviendo la posición del ciclo.
4. **Cálculo del ciclo** — `RoutineCycle.PositionOn(startsOn, cycleLength, date)`, función pura y sin estado, en Application. Aritmética de módulo que maneje correctamente fechas anteriores a `starts_on`.
5. **API** — `RoutinesController` y `MemberRoutinesController`. `MeController` gana `GET /api/me/routines`.
6. **Feature flag** — con `show_routines = false`, los endpoints del miembro responden `403`; los de staff siguen abiertos.

### Migración
`Etapa09_2_Routines` — `routines.routines`, `routines.routine_days`, `routines.routine_exercises`.

### Aceptación
- Crear una rutina cuya línea referencia un ejercicio de otro gym falla y **no deja rutina a medias** (test de rollback, como el de alta de gym).
- Con ciclo `[D1·D2·D3·descanso]` empezando un lunes: lunes→D1, jueves→D1 otra vez, miércoles→D3, y el domingo de la segunda vuelta cae en día de entrenamiento (el ciclo se desplaza, es lo esperado).
- Un miembro con `show_routines = false` recibe `403`; el staff no.
- Un `member` no puede ver la rutina de otro miembro.

---

# 9.3 — Seguimiento del miembro

**Objetivo:** el miembro marca lo que hace y, si quiere, anota la carga.

### Trabajo

1. **Domain** — `RoutineExerciseLog`, con los tres campos de carga opcionales.
2. **Infrastructure** — índice **único `(routine_exercise_id, performed_on)`**, más `(member_id, performed_on)`.
3. **Application**
   - `LogRoutineExerciseHandler` — marcar y registrar son la misma operación. **Idempotente por día:** si ya existe registro para esa fecha, actualiza. `performed_on = clock.Today`.
   - `UnlogRoutineExerciseHandler` — borra el registro de una fecha.
   - `GetMyRoutineHistoryHandler` — rango de fechas, con proyección explícita.
   - Publicar `RoutineSessionLogged` **solo la primera vez** que el miembro registra algo en un día dado: comprobar si ya había registros suyos con ese `performed_on` antes de insertar.
4. **API** — endpoints de log bajo `/api/routine-exercises/{id}/log` y el histórico en `MeController`.

### Migración
`Etapa09_3_RoutineLogs` — `routines.routine_exercise_logs`.

### Aceptación
- Registrar dos veces el mismo ejercicio el mismo día **actualiza**, no duplica.
- `RoutineSessionLogged` se publica **una sola vez** por día aunque se registren cinco ejercicios.
- Un registro a las 22:00 hora Colombia cuenta en el día local, no en el de UTC (mismo patrón que el test de caja).
- Un `member` no puede registrar sobre la rutina de otro.

---

# 9.4 — Racha contra el calendario de la rutina

**Objetivo:** el descanso planificado deja de romper la racha.

> Es la sub-etapa con más riesgo: cambia el comportamiento de un módulo del MVP ya cerrado. Los tests de racha existentes deben seguir pasando **sin tocarlos**.

### Trabajo

1. **Generalizar `StreakCalculator`** (Attendance) para aceptar un calendario esperado opcional. **Sin calendario, comportamiento idéntico al actual** — es la garantía de que nadie pierde su racha.
   - Con calendario: descanso → día bueno; entrenamiento con asistencia → día bueno; entrenamiento sin asistencia → corta.
   - **El día en curso nunca penaliza:** si hoy toca entrenar y no hay asistencia todavía, la evaluación arranca en ayer.
   - La racha cuenta **días de calendario**, no entrenamientos.
2. **`IRoutineReader.GetTrainingCalendarAsync(memberId, from, to)`** — qué días tocaba entrenar, resolviendo la rutina vigente en cada fecha (`starts_on` / `deactivated_on`).
3. **`GetMemberStreakHandler`** — caso de uso compositor, siguiendo el precedente del Dashboard: junta `IAttendanceReader` e `IRoutineReader`. **Attendance no debe referenciar Routines.**
4. **API** — `GET /api/members/{id}/streak` pasa a resolverse por este handler.

### Migración
Ninguna.

### Aceptación
- Ciclo `[D1·D2·D3·descanso]`, el miembro entrena los tres días y no va el de descanso: **4 días de racha por ciclo**; dos ciclos completos → racha 8.
- Faltar a un día de entrenamiento corta la racha (todavía sin congeladores).
- Hoy es día de entrenamiento y aún no ha ido → misma racha que ayer.
- Un miembro **sin** rutina conserva exactamente la racha de antes: los tests de racha del MVP pasan sin modificarse.

---

# 9.5 — Congelador de racha

**Objetivo:** un imprevisto no arruina un mes de constancia.

### Trabajo

1. **Domain (schema `achievements`)** — `StreakFreeze`: `granted_on`, `used_on` nullable. Vive aquí, no en Routines, porque **aplica también a miembros sin rutina**.
2. **Infrastructure** — índice `(member_id, used_on)` y **único parcial** `(member_id, used_on) WHERE used_on IS NOT NULL`: es lo que hace idempotente el consumo.
3. **Application**
   - Saldo = `count(used_on IS NULL)`, tope 2.
   - **Concesión perezosa:** al consultar la racha, si han pasado ≥ 15 días desde el último `granted_on` y hay menos de 2 disponibles, se otorga y se persiste. No hay scheduler y no se va a introducir uno.
   - **Consumo:** al encontrar un día de entrenamiento fallado, marcar un congelador con `used_on` = esa fecha y continuar el recorrido.
   - Exponerlo por `IAchievementService` (ya existe como escritura cruzada documentada).
4. **API** — `GET /api/me/streak` devuelve racha y congeladores disponibles.

> **Nota de diseño asumida:** consultar la racha puede escribir. Es deliberado — sin proceso diario no hay otro momento donde detectar que un miembro faltó, precisamente porque no vino. Los índices únicos garantizan que sea idempotente.

### Migración
`Etapa09_5_StreakFreezes` — `achievements.streak_freezes`.

### Aceptación
- Un fallo con congelador disponible **no** corta la racha, y el congelador queda marcado con la fecha fallada.
- Un segundo fallo sin saldo **sí** corta.
- **Consultar la racha dos veces no gasta dos congeladores** por el mismo día fallado.
- El saldo nunca pasa de 2, por muchos días que transcurran.

---

# 9.6 — Gamificación y auto-servicio

**Objetivo:** las sesiones puntúan y cada gym decide si sus miembros se arman rutinas.

### Trabajo

1. **Achievements** — nuevo `condition_type` **`routine_sessions`**:
   - `ALTER TYPE achievements.condition_type ADD VALUE 'routine_sessions'` (los `ADD VALUE` de Postgres no se revierten: la migración `Down` no puede quitarlo, documentarlo en el archivo).
   - `IDomainEventHandler<RoutineSessionLogged>` que dispare la evaluación.
   - `AchievementService` lee también `IRoutineReader`.
2. **Gyms** — añadir `members_can_create_routines` (default `false`) a `GymFeatureFlags`, y exponerlo en los endpoints de flags.
3. **Auto-servicio** — `POST /api/me/routines`, con `403` si el flag está apagado; crea la rutina con `self_assigned = true`.
4. **Exclusión de logros** — las rutinas con `self_assigned = true` **no** cuentan: ni para `routine_sessions`, ni para la racha que puntúa logros. `GetCompletedSessionCountAsync(memberId, onlyAssigned)` y el filtro en el calendario de racha.

### Migración
`Etapa09_6_RoutineGamification` — valor del enum + columna del feature flag.

### Aceptación
- Registrar sesiones desbloquea un logro de tipo `routine_sessions`.
- Una sesión de rutina con `self_assigned = true` **no** incrementa el contador ni desbloquea nada.
- Con `members_can_create_routines = false`, `POST /api/me/routines` responde `403`.
- Los logros de racha existentes (7 y 30) siguen funcionando y ahora son alcanzables con una rutina normal.

---

## Resumen de migraciones

| Sub-etapa | Migración | Contenido |
|---|---|---|
| 9.1 | `Etapa09_1_ExerciseCatalog` | enums `muscle`/`equipment`, `exercises`, seed de ~40 |
| 9.2 | `Etapa09_2_Routines` | `routines`, `routine_days`, `routine_exercises` |
| 9.3 | `Etapa09_3_RoutineLogs` | `routine_exercise_logs` |
| 9.4 | — | sin cambios de esquema |
| 9.5 | `Etapa09_5_StreakFreezes` | `achievements.streak_freezes` |
| 9.6 | `Etapa09_6_RoutineGamification` | `condition_type` + `members_can_create_routines` |

## Tests que deja el módulo

Además de re-correr los 17 existentes en cada sub-etapa:

- **Tenancy del catálogo híbrido** — el caso nuevo, porque `Exercise` queda fuera del filtro global.
- **Rollback** al crear una rutina inválida.
- **Ciclo rotativo** — posición correcta a lo largo de dos vueltas.
- **Idempotencia** del registro diario y de la publicación del evento.
- **Racha con calendario** — ciclo completo, fallo, día en curso, miembro sin rutina.
- **Congelador** — consume, agota, no se gasta dos veces por la misma fecha.
- **Exclusión** de rutinas auto-asignadas de la gamificación.

## Orden y dependencias

```
9.1 catálogo ──> 9.2 rutinas ──> 9.3 seguimiento ──> 9.4 racha ──> 9.5 congelador ──> 9.6 gamificación
```

Estrictamente secuencial. El corte natural para pausar es **al final de 9.3**: en ese punto el módulo ya es usable de cara al miembro (ve su rutina y registra lo que hace) y todo lo que sigue es gamificación.
