# Sesión perdida y reorganización de la semana (SP)

**Estado:** SP1 construido (2026-11-05); SP2–SP4 en diseño. Pedido el 2026-11-05. Sirve a cualquier tipo de negocio (no hay `if (tipo == …)`); pesa más en entrenador personal y estudio, donde el día cuenta al entrenar.

## 1. Qué resuelve

Hoy, si el miembro falta a un día de su rutina, ese día queda como no hecho, la racha gasta un congelador o se corta, y al día siguiente la app le muestra lo que toca ese día, sin acordarse del que perdió. En la vida real el miembro quiere recuperar la rutina que perdió (sobre todo si era la de pecho y no quiere saltársela), y a veces quiere correr toda la semana.

![Opciones al recuperar la sesión perdida](img/sesion-perdida-opciones.png)

## 2. Reglas (decididas por el dueño)

| # | Regla | Decisión |
|---|---|---|
| 1 | Ver otros días | El miembro puede ver y **ejecutar cualquier rutina del plan en cualquier momento**. La sesión se registra con su **fecha real** (la de hoy), no con la del día al que pertenece la rutina. |
| 2 | Detectar la perdida | Si **ayer** tocaba entrenar y no entrenó, la app marca la rutina de ayer como **perdida**. Solo se ofrece la de ayer. Las anteriores quedan en el historial como perdidas y no se ofrecen. |
| 3 | Hoy es descanso | La app **sugiere** hacer la perdida. |
| 4 | Hoy toca entrenar | La app **pregunta**: ¿haces la perdida o sigues con la de hoy? |
| 4a | Sigue con la de hoy | La perdida queda perdida y no pasa nada más. |
| 4b | Hace la perdida | Se le pregunta si **reorganiza** la semana. |
| 4b-sí | Reorganiza | Se aplica la regla 5. |
| 4b-no | Solo cambia | Hoy hace la perdida; la rutina de hoy, que no se hace, queda **perdida**. El resto de la semana no cambia. |
| 5 | Regla de reorganizar | **Los descansos quedan fijos.** Las rutinas se corren en orden, saltando los descansos, **solo dentro de la semana** (lunes a domingo, `IsoWeekDay`). La que ya no cabe queda **perdida**. |
| 6 | Explicación al usuario | Antes de decidir, se le muestra qué pasa con su semana, de preferencia con un diagrama antes/después como el de arriba. |
| 7 | Si no está seguro | La pantalla de decisión incluye la nota: «Si no estás seguro, pregúntale a tu entrenador». Si tiene entrenador asignado, se muestra su nombre. |

**Prioridad:** primero lo básico, reglas 1 a 4 (con 4b-no). La reorganización (regla 5 y 4b-sí) puede venir después.

### Ejemplo de la regla 5

Plan L Pecho · M Pierna · X Espalda · J Descanso · V Hombro. Faltó el lunes; hoy es martes.

| | Mar | Mié | Jue | Vie | Perdida |
|---|---|---|---|---|---|
| No reorganizar | Pecho | Espalda | Descanso | Hombro | Pierna |
| Reorganizar | Pecho | Pierna | Descanso | Espalda | Hombro |

## 3. Choque con reglas que ya existen

La regla de qué toca cada fecha es una sola, `RoutineCalendar`, y la usan la racha y el cumplimiento. Hoy dice que **un día cuenta como hecho solo con registros de sus propias líneas**. Por eso, si el miembro hace el martes la rutina del lunes, hoy no cumple ni el lunes ni el martes. Este diseño cambia eso de forma explícita:

- Se agrega a Routines un registro de **ajustes del calendario por miembro y fecha**: «el 2026-11-04 se hizo el día *Pecho* en lugar de *Pierna*», «*Hombro* quedó perdida». `RoutineCalendar.Resolve` lo consulta, así que la racha y el cumplimiento siguen diciendo lo mismo de cada fecha. **No se reimplementa la regla en otro lado.**
- Con un ajuste, el día de hoy cuenta como hecho con los registros del día que se cambió (Pecho), no con los del original (Pierna).
- La sesión perdida que se recupera **no** vuelve atrás: el lunes sigue sin hacer en el cumplimiento. Lo que cambia es el martes, que ahora cuenta como hecho. Queda por decidir si la racha perdona ese lunes (pregunta abierta 1).
- La regla 1 (cualquier rutina, cualquier día) sin un ajuste es un registro libre: se guarda y suma a progreso y récords, pero no cumple ningún día. Así lo hace hoy «Hacer otro ejercicio hoy» con las alternativas.

## 4. Cómo encaja

- **Todo vive en Routines.** No hay escritura cruzada nueva ni puertos nuevos. Attendance ya cuenta el día por entrenamiento con `RoutineExerciseLogged`, sin saber de qué día era la rutina.
- **Sin proceso en segundo plano.** «Perdida» se deriva al leer, como el cumplimiento: ayer tocaba entrenar (`RoutineCalendar`) y no hay registros. Solo se persiste lo que decide el miembro: el cambio y la reorganización.
- **Rutinas rotativas** (`ScheduleMode.Rotating`): la regla 5 habla de semana y de descansos fijos, así que en la primera versión **solo se reorganizan las semanales**. En una rotativa se ofrece solo cambiar (4b-no). Ver la pregunta abierta 2.

## 5. Datos (borrador)

| Dónde | Qué |
|---|---|
| `routines.routine_day_overrides` (nueva) | `id`, `gym_id`, `member_id`, `routine_id`, `date`, `planned_day_id` (lo que tocaba), `performed_day_id` (null = perdida), `reason` (`swap` / `reorganize`), `created_at`. Índice único `(member_id, date)`. |

Una reorganización escribe una fila por cada fecha de la semana que cambia, en una transacción. Deshacer es borrar las filas futuras de esa semana (pregunta abierta 3).

## 6. API y pantallas

- `GET /api/me/routines/today`: lo que hoy devuelve más `missedYesterday` (el día perdido o null) y `options` (hacer la perdida, seguir con la de hoy).
- `POST /api/me/routines/swap`: `{ date, performedDayId, reorganize }`. Valida que la perdida sea la de ayer y que la fecha sea hoy.
- `GET /api/me/routines/reorganize-preview?performedDayId=…`: la semana antes/después, para el diagrama. La misma función pura que aplica la regla 5 la calcula, así que lo que se muestra es lo que se guarda.
- **App del miembro (GO), `/app/rutina`:**
  - selector para ver y ejecutar cualquier día del plan (regla 1);
  - tarjeta «Ayer te faltó *Pecho*» con las opciones (reglas 3 y 4);
  - diagrama antes/después y la nota del entrenador (reglas 6 y 7).
- **Panel:** en la ficha del miembro (pestaña Rutina) y en el historial, los días cambiados y perdidos con su etiqueta, para que el entrenador lo vea.

## 7. Etapas

| Etapa | Qué | Tests |
|---|---|---|
| **SP1** ✓ | Regla 1: ver y ejecutar cualquier día del plan, registro con fecha real (registro libre, no cumple día). La API ya lo permitía; se agregó el selector de día en `/app/rutina` y lo registrado hoy de otro día reabre ese día. | `RoutineLogTests` + `RoutineOtherDayUiTests` |
| **SP2** | Dominio + migración de `routine_day_overrides`; `RoutineCalendar` lo consulta; racha y cumplimiento con cambios. | unitarios de la función pura + integración (zona horaria Colombia) |
| **SP3** | Reglas 2–4 con 4b-no: detectar la perdida de ayer, sugerir o preguntar, cambiar. API y pantallas. | integración HTTP + bUnit |
| **SP4** | Regla 5 (reorganizar, solo semanal): vista previa, aplicar, diagrama antes/después, avisos. | unitarios de la regla + bUnit |

SP1 a SP3 es «lo básico» que pidió el dueño. SP4 puede venir después.

## 8. Preguntas abiertas

1. **Racha:** al recuperar el lunes el martes, ¿el lunes deja de cortar la racha (como si fuera un congelador gratis) o la racha solo mira si cada día se hizo lo que tocaba después del ajuste? Propuesta: el lunes sigue como fallo y gasta congelador, para no inflar la racha.
2. **Rutinas rotativas:** ¿se reorganizan corriendo el ciclo (todo se corre un día, como la opción B anterior) o se dejan solo con cambiar?
3. **Deshacer:** ¿puede el miembro deshacer una reorganización el mismo día? ¿Y el entrenador desde el panel?
4. **Logros:** ¿la sesión recuperada cuenta para logros igual que una normal? Propuesta: sí, porque es su rutina asignada, no una auto-asignada.
5. **Aviso al entrenador:** ¿recibe aviso (campana) cuando un miembro reorganiza su semana?
6. **Semana:** ¿empieza el lunes para todos los gyms, o debe ser configurable?

## 9. Fuera de alcance

Recuperar sesiones de antes de ayer, mover sesiones a la semana siguiente y que el entrenador reorganice por el miembro.
