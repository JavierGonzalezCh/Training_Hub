-- R3b, paso 2: Paseo del granjero del catalogo del sistema (gym_id IS NULL) pasa a medir peso + distancia.
-- Corre en una migracion POSTERIOR a la que agrega 'weight_distance' (V2_R3b_type.sql): el valor nuevo solo
-- se puede usar despues de que esa transaccion haga commit. Los datos del sistema solo los cambia una
-- migracion, nunca un test. Idempotente.
UPDATE routines.exercises
SET tracking_type = 'weight_distance'
WHERE gym_id IS NULL
  AND name = 'Paseo del granjero';
