-- I2 — Una cuenta, varios gyms: cada AppUser actual genera su GymAccess (docs/GSM-modulo-identity.md §3.5).
--
-- ORDEN: este script debe correr DESPUÉS de crear identity.gym_accesses, identity.users.is_super_admin y
-- identity.refresh_tokens.gym_access_id, y ANTES de borrar identity.users.gym_id / identity.users.role,
-- porque lee esas dos columnas viejas. Si el integrador regenera la migración sobre el modelo unido,
-- debe conservar ese orden: crear, SqlScript("I2"), borrar.
--
-- Idempotente: se puede correr dos veces sin duplicar accesos.

-- 1. El superadmin deja de ser un rol de la cuenta y pasa a ser una marca.
UPDATE identity.users
SET is_super_admin = TRUE
WHERE role = 'superadmin';

-- 2. Cada usuario con gym recibe su acceso, con el rol que tenía. Un usuario desactivado (el staff que
--    el owner dio de baja) queda con su ACCESO inactivo; la cuenta vuelve a quedar activa porque
--    "cuenta inactiva" pasa a significar solo "desactivada por completo por el SuperAdmin", y así la
--    persona puede recibir acceso a otro gym. Sin acceso activo no puede entrar a ninguno.
INSERT INTO identity.gym_accesses
    (id, user_id, gym_id, role, is_team, is_active, deactivated_at, last_used_at, created_at, updated_at)
SELECT
    uuidv7(),
    u.id,
    u.gym_id,
    u.role,
    u.role IN ('owner', 'staff'),
    u.is_active,
    CASE WHEN u.is_active THEN NULL ELSE now() END,
    NULL,
    now(),
    now()
FROM identity.users u
WHERE u.gym_id IS NOT NULL
  AND u.role <> 'superadmin'
  AND NOT EXISTS (
      SELECT 1
      FROM identity.gym_accesses a
      WHERE a.user_id = u.id
        AND a.gym_id = u.gym_id
        AND a.is_team = (u.role IN ('owner', 'staff')));

UPDATE identity.users u
SET is_active = TRUE
WHERE u.is_active = FALSE
  AND u.is_super_admin = FALSE
  AND EXISTS (SELECT 1 FROM identity.gym_accesses a WHERE a.user_id = u.id);

-- 3. Las sesiones abiertas (refresh tokens) siguen valiendo: se atan al único acceso que tenía su dueño.
UPDATE identity.refresh_tokens rt
SET gym_access_id = a.id
FROM identity.gym_accesses a
WHERE a.user_id = rt.user_id
  AND rt.gym_access_id IS NULL;
