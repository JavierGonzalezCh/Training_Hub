-- R3: catálogo de ejercicios v2.
--
-- 1) routines.muscle pierde 'cardio' y gana 'traps', 'obliques', 'adductors', 'abductors'. Postgres no
--    permite quitar un valor de un enum: se crea el tipo nuevo, se convierten las columnas y se borra el viejo.
--    Los ejercicios cuyo músculo principal era 'cardio' pasan a 'full_body' con tipo 'cardio'; 'cardio' como
--    músculo secundario simplemente se quita.
-- 2) Se clasifican los ejercicios del sistema (gym_id IS NULL): tipo, qué se mide, unilateral y nivel.
--    Los datos del sistema solo los cambia una migración, nunca un test.

-- Los que tenían 'cardio' como principal: se marcan antes de convertir (la columna aún conserva el valor).
UPDATE routines.exercises
SET exercise_type = 'cardio',
    tracking_type = 'time_distance'
WHERE primary_muscle = 'cardio';

CREATE TYPE routines.muscle_new AS ENUM (
    'chest', 'back', 'shoulders', 'biceps', 'triceps', 'forearms', 'quadriceps', 'hamstrings',
    'glutes', 'calves', 'abs', 'lower_back', 'neck', 'full_body', 'traps', 'obliques', 'adductors', 'abductors');

ALTER TABLE routines.exercises
    ALTER COLUMN primary_muscle TYPE routines.muscle_new
    USING (CASE WHEN primary_muscle::text = 'cardio' THEN 'full_body' ELSE primary_muscle::text END)::routines.muscle_new;

ALTER TABLE routines.exercises
    ALTER COLUMN secondary_muscles TYPE routines.muscle_new[]
    USING array_remove(secondary_muscles::text[], 'cardio')::routines.muscle_new[];

DROP TYPE routines.muscle;
ALTER TYPE routines.muscle_new RENAME TO muscle;

-- "Encogimientos de trapecio" estaba como 'neck' por falta de un músculo para el trapecio.
UPDATE routines.exercises
SET primary_muscle = 'traps'
WHERE gym_id IS NULL AND name = 'Encogimientos de trapecio con mancuernas';

-- Clasificación del catálogo del sistema. Lo que no aparece aquí queda en los valores por defecto
-- (strength, reps_weight, bilateral, intermediate).
UPDATE routines.exercises AS e
SET exercise_type = v.exercise_type::routines.exercise_type,
    tracking_type = v.tracking_type::routines.tracking_type,
    is_unilateral = v.is_unilateral,
    difficulty    = v.difficulty::routines.difficulty
FROM (VALUES
    ('Press de banca plana con barra',            'strength',    'reps_weight',   false, 'intermediate'),
    ('Press de banca inclinado con mancuernas',   'strength',    'reps_weight',   false, 'intermediate'),
    ('Aperturas con mancuernas',                  'strength',    'reps_weight',   false, 'intermediate'),
    ('Fondos en paralelas',                       'strength',    'reps',          false, 'advanced'),
    ('Cruce de poleas',                           'strength',    'reps_weight',   false, 'intermediate'),
    ('Flexiones de pecho',                        'strength',    'reps',          false, 'beginner'),
    ('Aperturas con banda elástica',              'strength',    'reps',          false, 'beginner'),
    ('Dominadas',                                 'strength',    'reps',          false, 'advanced'),
    ('Remo con barra',                            'strength',    'reps_weight',   false, 'intermediate'),
    ('Remo con mancuerna a una mano',             'strength',    'reps_weight',   true,  'beginner'),
    ('Jalón al pecho en polea',                   'strength',    'reps_weight',   false, 'beginner'),
    ('Peso muerto convencional',                  'strength',    'reps_weight',   false, 'advanced'),
    ('Remo en máquina',                           'strength',    'reps_weight',   false, 'beginner'),
    ('Remo en TRX',                               'strength',    'reps',          false, 'intermediate'),
    ('Press militar con barra',                   'strength',    'reps_weight',   false, 'intermediate'),
    ('Press de hombros con mancuernas',           'strength',    'reps_weight',   false, 'intermediate'),
    ('Elevaciones laterales',                     'strength',    'reps_weight',   false, 'beginner'),
    ('Elevaciones posteriores (pájaros)',         'strength',    'reps_weight',   false, 'beginner'),
    ('Face pull en polea',                        'strength',    'reps_weight',   false, 'beginner'),
    ('Curl de bíceps con barra',                  'strength',    'reps_weight',   false, 'beginner'),
    ('Curl alterno con mancuernas',               'strength',    'reps_weight',   false, 'beginner'),
    ('Curl martillo',                             'strength',    'reps_weight',   false, 'beginner'),
    ('Curl de bíceps en polea',                   'strength',    'reps_weight',   false, 'beginner'),
    ('Extensión de tríceps en polea',             'strength',    'reps_weight',   false, 'beginner'),
    ('Press francés con barra',                   'strength',    'reps_weight',   false, 'intermediate'),
    ('Fondos entre bancos',                       'strength',    'reps',          false, 'beginner'),
    ('Patada de tríceps con mancuerna',           'strength',    'reps_weight',   true,  'beginner'),
    ('Curl de muñeca con barra',                  'strength',    'reps_weight',   false, 'beginner'),
    ('Paseo del granjero',                        'strength',    'distance',      false, 'intermediate'),
    ('Sentadilla con barra',                      'strength',    'reps_weight',   false, 'intermediate'),
    ('Prensa de piernas',                         'strength',    'reps_weight',   false, 'beginner'),
    ('Zancadas con mancuernas',                   'strength',    'reps_weight',   true,  'intermediate'),
    ('Extensión de cuádriceps en máquina',        'strength',    'reps_weight',   false, 'beginner'),
    ('Sentadilla búlgara',                        'strength',    'reps_weight',   true,  'advanced'),
    ('Curl femoral tumbado en máquina',           'strength',    'reps_weight',   false, 'beginner'),
    ('Peso muerto rumano',                        'strength',    'reps_weight',   false, 'intermediate'),
    ('Buenos días con barra',                     'strength',    'reps_weight',   false, 'advanced'),
    ('Hip thrust con barra',                      'strength',    'reps_weight',   false, 'intermediate'),
    ('Patada de glúteo en polea',                 'strength',    'reps_weight',   true,  'beginner'),
    ('Puente de glúteos',                         'strength',    'reps',          false, 'beginner'),
    ('Elevación de talones de pie',               'strength',    'reps_weight',   false, 'beginner'),
    ('Elevación de talones sentado',              'strength',    'reps_weight',   false, 'beginner'),
    ('Salto a la cuerda',                         'cardio',      'time',          false, 'beginner'),
    ('Crunch abdominal',                          'strength',    'reps',          false, 'beginner'),
    ('Plancha',                                   'strength',    'time',          false, 'beginner'),
    ('Elevación de piernas colgado',              'strength',    'reps',          false, 'advanced'),
    ('Lanzamiento de balón medicinal',            'plyometrics', 'reps',          false, 'intermediate'),
    ('Rueda abdominal',                           'strength',    'reps',          false, 'advanced'),
    ('Hiperextensiones lumbares',                 'strength',    'reps',          false, 'beginner'),
    ('Encogimientos de trapecio con mancuernas',  'strength',    'reps_weight',   false, 'beginner'),
    ('Burpees',                                   'plyometrics', 'reps',          false, 'intermediate'),
    ('Swing con kettlebell',                      'strength',    'reps_weight',   false, 'intermediate'),
    ('Peso muerto con kettlebell',                'strength',    'reps_weight',   false, 'beginner'),
    ('Subida al cajón',                           'strength',    'reps',          true,  'beginner'),
    ('Cinta de correr',                           'cardio',      'time_distance', false, 'beginner'),
    ('Bicicleta estática',                        'cardio',      'time_distance', false, 'beginner'),
    ('Remo en máquina de cardio',                 'cardio',      'time_distance', false, 'beginner'),
    ('Escaladores (mountain climbers)',           'cardio',      'time',          false, 'beginner')
) AS v(name, exercise_type, tracking_type, is_unilateral, difficulty)
WHERE e.gym_id IS NULL AND e.name = v.name;
