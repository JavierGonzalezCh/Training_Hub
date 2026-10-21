# Módulo Routines — Especificación

> Fase 2, primer módulo fuera del MVP. Complementa `CLAUDE.md` y `docs/GSM-especificacion-tecnica.md`; **si algo aquí contradice a esos dos, ellos mandan.** Sigue las convenciones transversales de `docs/DESARROLLO.md` (IDs Guid v7, schema propio, `IClock` para fechas, jerarquía de `DomainException`, casos de uso con `HandleAsync`, comunicación entre módulos solo por `Public/` o eventos).

**Schema:** `routines` · **Toca también:** `achievements` (congelador de racha) y `gyms` (feature flag)

---

## Replanteamiento v2 — construido

> Nada de esta sección está construido. Las secciones que vienen después de ella describen **la v1,
> que es lo construido hoy**. Donde las dos se contradicen, manda esta sección una vez aprobada.

### Decisiones tomadas

| Tema | Decisión |
|---|---|
| Calendario | **Dos modos**, se elige al crear la rutina: **semanal** (cada día de entrenamiento se ata a un día de la semana) y **rotativa** (el ciclo Día 1…N de la v1). La racha respeta los descansos en los dos. |
| Series | **Se planean una por una**: cada serie tiene su objetivo y su tipo (calentamiento, normal, descendente, al fallo). Varios ejercicios se pueden agrupar en **superserie o circuito**. |
| Objetivo de la serie | **Repeticiones, tiempo o distancia**, según el ejercicio. Las repeticiones son un rango mín–máx o "al fallo". Esfuerzo opcional en **RIR** (repeticiones en reserva). Ya no es texto libre: se puede comparar lo planeado con lo hecho. |
| Revisión | **Fecha de revisión, sin vencimiento.** Al asignar la rutina se fija una fecha (6 semanas por defecto, configurable por gym). El entrenador ve "rutinas por revisar". La rutina sigue activa aunque la fecha pase. |
| Salud | En el editor de rutinas, el entrenador ve el aviso "tiene alertas de salud" y, si tiene el permiso `ViewMemberHealth`, también el detalle (ver `GSM-modulo-members.md` §7). |
| Edición | **Se edita en el lugar** y el historial de entrenamiento nunca se pierde. Corrige el bug **B1** (`docs/BUGS.md`). |
| Plantillas | Se construyen **al final del replanteamiento**, sobre este modelo. |
| Catálogo | Entran: medios subidos, músculos completos, tipo de ejercicio, qué se mide, unilateral, nivel, alternativas, ocultar y duplicar. **Sinónimos de búsqueda: descartados por ahora**, porque se complican rápido (decidido 2026-10-07). |

### Catálogo de ejercicios

**`Exercise`** — cambios:

| Campo | Tipo | Notas |
|---|---|---|
| `exercise_type` | enum `strength`, `cardio`, `mobility`, `stretching`, `plyometrics` | **Nuevo.** Tipo de ejercicio. Es filtro del catálogo y define el `tracking_type` sugerido. |
| `tracking_type` | enum `reps_weight`, `reps`, `time`, `distance`, `time_distance` | **Nuevo.** Qué se mide. Press de banca → `reps_weight`; dominadas → `reps`; plancha → `time`; correr → `time_distance`. Lo heredan las líneas de las rutinas y los registros. Sugerido por tipo: `cardio` → `time_distance`; `mobility` y `stretching` → `time`; el resto `reps_weight`. Se puede cambiar. |
| `is_unilateral` | `bool` | **Nuevo.** Se hace por lado (zancadas, remo a una mano). **Las repeticiones planeadas y registradas son por lado**; la app muestra "10 × lado". No se registra cada lado por separado. |
| `difficulty` | enum `beginner`, `intermediate`, `advanced` | **Nuevo.** Filtro del catálogo. |
| `primary_muscle`, `secondary_muscles` | enum `muscle` | **Cambia la lista** (abajo). |
| `image_url`, `video_url` | texto | Se mantienen, pero ahora **también se pueden subir archivos** (abajo). |

**Enum `muscle`, cambios:**

- **Se añaden** `traps` (trapecio), `obliques` (oblicuos), `adductors` (aductores) y `abductors`
  (abductores).
- **Se quita** `cardio`: deja de ser un músculo y pasa a ser `exercise_type = cardio`. Los ejercicios
  que hoy lo tienen como principal migran a `full_body` con tipo `cardio`.
- Postgres no permite quitar un valor de un enum: la migración crea el tipo nuevo, convierte las
  columnas y borra el viejo.
- La migración corrige los ejercicios del sistema mal clasificados por falta de músculo, como
  "Encogimientos de trapecio" → `traps`.

**Grupo muscular (zona del cuerpo)** — decidido 2026-10-07:

Un nivel más general que el músculo, para mirar primero "tren superior" y después afinar por músculo.
**Se deriva del músculo, no se guarda**: así nunca se contradicen.

| Grupo | Músculos |
|---|---|
| **Tren superior** (`upper_body`) | pecho, espalda, hombros, trapecio, bíceps, tríceps, antebrazos, cuello |
| **Tren inferior** (`lower_body`) | cuádriceps, isquiotibiales, glúteos, pantorrillas, aductores, abductores |
| **Core** (`core`) | abdomen, oblicuos, lumbar (lumbar en core: decidido 2026-10-07) |
| **Cuerpo completo** (`full_body`) | cuerpo completo |

- El grupo de un ejercicio es el de su **músculo principal**. Por ejemplo, el peso muerto rumano
  (isquiotibiales) es tren inferior, aunque también trabaje la lumbar.
- **Catálogo:** primero se elige el grupo y después aparecen solo sus músculos (filtro en dos pasos).
  En la lista se ve el grupo como etiqueta junto al músculo.
- **Editor de rutinas y app:** cada día de la rutina muestra los grupos que trabaja según sus
  ejercicios ("Tren superior · Core"). Si el día no tiene nombre, eso sirve de nombre sugerido.
- El mapa vive en el dominio (`Muscle → MuscleGroup`), igual para todos los gyms. Si un gym quisiera
  agrupar distinto, se revisa después.

**Medios (imagen y video):**

- Se mantiene la URL externa (YouTube, Vimeo) y se añade la **subida de archivos** a R2 por
  `IFileStorage`: imagen (JPG, PNG o WebP, hasta 5 MB) y video corto (MP4, hasta 50 MB).
- Los medios de los ejercicios del sistema los sube el SuperAdmin; los del gym, su staff.
- **Depende de que R2 esté configurado**, igual que las fotos de los miembros.
- Los 55 ejercicios del sistema no tienen ningún medio. **Conseguirlos es trabajo de contenido, no de
  código**, y no se pueden copiar de internet sin licencia.

**Alternativas** — tabla nueva `routines.exercise_alternatives`:

| Campo | Notas |
|---|---|
| `exercise_id`, `alternative_id` | Dos ejercicios visibles para el gym. La relación vale en los dos sentidos y se guarda una sola vez. |
| `gym_id` | `null` = alternativa del sistema (la define el SuperAdmin entre ejercicios del sistema). Con valor = la definió ese gym, entre cualquier ejercicio que vea. |

**Todo ejercicio sugiere alternativas** (decidido 2026-10-07), aunque nadie las haya definido. La
lista sale de dos fuentes, en este orden:

1. **Recomendadas:** las que el gym o el SuperAdmin guardaron en `exercise_alternatives`. Van primero
   y con la etiqueta "Recomendada".
2. **Sugeridas automáticamente,** calculadas al vuelo, sin guardar nada:
   - mismo **músculo principal** y mismo **tipo de ejercicio**;
   - se ordenan por: más músculos secundarios en común → mismo `tracking_type` → nivel igual o menor →
     nombre;
   - se excluyen los inactivos, los ocultos por el gym, **los que no se pueden hacer hoy en el gym**
     (máquina en mantenimiento o equipo que el gym no tiene; ver `GSM-modulo-equipment.md` §5) y el
     propio ejercicio;
   - si no salen al menos 3, se completa con ejercicios del **mismo grupo muscular** y mismo tipo.

Se muestran como máximo 6 entre las dos fuentes.

`GET /api/exercises/{id}/alternatives` devuelve la lista con su origen (`recommended` o `suggested`).

**Dónde se usan:**

- **Editor de rutinas:** cada línea ofrece **"Cambiar por…"**. El cambio queda en la rutina.
- **App del miembro: "Hacer otro ejercicio hoy"** (decidido 2026-10-07). El miembro puede
  reemplazar un ejercicio por una alternativa **solo ese día**, por ejemplo si la máquina está
  ocupada. La rutina no cambia.
  - El registro guarda `routine_exercise_id` (lo planeado) y `exercise_id` (lo que hizo de verdad).
    Si son distintos, fue un reemplazo; no hace falta otro campo.
  - Cuenta igual para la sesión del día, la racha y los logros.
  - El progreso se suma al ejercicio que hizo, no al planeado.
  - El entrenador lo ve en el historial del miembro: "Hizo *Prensa de piernas* en lugar de
    *Sentadilla con barra*". Si un mismo reemplazo se repite, es una pista para cambiar la rutina en
    la próxima revisión.
  - El miembro solo puede elegir de la lista de alternativas, no cualquier ejercicio del catálogo.

**Ocultar ejercicios del sistema** — tabla nueva `routines.gym_hidden_exercises` (`gym_id`,
`exercise_id`):

- Un gym oculta los ejercicios del sistema que no usa (por ejemplo, si no tiene multipower). Dejan de
  salir en el buscador y en el editor.
- **Las rutinas que ya los usan no cambian**, y se pueden volver a mostrar cuando se quiera.
- Solo aplica a ejercicios del sistema. Los del gym se desactivan, como hasta ahora.

**Duplicar:**

- `POST /api/exercises/{id}/duplicate` crea una copia **del gym** con todos los campos, sus medios
  (apuntando a los mismos archivos) y sus alternativas del sistema.
- El diálogo ofrece **"ocultar el original"** para que el gym quede solo con su versión.
- Es la forma de "editar" un ejercicio del sistema; la regla de que un gym nunca lo modifica sigue
  igual.

**Filtros del catálogo:** búsqueda por nombre (como hoy), **grupo muscular → músculo**, equipo,
**tipo**, **nivel**, "mostrar ocultos" y **"solo lo que se puede hacer en mi gym"**.

**Máquinas del gym** (decidido 2026-10-07): el inventario, el estado de cada máquina y los reportes de
fallas son un módulo nuevo, **Equipment** (`GSM-modulo-equipment.md`). El enum de equipo de los
ejercicios pasa a ser suyo (`EquipmentType`, mismos valores). Routines guarda qué máquina se usa para cada
ejercicio: los del sistema, ligados a los modelos del catálogo de máquinas
(`routines.exercise_equipment_models`), y los ajustes de cada gym (`routines.exercise_equipment_items`), y consulta su disponibilidad por
`IEquipmentReader`. En la app, cada ejercicio muestra la foto de la máquina real, su zona y sus
instrucciones de ajuste.

### Cambios en el modelo de rutinas

**`Routine`** — campos nuevos:

| Campo | Tipo | Notas |
|---|---|---|
| `schedule_mode` | enum `weekly`, `rotating` | Los existentes migran como `rotating`. |
| `review_on` | `DateOnly` | `starts_on` + las semanas de revisión configuradas en el gym. |
| `last_reviewed_at`, `last_reviewed_by` | instante, uuid | La acción "Marcar revisada" los rellena y corre `review_on` otras N semanas. |

**`RoutineDay`**:

- **Modo rotativo:** igual que en la v1 (`position` 1..N, `is_rest`).
- **Modo semanal:** cada día de entrenamiento lleva `day_of_week` (lunes a domingo, sin repetir).
  **Un día de la semana sin fila es descanso**: como la semana está completa, la ausencia no es
  ambigua (en el modo rotativo sí lo era, por eso allí el descanso es una fila explícita).

**`RoutineExercise`** (la línea de la rutina):

| Campo | Cambio |
|---|---|
| `target_sets`, `target_reps`, `target_weight_kg` | **Se quitan.** Los reemplaza `set_targets`. |
| `set_targets` | **Nuevo**, JSON en la misma fila, igual que el detalle por serie de los registros. Una entrada por serie: `number`, `set_type` (`warmup`, `normal`, `drop`, `failure`), `reps_min`, `reps_max`, `weight_kg`, `duration_seconds`, `distance_m`, `rir`. Solo se usan los campos que corresponden al `tracking_type` del ejercicio. |
| `group` | **Nuevo**, `int?`. Las líneas del mismo día con el mismo `group` forman una superserie o circuito y se hacen seguidas. `null` = ejercicio suelto. |
| `rest_seconds` | Se mantiene. En un grupo, es el descanso después de completar la vuelta. |
| `retired_on` | **Nuevo**, `DateOnly?`. Una línea que se quita de la rutina se marca como retirada y no se borra, para que sus registros sigan apuntando a algo. |

**`RoutineExerciseLog`** (lo que hizo el miembro):

| Campo | Cambio |
|---|---|
| `exercise_id` | **Nuevo**, copiado de la línea. El historial de un ejercicio se puede consultar aunque la línea se haya retirado o la rutina haya cambiado. |
| `sets` (JSON) | Cada serie suma `set_type`, `duration_seconds`, `distance_m` y `rir`. Así queda simétrica con `set_targets` y se compara serie contra serie. |
| `sets_done`, `reps_done`, `weight_kg` | Se quitan; los reemplaza `sets`. Los datos existentes se migran a una entrada por serie cuando se pueda; si no, quedan en `notes`. |
| FK a `routine_exercises` | Pasa de `Cascade` a **`Restrict`**. Ninguna edición borra un registro. |

### Editar sin perder historial (corrige B1)

`PUT /api/routines/{id}` deja de borrar y recrear. El body trae los ids de los días y las líneas que
ya existían:

- Una línea con id existente **se actualiza** y conserva su id, así que sus registros siguen ligados.
- Una línea sin id **se crea**.
- Una línea existente que no viene en el body **se retira** (`retired_on`), no se borra.
- Con los días pasa lo mismo.
- **Cambiar el modo de calendario** (semanal ↔ rotativa) no es una edición: crea una rutina nueva y
  desactiva la anterior. Si no, se rompe la racha calculada hacia atrás.

### Revisión

- Ajuste por gym `review_weeks`, 6 por defecto. Vive en `routines.routine_settings`, con el mismo
  patrón perezoso que `attendance.checkin_settings`: sin fila valen los valores por defecto.
- `GET /api/routines/due-for-review` (staff): rutinas activas con `review_on` ≤ hoy + 7 días,
  ordenadas por fecha.
- `POST /api/routines/{id}/review` (staff): marca la rutina como revisada y corre `review_on`.
- El dashboard del panel suma la tarjeta "Rutinas por revisar".
- El miembro ve en `/app/rutina` "Tu rutina se revisa el 18 nov".

### Racha

- **Modo semanal:** qué toca cada día sale directamente de `day_of_week`. Los días sin entrenamiento
  son descanso y cuentan como día bueno, igual que en la v1.
- **Modo rotativo:** sin cambios.
- `IRoutineReader.GetTrainingCalendarAsync` resuelve los dos modos. `StreakCalculator` no cambia.
- **Cada día se juzga con la rutina vigente ese día** (decidido 2026-10-11): el calendario hacia atrás
  usa los días y líneas que estaban vigentes en cada fecha (`retired_on`), y la rutina anterior
  (`deactivated_on`) si hubo cambio de rutina o de modo. Cambiar o editar la rutina nunca altera la
  racha ya ganada. Tarea **R2b**.

### Quitar un ejercicio de las rutinas (decidido 2026-10-11, tarea R12)

**Caso 1 — el ejercicio se desactiva en el gym** (máquina dañada, ejercicio no recomendado…). Lo hacen
el Owner y el staff con permiso `trainer`.

- Al desactivarlo se listan **todas las rutinas activas que lo usan** y es obligatorio elegir un
  reemplazo (por defecto una alternativa recomendada, R5; se puede cambiar) y un **motivo** de una
  lista: máquina dañada, ejercicio no recomendado, otro (con texto).
- El reemplazo se aplica a todas esas rutinas de una vez, en el lugar del original (mismo día, series y
  repeticiones). La línea original se retira (`retired_on`) y su historial se conserva.
- Cada miembro afectado recibe un aviso (categoría "Mi rutina"): "En tu rutina, *X* se reemplazó por
  *Y*: {motivo}".
- **Nada se restablece solo**, sea cual sea el motivo. Cuando alguien **reactiva** el ejercicio a mano,
  el sistema pregunta si lo restablece en las rutinas que lo tenían. Solo vuelve donde **sigue el
  reemplazo** de esa desactivación (si el entrenador ya cambió esa línea, no se toca), y los miembros
  reciben el aviso "*X* vuelve a tu rutina".
- Se guarda cada reemplazo (rutina, línea original, línea nueva, motivo, quién, cuándo) para poder
  restablecer y para la auditoría.

**Caso 2 — se quita solo de una rutina.** Lo decide el entrenador en la edición normal: reemplazarlo o
solo quitarlo. El miembro también recibe aviso ("*X* se quitó de tu rutina" o "*X* se reemplazó por
*Y*").

Encaja con R5 (alternativas) y con Equipment E2 (estado de máquinas): una máquina dañada puede
proponer desactivar los ejercicios que dependen de ella.

### Entrenador asignado

**Cada miembro puede tener, o no, un entrenador asignado** (decidido 2026-10-07).

- El dato vive en **Members** (`Member.TrainerUserId`, nulo = sin entrenador), porque es parte de la
  ficha del miembro. Ver `GSM-modulo-members.md` §3.1.
- Puede ser entrenador **una cuenta de staff marcada como entrenador**, o el Owner. La marca es un
  permiso de Identity, `Trainer`, que se suma a `ViewMemberHealth` en la lista de permisos por cuenta
  (`GSM-modulo-members.md` §7).
- **Cualquier staff puede seguir armando la rutina de cualquier miembro.** El entrenador asignado es
  una referencia, no un candado. `assigned_by` sigue diciendo quién creó la rutina.
- "Rutinas por revisar" se filtra por defecto por **mis miembros** cuando quien entra es entrenador,
  con la opción de ver todas.
- Si una cuenta de entrenador se desactiva, sus miembros quedan sin entrenador asignado y el Owner
  ve la lista para reasignarlos.

### Progreso del miembro

**Va en Routines** (decidido 2026-10-07), no en Reports. Reports queda para descargas (Excel y
similares).

- Por ejercicio: evolución de la mejor serie (peso × repeticiones), volumen por sesión (Σ peso ×
  repeticiones), tiempo o distancia según el `tracking_type`. Se lee de `RoutineExerciseLog` por
  `exercise_id`, así que sobrevive a cambios de rutina.
- Récords personales (el mejor peso y el mejor volumen de cada ejercicio).
- Lo ven el miembro en la app y el staff en la ficha, pestaña Rutina.
- Solo lectura: no necesita tablas nuevas.

### Anonimización

Decidido 2026-10-07: **las rutinas y los registros de entrenamiento se conservan; solo se vacían los
datos sensibles.**

- Se vacían `Routine.Notes`, `RoutineExercise.Notes` y `RoutineExerciseLog.Notes`. Son texto libre y
  pueden traer datos de salud ("rodilla operada, no sentadilla profunda").
- Se conserva todo lo demás: ejercicios, series, pesos y fechas, ligados a un `member_id` que ya no
  identifica a nadie.
- Routines lo hace en un manejador del evento `MemberAnonymized` que publica Members
  (`GSM-modulo-members.md` §11). No hay escritura cruzada.

### Sub-etapas (construidas)

| Sub-etapa | Qué |
|---|---|
| **R1** | Edición en el lugar, `exercise_id` en los registros, FK `Restrict` (**corrige B1**). Primero va el test que lo reproduce. |
| **R2** | Modo semanal + racha con calendario semanal. |
| **R2b** | Racha con el calendario vigente en cada fecha (días retirados y rutinas anteriores). Decidido 2026-10-11. |
| **R3** | Catálogo v2: tipo, `tracking_type`, unilateral, nivel, músculos nuevos (con la corrección de los ejercicios del sistema), grupo muscular derivado y filtro en dos pasos, ocultar y duplicar. |
| **R3b** | Medida `weight_distance` (peso + distancia) y Paseo del granjero pasa a usarla. Decidido 2026-10-11. |
| **R4** | Series planeadas una por una, superseries, registros por serie y comparación planeado vs hecho en la app. |
| **R5** | (Requiere E1 y E2 de Equipment.) Alternativas: recomendadas (tabla) + sugeridas automáticamente, "Cambiar por…" en el editor y "Hacer otro ejercicio hoy" en la app (depende de R4 para el registro por serie). |
| **R6** | Subida de imagen y video al ejercicio (depende de configurar R2). |
| **R7** | Progreso del miembro y récords personales. |
| **R8** | Fecha de revisión: ajuste por gym, lista "por revisar" (filtrada por entrenador), acción "marcar revisada", tarjeta en el dashboard. Depende del entrenador asignado (Members + permiso `Trainer`). |
| **R9** | Avisos de salud en el editor (depende de M3 de Members). |
| **R10** | Limpieza de notas al anonimizar (depende de M6 de Members). |
| **R12** | Quitar un ejercicio de las rutinas: desactivar con reemplazo masivo y motivo, aviso al miembro, restablecer a mano (depende de R5 y N1). Decidido 2026-10-11. |
| **R11** | Plantillas. Al final del replanteamiento. |

## Decisiones de producto

Cerradas antes de especificar, porque definen el modelo:

1. **Asignación directa, sin plantillas.** El entrenador arma la rutina eligiendo ejercicios del catálogo.
2. **Catálogo híbrido.** Un catálogo del sistema compartido, más los ejercicios propios de cada gym.
3. **Seguimiento opcional.** El miembro marca el ejercicio y, si quiere, anota lo que levantó.
4. **La rutina es un ciclo rotativo** de entrenamiento y descanso, no un horario semanal.
5. **La racha cuenta días de calendario cumplidos.** Un descanso respetado es un día bueno igual que un entrenamiento hecho: "racha de 30" son 30 días reales.
6. **Congelador explícito** para imprevistos: uno cada 15 días, máximo dos, visible.
7. **El auto-servicio lo decide cada gym** por feature flag, y lo que el miembro se asigne a sí mismo **no cuenta para logros**.

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
| `self_assigned` | `bool` | La creó el propio miembro. **Las rutinas con `true` no puntúan para logros.** Campo explícito y no derivado de comparar `assigned_by`: la regla de gamificación no debe depender de una inferencia. |
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
| `member_id` | `Guid` | Denormalizado: casi toda consulta de progreso parte del miembro. |
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

El recorrido se detiene en `starts_on` de la rutina vigente; si el miembro cambió de rutina, cada fecha se evalúa con la que estaba activa ese día (de ahí `deactivated_on`).

**Sin rutina activa** se mantiene la regla actual del `StreakCalculator` (tolerancia de días de descanso). Nadie pierde la racha que ya tiene, y los gyms con `show_routines` apagado siguen con gamificación.

> **Contrapartida conocida:** una rutina laxa (un entrenamiento y tres descansos) acumula racha tan rápido como una de cinco días. Lo que impide abusar es que las rutinas las asigna el entrenador y que las auto-asignadas no puntúan.

### Congelador

Vive en el schema `achievements`: es mecánica de juego y aplica **también a miembros sin rutina**, así que no puede vivir en Routines.

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

> **Efecto de escritura en una lectura.** Consultar la racha puede crear o consumir filas de `StreakFreeze`. Es deliberado: sin proceso diario, no hay otro momento donde detectar un fallo de un miembro que precisamente *no vino*. Los índices únicos hacen la operación idempotente, así que dos consultas concurrentes no duplican nada.

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

**Members (3)** — validación del miembro por `IMemberReader`, sin FK entre esquemas.

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
- `RoutineSessionLogged(MemberId, GymId, PerformedOn, SelfAssigned)` — solo la **primera** vez que el miembro registra algo en un día dado, para contar sesiones y no ejercicios sueltos.

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

**Miembro**

```
GET    /api/me/routines                        rutina activa + qué toca hoy (o descanso)
POST   /api/me/routines                        solo si members_can_create_routines; self_assigned = true
POST   /api/routine-exercises/{id}/log         marcar y/o registrar carga (idempotente por día)
DELETE /api/routine-exercises/{id}/log?date=   desmarcar
GET    /api/me/routines/history?from=&to=
GET    /api/me/streak                          racha + congeladores disponibles
```

Un rol `member` solo alcanza sus propios datos; se reutiliza el patrón `MemberSelfAccess` existente.

**Feature flags:** con `show_routines = false`, los endpoints del miembro responden `403` y los de staff siguen abiertos (para preparar rutinas antes de encender el módulo). `POST /api/me/routines` responde `403` si `members_can_create_routines = false`.

---

## Criterios de aceptación

1. Un gym ve los ejercicios del sistema y los suyos; **nunca** los de otro gym. Test automatizado, en la línea del de tenancy.
2. Un staff que intenta editar un ejercicio del sistema recibe `403`.
3. Crear una rutina con sus días y líneas es atómico: si una línea referencia un ejercicio inexistente o de otro gym, no queda rutina a medias.
4. Registrar dos veces el mismo ejercicio el mismo día **actualiza**, no duplica, y `RoutineSessionLogged` se publica una sola vez ese día.
5. El día del registro es el día de negocio del gym: un registro a las 22:00 en Bogotá cuenta en el día local, no en el de UTC.
6. **Racha, ciclo 4 (`D1·D2·D3·descanso`):** el miembro que entrena los tres días y no va el de descanso suma **4 días de racha por ciclo**. Test con dos ciclos completos → racha 8.
7. **Un fallo con congelador disponible no corta la racha**, y el congelador queda marcado con la fecha fallada. Un segundo fallo sin saldo, sí corta.
8. **Idempotencia del congelador:** consultar la racha dos veces no gasta dos congeladores por el mismo día fallado.
9. **La racha no se pierde por el día en curso:** hoy es día de entrenamiento y sin asistencia todavía → misma racha que ayer.
10. Un miembro **sin** rutina conserva exactamente la racha que da la regla actual.
11. Una sesión de una rutina con `self_assigned = true` **no** incrementa `routine_sessions` ni desbloquea logros.
12. Con `show_routines = false`, el miembro recibe `403` y el staff sigue operando.

---

## Fuera de alcance

- **Progreso del miembro** (evolución de carga por ejercicio). Los datos quedan capturados en `RoutineExerciseLog`, así que es un módulo de lectura posterior, sin migración.
- **Plantillas de rutina** reutilizables. Es lo primero que pedirá un gym con muchos miembros; se añade encima de este modelo copiando días y líneas.
- **Vídeos e imágenes del catálogo**: los campos existen, pero dependen de Cloudflare R2, que sigue **sin configurar** (etapa 8, punto pendiente).
- **Horario semanal fijo** como alternativa al ciclo rotativo.
