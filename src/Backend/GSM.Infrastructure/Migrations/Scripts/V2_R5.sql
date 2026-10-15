-- R5: alternativas recomendadas del sistema (routines.exercise_alternatives, gym_id nulo).
-- Corre DESPUES de crear la tabla. Es una migracion de datos porque el catalogo de ejercicios del sistema ya vive en
-- produccion y estas recomendaciones tienen que venir con el: la lista de alternativas de "Sentadilla con barra" ofrece
-- primero "Prensa de piernas" y "Sentadilla bulgara". Los tests nunca siembran ni cambian datos del sistema.
-- Se enlaza por nombre y solo entre ejercicios del sistema (gym_id nulo); si un nombre no existe, el par se omite.
-- Cada par se guarda una sola vez, con el menor uuid en exercise_id (el orden de Postgres es el mismo que usa el dominio).
-- Idempotente: el indice unico (gym_id, exercise_id, alternative_id) con NULLS NOT DISTINCT evita duplicados.
INSERT INTO routines.exercise_alternatives (id, gym_id, exercise_id, alternative_id, created_at, updated_at)
SELECT gen_random_uuid(), NULL, least(a.id, b.id), greatest(a.id, b.id), now(), now()
FROM (VALUES
    -- Pecho
    ('Press de banca plana con barra', 'Press de banca inclinado con mancuernas'),
    ('Press de banca plana con barra', 'Flexiones de pecho'),
    ('Aperturas con mancuernas', 'Cruce de poleas'),
    ('Aperturas con mancuernas', 'Aperturas con banda elástica'),
    ('Fondos en paralelas', 'Flexiones de pecho'),
    -- Espalda
    ('Remo con barra', 'Remo con mancuerna a una mano'),
    ('Remo con barra', 'Remo en máquina'),
    ('Remo en máquina', 'Remo en TRX'),
    ('Dominadas', 'Jalón al pecho en polea'),
    -- Hombros
    ('Press militar con barra', 'Press de hombros con mancuernas'),
    ('Elevaciones posteriores (pájaros)', 'Face pull en polea'),
    -- Bíceps
    ('Curl de bíceps con barra', 'Curl alterno con mancuernas'),
    ('Curl alterno con mancuernas', 'Curl martillo'),
    ('Curl de bíceps con barra', 'Curl de bíceps en polea'),
    -- Tríceps
    ('Extensión de tríceps en polea', 'Press francés con barra'),
    ('Extensión de tríceps en polea', 'Patada de tríceps con mancuerna'),
    ('Fondos entre bancos', 'Patada de tríceps con mancuerna'),
    -- Pierna y glúteo
    ('Sentadilla con barra', 'Prensa de piernas'),
    ('Sentadilla con barra', 'Sentadilla búlgara'),
    ('Prensa de piernas', 'Extensión de cuádriceps en máquina'),
    ('Zancadas con mancuernas', 'Sentadilla búlgara'),
    ('Peso muerto rumano', 'Curl femoral tumbado en máquina'),
    ('Peso muerto rumano', 'Buenos días con barra'),
    ('Hip thrust con barra', 'Puente de glúteos'),
    ('Hip thrust con barra', 'Patada de glúteo en polea'),
    ('Elevación de talones de pie', 'Elevación de talones sentado'),
    -- Core
    ('Crunch abdominal', 'Elevación de piernas colgado'),
    ('Plancha', 'Rueda abdominal'),
    -- Cardio
    ('Cinta de correr', 'Bicicleta estática'),
    ('Bicicleta estática', 'Remo en máquina de cardio'),
    ('Cinta de correr', 'Salto a la cuerda')
) AS v(first_name, second_name)
JOIN routines.exercises a ON a.name = v.first_name AND a.gym_id IS NULL
JOIN routines.exercises b ON b.name = v.second_name AND b.gym_id IS NULL
ON CONFLICT DO NOTHING;
