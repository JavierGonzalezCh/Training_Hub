# Módulo Routines — Especificación

> Fase 2, primer módulo fuera del MVP. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`; **si algo aquí contradice a esos dos, ellos mandan.** Sigue las convenciones transversales de `docs/BACKEND-MVP-PLAN.md` (IDs Guid v7, schema propio, `IClock` para fechas, jerarquía de `DomainException`, casos de uso con `HandleAsync`, comunicación entre módulos solo por `Public/` o eventos).

**Schema:** `routines` · **Toca también:** `achievements` (congelador de racha) y `gyms` (feature flag)

---

## Decisiones de producto

Cerradas antes de especificar, porque definen el modelo:

1. **Asignación directa, sin plantillas.** El entrenador arma la rutina eligiendo ejercicios del catálogo.
2. **Catálogo híbrido.** Un catálogo del sistema compartido, más los ejercicios propios de cada gym.
3. **Seguimiento opcional.** El socio marca el ejercicio y, si quiere, anota lo que levantó.
4. **La rutina es un ciclo rotativo** de entrenamiento y descanso, no un horario semanal.
5. **La racha cuenta días de calendario cumplidos.** Un descanso respetado es un día bueno igual que un entrenamiento hecho: "racha de 30" son 30 días reales.
6. **Congelador explícito** para imprevistos: uno cada 15 días, máximo dos, visible.
7. **El auto-servicio lo decide cada gym** por feature flag, y lo que el socio se asigne a sí mismo **no cuenta para logros**.

---

## Entidades

### `Exercise` — catálogo híbrido

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid?` | **`null` = ejercicio del sistema**, visible para todos. Con valor = propio de ese gym. |
| `name` | `string(120)` | |
| `description` | `string(1000)?` | Cómo se ejecuta. |
| `primary_muscle` | enum `muscle` | Obligatorio, uno solo. |
| `secondary_muscles` | `muscle[]` | Cero o más. No puede contener el primario. |
| `equipment` | `equipment[]` | Cero o más. Vacío = solo peso corporal. |
| `video_url` | `string(2048)?` | |
| `image_url` | `string(2048)?` | |
| `is_active` | `bool` | Baja lógica. |

**Enum `muscle`:** `chest`, `back`, `shoulders`, `biceps`, `triceps`, `forearms`, `quadriceps`, `hamstrings`, `glutes`, `calves`, `abs`, `lower_back`, `neck`, `full_body`, `cardio`.

**Enum `equipment`:** `barbell`, `dumbbell`, `kettlebell`, `machine`, `cable`, `smith_machine`, `bench`, `rack`, `resistance_band`, `trx`, `medicine_ball`, `mat`, `box`, `jump_rope`, `bodyweight`, `cardio_machine`.

> Conjuntos cerrados y estables (anatomía y categorías de implemento, no marcas). Ampliarlos es un `ALTER TYPE ... ADD VALUE`, migración trivial. El detalle fino ("la máquina Hammer del segundo piso") va en `description`, no en el enum.

**Índices:** único `(gym_id, name)`; más `(primary_muscle)`.

### `Routine`

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `member_id` | `Guid` | Validado contra `IMemberReader`, sin FK cruzada. |
| `name` | `string(120)` | |
| `notes` | `string(1000)?` | |
| `assigned_by` | `Guid` | Siempre de `ICurrentUserService`, **nunca** del body. |
| `self_assigned` | `bool` | La creó el propio socio. **Las rutinas con `true` no puntúan para logros.** Campo explícito y no derivado de comparar `assigned_by`: la regla de gamificación no debe depender de una inferencia. |
| `starts_on` | `DateOnly` | **Ancla del ciclo**: el día 1 cae aquí. Día de negocio, de `IClock.Today`. |
| `is_active` | `bool` | Baja lógica. |
| `deactivated_on` | `DateOnly?` | Cierra la vigencia; necesario para recorrer la racha hacia atrás. |

**Índice:** `(gym_id, member_id, is_active)`.

### `RoutineDay` — un eslabón del ciclo

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `routine_id` | `Guid` | FK interna, `ON DELETE CASCADE`. |
| `position` | `int` | 1..N, posición en el ciclo. |
| `name` | `string(60)?` | Opcional. La app muestra `name ?? "Día {position}"`. |
| `is_rest` | `bool` | Día de descanso: sin ejercicios y sin asistencia esperada. |

**Índice único:** `(routine_id, position)`.

> El descanso es una fila con `is_rest = true`, no la ausencia de fila. Es la pieza que permite distinguir **"hoy no le tocaba"** de **"hoy faltó"**; sin ella, ambas cosas son idénticas en la base y toda la racha se cae.

### `RoutineExercise`

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `routine_day_id` | `Guid` | FK interna, `ON DELETE CASCADE`. |
| `exercise_id` | `Guid` | Al catálogo. Sin FK: `exercises` admite filas globales. Se valida en el caso de uso. |
| `order` | `int` | Orden dentro del día. |
| `target_sets` | `int` | ≥ 1. |
| `target_reps` | `string(30)` | Texto: admite `12`, `8-10`, `al fallo`. |
| `target_weight_kg` | `decimal(6,2)?` | |
| `rest_seconds` | `int?` | |
| `notes` | `string(500)?` | |

**Índice:** `(routine_day_id, order)`.

### `RoutineExerciseLog`

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `member_id` | `Guid` | Denormalizado: casi toda consulta de progreso parte del socio. |
| `routine_exercise_id` | `Guid` | FK interna. |
| `performed_on` | `DateOnly` | Día de negocio, no UTC. |
| `completed_at` | `DateTimeOffset` | Instante real. |
| `sets_done` | `int?` · `reps_done` `string(30)?` · `weight_kg` `decimal(6,2)?` | Opcionales. |
| `notes` | `string(500)?` | |

**Índice único `(routine_exercise_id, performed_on)`** — un registro por ejercicio por día; repetir **actualiza**, no duplica. Más `(member_id, performed_on)`.

> Marcar y anotar son la misma operación: un registro con los tres campos de carga en `null` es simplemente "hecho".

---

## El ciclo rotativo

Los `N` días de la rutina se repiten desde `starts_on`, sin atarse a la semana:

```
3 días  →  [ Día 1 · Día 2 · Día 3 · Descanso ]                  ciclo de 4
5 días  →  [ D1 · D2 · D3 · Descanso · D4 · D5 · Descanso ]      ciclo de 7
```

Qué toca un día concreto es una función pura:

```
posición(fecha) = ((fecha − starts_on) mod N) + 1
```

Sin estado que mantener: la racha se recomputa entera en cualquier momento sin haber guardado nada día a día.

**Consecuencia asumida:** el ciclo se desplaza. Uno de 4 días que empieza lunes vuelve a empezar en viernes, y con el tiempo tocará entrenar domingos. Es lo propio de un *push/pull/legs*; si algún gym pide horario semanal fijo, se añade como segunda modalidad sin romper esta.

---

## Racha

### Qué cuenta

**Días de calendario cumplidos**, recorriendo hacia atrás desde hoy:

| Qué tocaba | Qué pasó | Efecto |
|---|---|---|
| Descanso | — | **día bueno**, suma |
| Entrenamiento | asistió | **día bueno**, suma |
| Entrenamiento | no asistió | gasta congelador; si no hay, **corta** |

Así "racha de 30" son 30 días reales, sea cual sea la rutina, y los umbrales de los logros existentes (7 y 30) siguen significando lo que dicen.

**El día en curso nunca penaliza.** Si hoy toca entrenar y aún no ha ido, la evaluación arranca en ayer.

El recorrido se detiene en `starts_on` de la rutina vigente; si el socio cambió de rutina, cada fecha se evalúa con la que estaba activa ese día (de ahí `deactivated_on`).

**Sin rutina activa** se mantiene la regla actual del `StreakCalculator` (tolerancia de días de descanso). Nadie pierde la racha que ya tiene, y los gyms con `show_routines` apagado siguen con gamificación.

> **Contrapartida conocida:** una rutina laxa (un entrenamiento y tres descansos) acumula racha tan rápido como una de cinco días. Lo que impide abusar es que las rutinas las asigna el entrenador y que las auto-asignadas no puntúan.

### Congelador

Vive en el schema `achievements`: es mecánica de juego y aplica **también a socios sin rutina**, así que no puede vivir en Routines.

**`StreakFreeze`** — una fila por congelador:

| Campo | Tipo | Notas |
|---|---|---|
| `id` | `Guid` | |
| `gym_id` | `Guid` | |
| `member_id` | `Guid` | |
| `granted_on` | `DateOnly` | |
| `used_on` | `DateOnly?` | `null` = disponible. |

**Índices:** `(member_id, used_on)` y único `(member_id, used_on)` filtrado a `used_on IS NOT NULL` — un solo congelador por fecha fallada, que es lo que hace idempotente el consumo.

**Reglas:**

- Se concede **uno cada 15 días**, con **tope de dos disponibles**. El saldo es `count(used_on IS NULL)`.
- Se consume automáticamente al encontrar un día de entrenamiento fallado, marcando `used_on` con esa fecha.
- Guardar *cuándo* se gastó es lo que mantiene la racha **recalculable desde cero**: al recorrer hacia atrás, un fallo con congelador gastado ese día simplemente se salta.

**Concesión perezosa.** No hay scheduler en el sistema (decisión del MVP, etapa 4) y no lo va a haber por esto. La concesión se evalúa al vuelo cuando se consulta la racha: si han pasado 15 días o más desde el último `granted_on` y hay menos de dos disponibles, se otorga y se persiste.

> **Efecto de escritura en una lectura.** Consultar la racha puede crear o consumir filas de `StreakFreeze`. Es deliberado: sin proceso diario, no hay otro momento donde detectar un fallo de un socio que precisamente *no vino*. Los índices únicos hacen la operación idempotente, así que dos consultas concurrentes no duplican nada.

### Dónde vive el cálculo

Attendance es el módulo 6 y Routines el 8: **Attendance no puede depender de Routines.**

Se resuelve con el precedente del Dashboard —un caso de uso que compone `Public/` de varios módulos sin pertenecer a ninguno:

- `StreakCalculator` (Attendance) se generaliza para aceptar un **calendario esperado** opcional. Sin él, se comporta exactamente como hoy.
- `IRoutineReader.GetTrainingCalendarAsync(memberId, from, to)` dice qué días tocaba entrenar.
- `IAchievementService` expone el saldo y el consumo de congeladores.
- Un **`GetMemberStreakHandler`** compone los tres. `GET /api/members/{id}/streak` pasa a resolverse por él.

Attendance no se entera de que Routines existe, y la regla vieja sigue siendo el camino por defecto.

---

## El catálogo híbrido

`Exercise` es la primera entidad del sistema que **no pertenece a un solo gym**.

**Visibilidad:** el gym ve los de `gym_id IS NULL` más los suyos. Nunca los de otro gym.

**`Exercise` no implementa `ITenantEntity`.** El Global Query Filter compara `GymId` por igualdad estricta y aquí hace falta `gym_id IS NULL OR gym_id = @current`. Antes que meter un caso especial en esa pieza —infraestructura crítica compartida por los siete módulos— el filtrado va **explícito en `ExerciseRepository`**, en un único método privado que atraviesan todas sus consultas.

| Acción | Autorización |
|---|---|
| Listar / buscar | Cualquier autenticado del gym |
| Crear, editar, desactivar uno **del gym** | `RequireStaff` |
| Crear, editar, desactivar uno **del sistema** | `RequireSuperAdmin` |

El módulo SuperAdmin es Fase 3, pero **el rol y la policy existen desde la etapa 1**: el catálogo global se administra desde ya.

**Un gym nunca modifica un ejercicio del sistema.** Si necesita variarlo, crea el suyo; el caso de uso devuelve `403` si un staff intenta editar una fila con `gym_id = null`.

**Siembra inicial:** ~40 ejercicios comunes por **migración de datos**, no por seed de desarrollo: tienen que existir también en producción.

---

## Integración con los demás módulos

**Attendance (6)** — `StreakCalculator` acepta un calendario opcional; sin él, comportamiento idéntico. `GET /members/{id}/streak` pasa al handler compositor. Routines consume `IAttendanceReader` para saber qué días hubo asistencia.

**Achievements (7)** — recibe dos cosas:

- **El congelador** (entidad `StreakFreeze`, saldo y consumo), expuesto por `IAchievementService`.
- Un `condition_type` nuevo, **`routine_sessions`** → `ALTER TYPE achievements.condition_type ADD VALUE 'routine_sessions'`, un `IDomainEventHandler<RoutineSessionLogged>` que dispare la evaluación, y `AchievementService` leyendo también `IRoutineReader`.
- **Las sesiones de rutinas con `self_assigned = true` no cuentan** ni para `routine_sessions` ni para la racha que puntúa logros.

Es otra escritura cruzada hacia Achievements, del mismo tipo que la del check-in: encaja en la excepción ya documentada en `CLAUDE.md`.

**Gyms (2)** — `show_routines` ya existe. Se añade **`members_can_create_routines`** (default `false`) a `GymFeatureFlags`: una migración de una columna.

**Members (3)** — validación del socio por `IMemberReader`, sin FK entre esquemas.

**Identity (1)** — `RequireStaff`, `RequireOwner` y `RequireSuperAdmin` tal cual están.

---

## Comunicación

**`Public/IRoutineReader`:**

- `GetActiveRoutineCountAsync(memberId)`
- `GetCompletedSessionCountAsync(memberId, bool onlyAssigned)` — días distintos con algo registrado; `onlyAssigned` excluye las auto-asignadas para gamificación.
- `GetLastSessionAsync(memberId)` → `DateOnly?`
- `GetTrainingCalendarAsync(memberId, from, to)` — qué días tocaba entrenar.

**Eventos** (publicados después del commit, nunca dentro):

- `RoutineAssigned(RoutineId, MemberId, GymId, SelfAssigned)`
- `RoutineSessionLogged(MemberId, GymId, PerformedOn, SelfAssigned)` — solo la **primera** vez que el socio registra algo en un día dado, para contar sesiones y no ejercicios sueltos.

**Consume:** `IMemberReader`, `IGymReader` (flags), `IAttendanceReader` (asistencias).

---

## Endpoints

**Catálogo**

```
GET    /api/exercises      ?search=&primaryMuscle=&equipment=&includeInactive=   [autenticado]
GET    /api/exercises/{id}                                                       [autenticado]
POST   /api/exercises                                    [Staff · SuperAdmin para globales]
PUT    /api/exercises/{id}                               [Staff · SuperAdmin para globales]
DELETE /api/exercises/{id}   baja lógica                 [Staff · SuperAdmin para globales]
```

**Rutinas (staff)**

```
GET    /api/members/{memberId}/routines
POST   /api/members/{memberId}/routines    rutina, días y líneas en una transacción
GET    /api/routines/{id}
PUT    /api/routines/{id}                  reemplaza cabecera, días y líneas
DELETE /api/routines/{id}                  baja lógica, fija deactivated_on
```

**Socio**

```
GET    /api/me/routines                        rutina activa + qué toca hoy (o descanso)
POST   /api/me/routines                        solo si members_can_create_routines; self_assigned = true
POST   /api/routine-exercises/{id}/log         marcar y/o registrar carga (idempotente por día)
DELETE /api/routine-exercises/{id}/log?date=   desmarcar
GET    /api/me/routines/history?from=&to=
GET    /api/me/streak                          racha + congeladores disponibles
```

Un rol `member` solo alcanza sus propios datos; se reutiliza el patrón `MemberSelfAccess` existente.

**Feature flags:** con `show_routines = false`, los endpoints del socio responden `403` y los de staff siguen abiertos (para preparar rutinas antes de encender el módulo). `POST /api/me/routines` responde `403` si `members_can_create_routines = false`.

---

## Criterios de aceptación

1. Un gym ve los ejercicios del sistema y los suyos; **nunca** los de otro gym. Test automatizado, en la línea del de tenancy.
2. Un staff que intenta editar un ejercicio del sistema recibe `403`.
3. Crear una rutina con sus días y líneas es atómico: si una línea referencia un ejercicio inexistente o de otro gym, no queda rutina a medias.
4. Registrar dos veces el mismo ejercicio el mismo día **actualiza**, no duplica, y `RoutineSessionLogged` se publica una sola vez ese día.
5. El día del registro es el día de negocio del gym: un registro a las 22:00 en Bogotá cuenta en el día local, no en el de UTC.
6. **Racha, ciclo 4 (`D1·D2·D3·descanso`):** el socio que entrena los tres días y no va el de descanso suma **4 días de racha por ciclo**. Test con dos ciclos completos → racha 8.
7. **Un fallo con congelador disponible no corta la racha**, y el congelador queda marcado con la fecha fallada. Un segundo fallo sin saldo, sí corta.
8. **Idempotencia del congelador:** consultar la racha dos veces no gasta dos congeladores por el mismo día fallado.
9. **La racha no se pierde por el día en curso:** hoy es día de entrenamiento y sin asistencia todavía → misma racha que ayer.
10. Un socio **sin** rutina conserva exactamente la racha que da la regla actual.
11. Una sesión de una rutina con `self_assigned = true` **no** incrementa `routine_sessions` ni desbloquea logros.
12. Con `show_routines = false`, el socio recibe `403` y el staff sigue operando.

---

## Fuera de alcance

- **Progreso del socio** (evolución de carga por ejercicio). Los datos quedan capturados en `RoutineExerciseLog`, así que es un módulo de lectura posterior, sin migración.
- **Plantillas de rutina** reutilizables. Es lo primero que pedirá un gym con muchos socios; se añade encima de este modelo copiando días y líneas.
- **Vídeos e imágenes del catálogo**: los campos existen, pero dependen de Cloudflare R2, que sigue **sin configurar** (etapa 8, punto pendiente).
- **Horario semanal fijo** como alternativa al ciclo rotativo.
