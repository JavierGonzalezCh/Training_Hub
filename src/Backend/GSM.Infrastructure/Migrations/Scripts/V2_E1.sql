-- E1: catálogo de máquinas del sistema (equipment.equipment_models). Corre DESPUÉS de crear las tablas.
-- Es una migración de datos porque el catálogo tiene que existir también en producción. Sin imágenes:
-- son trabajo de contenido (necesitan licencia) y la UI muestra un ícono por tipo mientras falten.
-- Idempotente: el nombre es único y un modelo ya existente no se pisa.
INSERT INTO equipment.equipment_models
    (id, name, type, description, default_instructions, tracked_per_unit, is_active, created_at, updated_at)
SELECT gen_random_uuid(), v.name, v.type::equipment.equipment_type, v.description, v.instructions, v.tracked, true, now(), now()
FROM (VALUES
    -- Máquinas de pierna
    ('Prensa de piernas 45°', 'machine', 'Prensa inclinada con carro sobre rieles para trabajar cuádriceps, glúteos e isquiotibiales.', 'Ajusta el respaldo para que las rodillas queden a unos 90° abajo. No bloquees las rodillas al extender.', true),
    ('Prensa de piernas horizontal', 'machine', 'Prensa sentada con empuje horizontal para cuádriceps y glúteos.', 'Regula el asiento para que las rodillas queden a unos 90° y la espalda baja pegada al respaldo.', true),
    ('Hack squat', 'machine', 'Sentadilla guiada en un carro inclinado, con hombros apoyados.', 'Ubica los pies a la altura de los hombros y baja hasta que los muslos queden paralelos al suelo.', true),
    ('Extensión de cuádriceps', 'machine', 'Máquina sentada para aislar el cuádriceps.', 'Alinea la rodilla con el eje de giro y apoya el rodillo sobre los tobillos.', true),
    ('Curl femoral sentado', 'machine', 'Máquina sentada para trabajar los isquiotibiales.', 'Alinea la rodilla con el eje de giro y ajusta el rodillo justo sobre el talón.', true),
    ('Curl femoral acostado', 'machine', 'Máquina boca abajo para aislar los isquiotibiales.', 'Alinea la rodilla con el eje de giro; mantén la cadera pegada al banco.', true),
    ('Máquina de abductores', 'machine', 'Máquina sentada para abrir las piernas: glúteo medio y abductores.', 'Ajusta el respaldo y el ancho inicial de las almohadillas.', true),
    ('Máquina de aductores', 'machine', 'Máquina sentada para cerrar las piernas: aductores.', 'Ajusta el ancho inicial de las almohadillas para empezar con un estiramiento cómodo.', true),
    ('Gemelos sentado', 'machine', 'Máquina para trabajar el sóleo y las pantorrillas desde sentado.', 'Apoya la parte delantera del pie y sube el talón lo más alto posible.', true),
    ('Gemelos de pie', 'machine', 'Máquina con hombreras para trabajar las pantorrillas de pie.', 'Ajusta las hombreras a tu altura y baja el talón hasta sentir el estiramiento.', true),
    ('Máquina de glúteos', 'machine', 'Máquina para empuje de cadera o patada de glúteo.', 'Ajusta el respaldo y el apoyo del pie según el movimiento.', true),
    ('Hiperextensiones (banco romano)', 'machine', 'Banco inclinado para fortalecer espalda baja, glúteos e isquiotibiales.', 'Ajusta el apoyo de la cadera justo debajo de las crestas ilíacas.', true),
    -- Máquinas de pecho, espalda y hombro
    ('Press de pecho (máquina)', 'machine', 'Press sentado con agarres guiados para pecho, hombro y tríceps.', 'Ajusta el asiento para que las asas queden a la altura del pecho.', true),
    ('Aperturas / Pec deck', 'machine', 'Máquina de aperturas para aislar el pecho (o deltoides posterior).', 'Ajusta el asiento para que los codos queden a la altura de los hombros.', true),
    ('Press de hombro (máquina)', 'machine', 'Press vertical sentado para deltoides y tríceps.', 'Ajusta el asiento para que las asas partan a la altura de los hombros.', true),
    ('Elevaciones laterales (máquina)', 'machine', 'Máquina para aislar el deltoides lateral.', 'Alinea el codo con el eje de giro y sube sin encoger los hombros.', true),
    ('Jalón al pecho (máquina)', 'machine', 'Máquina de jalones con placas para dorsales.', 'Ajusta el rodillo de muslos para que quede firme y jala hacia la parte alta del pecho.', true),
    ('Remo sentado (máquina)', 'machine', 'Remo sentado con apoyo de pecho para espalda media.', 'Ajusta el asiento y el apoyo de pecho para que los brazos queden extendidos al inicio.', true),
    ('Máquina de dominadas asistidas', 'machine', 'Dominadas y fondos con contrapeso que resta carga corporal.', 'Más peso en la placa significa más ayuda. Arrodíllate en la plataforma.', true),
    ('Curl de bíceps (máquina)', 'machine', 'Máquina sentada para aislar el bíceps.', 'Ajusta el asiento para que los codos queden alineados con el eje de giro.', true),
    ('Extensión de tríceps (máquina)', 'machine', 'Máquina sentada para aislar el tríceps.', 'Ajusta el asiento para que los codos queden alineados con el eje de giro.', true),
    ('Abdominales (máquina)', 'machine', 'Máquina de crunch con resistencia para el abdomen.', 'Ajusta el asiento y exhala al contraer el abdomen.', true),
    -- Poleas y multipower
    ('Polea doble (crossover)', 'cable', 'Dos poleas ajustables en torres para cruces, jalones y muchos ejercicios más.', 'Ajusta la altura de cada polea según el ejercicio y elige el agarre.', true),
    ('Polea alta (jalón)', 'cable', 'Polea alta con barra para jalones al pecho y tríceps.', 'Ajusta el rodillo de muslos y elige la barra o el agarre.', true),
    ('Polea baja (remo)', 'cable', 'Polea baja con asiento para remos y curls.', 'Siéntate con las rodillas ligeramente flexionadas y la espalda recta.', true),
    ('Torre de polea ajustable (functional trainer)', 'cable', 'Dos poleas regulables en altura, muy versátiles.', 'Mueve el carro a la altura deseada y fíjalo antes de empezar.', true),
    ('Multipower', 'smith_machine', 'Barra guiada en rieles verticales con ganchos de seguridad.', 'Gira la barra para soltar los ganchos y vuelve a girarla para asegurarla.', true),
    -- Bancos y racks
    ('Banco plano', 'bench', 'Banco horizontal para press y trabajo con mancuernas.', null, true),
    ('Banco inclinado ajustable', 'bench', 'Banco con respaldo regulable en varios ángulos.', 'Fija el respaldo con el pasador en el ángulo deseado.', true),
    ('Banco declinado', 'bench', 'Banco con inclinación negativa para pecho inferior y abdomen.', 'Asegura bien los pies bajo los rodillos antes de bajar.', true),
    ('Banco Scott (predicador)', 'bench', 'Banco con apoyo inclinado para el curl de bíceps aislado.', 'Ajusta el asiento para que las axilas queden apoyadas arriba del cojín.', true),
    ('Rack de sentadillas', 'rack', 'Soportes y barras de seguridad para sentadilla con barra.', 'Ubica los ganchos bajo la altura del hombro y las barras de seguridad bajo el punto más bajo.', true),
    ('Jaula de potencia (power rack)', 'rack', 'Estructura cerrada con barras de seguridad para sentadilla, press y dominadas.', 'Ajusta ganchos y barras de seguridad antes de cargar la barra.', true),
    ('Soporte de press de banca', 'rack', 'Banco con soportes integrados para press con barra.', 'Regula la altura de los soportes para sacar la barra sin despegar la espalda.', true),
    ('Rack de mancuernas', 'rack', 'Estante para ordenar las mancuernas del gym.', null, true),
    -- Cardio
    ('Cinta de correr', 'cardio_machine', 'Banda motorizada para caminar o correr, con velocidad e inclinación regulables.', 'Clip de seguridad en la ropa y arranca a baja velocidad.', true),
    ('Bicicleta estática', 'cardio_machine', 'Bicicleta vertical con resistencia regulable.', 'Ajusta la altura del asiento para que la rodilla quede casi extendida abajo.', true),
    ('Bicicleta de spinning', 'cardio_machine', 'Bicicleta con volante de inercia y resistencia por tensión.', 'Ajusta altura de asiento y manubrio antes de empezar.', true),
    ('Elíptica', 'cardio_machine', 'Máquina de movimiento elíptico de bajo impacto.', 'Mantén la postura erguida y los pies planos sobre los pedales.', true),
    ('Remo (ergómetro)', 'cardio_machine', 'Máquina de remo con volante para cardio de cuerpo completo.', 'Ajusta las correas de los pies y empuja con las piernas antes de jalar con los brazos.', true),
    ('Escaladora (stair climber)', 'cardio_machine', 'Máquina de escalones continuos para cardio y piernas.', 'Mantén el torso erguido y apoya las manos sin cargar el peso en ellas.', true),
    ('Air bike', 'cardio_machine', 'Bicicleta de ventilador con brazos móviles; la resistencia crece con el esfuerzo.', 'Ajusta el asiento y empuja y jala con los brazos a la vez.', true),
    -- Peso libre e implementos sueltos (con cantidad)
    ('Mancuernas', 'dumbbell', 'Juego de mancuernas de distintos pesos.', null, false),
    ('Barras olímpicas', 'barbell', 'Barras rectas de 20 kg para sentadilla, press y peso muerto.', null, false),
    ('Barras Z', 'barbell', 'Barras curvas para curl de bíceps y extensiones de tríceps.', null, false),
    ('Discos de peso', 'barbell', 'Discos para cargar las barras.', null, false),
    ('Kettlebells (pesas rusas)', 'kettlebell', 'Pesas con asa para swings, arranques y trabajo funcional.', null, false),
    ('Bandas elásticas', 'resistance_band', 'Bandas de resistencia para asistencia y trabajo de movilidad.', null, false),
    ('Sistema de suspensión (TRX)', 'trx', 'Correas de suspensión para ejercicios con el peso corporal.', 'Ancla las correas a un punto firme y comprueba el agarre antes de apoyarte.', false),
    ('Balones medicinales', 'medicine_ball', 'Balones con peso para lanzamientos y trabajo de core.', null, false),
    ('Colchonetas', 'mat', 'Colchonetas para suelo, abdomen y estiramientos.', null, false),
    ('Cajones pliométricos', 'box', 'Cajones de distintas alturas para saltos y escalones.', null, false),
    ('Cuerdas de saltar', 'jump_rope', 'Cuerdas para saltar y calentar.', null, false),
    ('Barra de dominadas', 'bodyweight', 'Barra fija o de pared para dominadas y colgarse.', null, true),
    ('Paralelas (fondos)', 'bodyweight', 'Barras paralelas para fondos y elevaciones de piernas.', null, true)
) AS v(name, type, description, instructions, tracked)
ON CONFLICT (name) DO NOTHING;
