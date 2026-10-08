-- R1: routine_exercise_logs.exercise_id se copia de la línea (routine_exercises.exercise_id) para los
-- registros que existían antes de la columna. Idempotente: solo toca filas sin valor.
UPDATE routines.routine_exercise_logs AS l
SET exercise_id = e.exercise_id
FROM routines.routine_exercises AS e
WHERE e.id = l.routine_exercise_id
  AND l.exercise_id IS NULL;
