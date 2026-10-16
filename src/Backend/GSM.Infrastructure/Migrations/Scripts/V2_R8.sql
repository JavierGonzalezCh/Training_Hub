-- V2_R8: fecha de revisión de rutinas (Routines v2, "Revisión").
--
-- Orden esperado de la migración V2_R8_RoutineReview:
--   1. AddColumn routines.routines: review_on date NOT NULL (EF le pone '0001-01-01' a las filas existentes),
--      last_reviewed_at timestamptz NULL, last_reviewed_by uuid NULL; índice
--      ix_routines_gym_id_is_active_review_on (gym_id, is_active, review_on).
--   2. CreateTable routines.routine_settings: id, gym_id, review_weeks int NOT NULL, created_at, updated_at...;
--      índice único sobre gym_id.
--   3. migrationBuilder.SqlScript("R8")  <- este script.
--
-- Rellena review_on de las rutinas que ya existían con el valor por defecto del gym (6 semanas = 42 días
-- desde el inicio). Solo toca las filas con el vacío de EF, así que re-ejecutarlo no pisa nada.
UPDATE routines.routines
SET review_on = starts_on + 42
WHERE review_on = DATE '0001-01-01';
