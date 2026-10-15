-- R2b: routine_days.added_on (día de negocio desde el que el día es parte de la rutina).
-- Se ejecuta DESPUÉS de crear la columna added_on (date, not null). Los días ya existentes se
-- consideran vigentes desde el inicio de su rutina.
UPDATE routines.routine_days AS d
SET added_on = r.starts_on
FROM routines.routines AS r
WHERE r.id = d.routine_id;
