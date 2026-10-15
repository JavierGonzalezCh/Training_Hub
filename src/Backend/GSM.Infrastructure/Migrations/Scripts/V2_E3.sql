-- E3: ligas de sistema ejercicio <-> modelo de máquina (routines.exercise_equipment_models).
-- Corre DESPUÉS de crear la tabla. Es una migración de datos porque los ejercicios y los modelos del catálogo
-- tienen que venir ligados también en producción: cuando un gym elige "Prensa de piernas 45°", la "Prensa de
-- piernas" del catálogo de ejercicios queda ligada sola a su máquina.
-- Se enlaza por nombre y solo ejercicios del sistema (gym_id nulo); si un nombre no existe, esa fila se omite.
-- Idempotente: el índice único (exercise_id, model_id) evita duplicados. No toca el catálogo sembrado.
INSERT INTO routines.exercise_equipment_models (id, exercise_id, model_id, created_at, updated_at)
SELECT gen_random_uuid(), e.id, m.id, now(), now()
FROM (VALUES
    ('Prensa de piernas', 'Prensa de piernas 45°'),
    ('Prensa de piernas', 'Prensa de piernas horizontal'),
    ('Extensión de cuádriceps en máquina', 'Extensión de cuádriceps'),
    ('Curl femoral tumbado en máquina', 'Curl femoral acostado'),
    ('Jalón al pecho en polea', 'Polea alta (jalón)'),
    ('Jalón al pecho en polea', 'Jalón al pecho (máquina)'),
    ('Remo en máquina', 'Remo sentado (máquina)'),
    ('Cruce de poleas', 'Polea doble (crossover)'),
    ('Cruce de poleas', 'Torre de polea ajustable (functional trainer)'),
    ('Extensión de tríceps en polea', 'Polea alta (jalón)'),
    ('Extensión de tríceps en polea', 'Polea doble (crossover)'),
    ('Hiperextensiones lumbares', 'Hiperextensiones (banco romano)'),
    ('Cinta de correr', 'Cinta de correr'),
    ('Bicicleta estática', 'Bicicleta estática'),
    ('Remo en máquina de cardio', 'Remo (ergómetro)'),
    ('Press de banca plana con barra', 'Soporte de press de banca')
) AS v(exercise_name, model_name)
JOIN routines.exercises e ON e.name = v.exercise_name AND e.gym_id IS NULL
JOIN equipment.equipment_models m ON m.name = v.model_name
ON CONFLICT (exercise_id, model_id) DO NOTHING;
