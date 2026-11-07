-- AC1: los tres levantamientos del catalogo del sistema que cuentan para «Club de la Fuerza».
-- Solo el catalogo del sistema (gym_id nulo); idempotente: vuelve a marcar lo mismo.
UPDATE routines.exercises SET power_lift = 'squat'
 WHERE gym_id IS NULL AND name = 'Sentadilla con barra';
UPDATE routines.exercises SET power_lift = 'bench'
 WHERE gym_id IS NULL AND name = 'Press de banca plana con barra';
UPDATE routines.exercises SET power_lift = 'deadlift'
 WHERE gym_id IS NULL AND name = 'Peso muerto convencional';
