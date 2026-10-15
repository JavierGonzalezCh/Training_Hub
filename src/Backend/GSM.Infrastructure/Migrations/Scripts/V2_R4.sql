-- R4: series planeadas una por una y registros por serie.
-- Se ejecuta DESPUÉS de crear routine_exercises.set_targets / group_number y ANTES de quitar las columnas
-- viejas (target_sets, target_reps, target_weight_kg en routine_exercises; sets_done, reps_done, weight_kg
-- en routine_exercise_logs). Los nombres de las claves JSON son los de las propiedades de C# (PascalCase)
-- y SetType va como texto ("Normal", "Warmup", "Drop", "Failure").

-- 1. Líneas: las repeticiones que no son un número ni un rango ("8/10", "AMRAP"...) no caben en una serie
--    estructurada; el texto original pasa a las notas de la línea para no perderlo.
UPDATE routines.routine_exercises AS e
SET notes = left(concat_ws(' · ', e.notes, 'Repeticiones: ' || btrim(e.target_reps)), 500)
WHERE e.set_targets IS NULL
  AND btrim(e.target_reps) !~ '^\d{1,4}$'
  AND btrim(e.target_reps) !~ '^\d{1,4}\s*-\s*\d{1,4}$'
  AND e.target_reps NOT ILIKE '%fallo%';

-- 2. Líneas: una entrada por serie. "8-12" -> mínimo 8, máximo 12; "10" -> 10-10; "al fallo" -> serie al fallo.
UPDATE routines.routine_exercises AS e
SET set_targets = (
    SELECT jsonb_agg(
        jsonb_strip_nulls(jsonb_build_object(
            'Number', n,
            'SetType', CASE WHEN e.target_reps ILIKE '%fallo%' THEN 'Failure' ELSE 'Normal' END,
            'RepsMin', CASE
                WHEN e.target_reps ILIKE '%fallo%' THEN NULL
                WHEN btrim(e.target_reps) ~ '^\d{1,4}$' THEN btrim(e.target_reps)::int
                WHEN btrim(e.target_reps) ~ '^\d{1,4}\s*-\s*\d{1,4}$' THEN
                    least(split_part(e.target_reps, '-', 1)::int, split_part(e.target_reps, '-', 2)::int)
            END,
            'RepsMax', CASE
                WHEN e.target_reps ILIKE '%fallo%' THEN NULL
                WHEN btrim(e.target_reps) ~ '^\d{1,4}$' THEN btrim(e.target_reps)::int
                WHEN btrim(e.target_reps) ~ '^\d{1,4}\s*-\s*\d{1,4}$' THEN
                    greatest(split_part(e.target_reps, '-', 1)::int, split_part(e.target_reps, '-', 2)::int)
            END,
            'WeightKg', e.target_weight_kg))
        ORDER BY n)
    FROM generate_series(1, least(greatest(e.target_sets, 1), 30)) AS n)
WHERE e.set_targets IS NULL;

-- 3. Registros sin detalle por serie cuyas repeticiones no se pueden repartir: el dato viejo pasa a las notas.
UPDATE routines.routine_exercise_logs AS l
SET notes = left(concat_ws(' · ', l.notes,
        CASE WHEN l.reps_done IS NOT NULL THEN 'Repeticiones: ' || l.reps_done END,
        CASE WHEN l.weight_kg IS NOT NULL AND coalesce(l.sets_done, 0) <= 0 THEN 'Peso: ' || l.weight_kg || ' kg' END), 500)
WHERE (l.sets IS NULL OR jsonb_array_length(l.sets) = 0)
  AND (
        (coalesce(l.sets_done, 0) <= 0 AND (l.reps_done IS NOT NULL OR l.weight_kg IS NOT NULL))
        OR (coalesce(l.sets_done, 0) > 0 AND l.reps_done IS NOT NULL
            AND NOT (l.reps_done ~ '^\d{1,4}$'
                     OR (l.reps_done ~ '^\d{1,4}(/\d{1,4})+$'
                         AND cardinality(string_to_array(l.reps_done, '/')) = l.sets_done)))
      );

-- 4. Registros sin detalle por serie pero con número de series: una entrada por serie con las repeticiones
--    ("10" para todas, o "10/8/6" una por serie) y el peso global.
UPDATE routines.routine_exercise_logs AS l
SET sets = (
    SELECT jsonb_agg(
        jsonb_strip_nulls(jsonb_build_object(
            'Number', n,
            'SetType', 'Normal',
            'Reps', CASE
                WHEN l.reps_done ~ '^\d{1,4}$' THEN l.reps_done::int
                WHEN l.reps_done ~ '^\d{1,4}(/\d{1,4})+$'
                     AND cardinality(string_to_array(l.reps_done, '/')) = l.sets_done
                    THEN (string_to_array(l.reps_done, '/'))[n]::int
            END,
            'WeightKg', l.weight_kg))
        ORDER BY n)
    FROM generate_series(1, least(l.sets_done, 30)) AS n)
WHERE coalesce(l.sets_done, 0) > 0
  AND (l.sets IS NULL OR jsonb_array_length(l.sets) = 0);

-- 5. Registros que ya traían detalle (etapa 12.8): se les marca el tipo de serie. Idempotente.
UPDATE routines.routine_exercise_logs AS l
SET sets = (
    SELECT jsonb_agg(elem || jsonb_build_object('SetType', 'Normal') ORDER BY ord)
    FROM jsonb_array_elements(l.sets) WITH ORDINALITY AS t(elem, ord))
WHERE l.sets IS NOT NULL
  AND jsonb_array_length(l.sets) > 0
  AND NOT jsonb_exists(l.sets -> 0, 'SetType');
