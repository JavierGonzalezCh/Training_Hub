-- I2 (reversa) — vuelve a un gym y un rol por cuenta. Es con pérdida: si la persona tiene varios accesos,
-- se queda con el de equipo (si lo hay) y, entre iguales, con el último usado. Debe correr DESPUÉS de
-- volver a crear identity.users.gym_id / role y ANTES de borrar identity.gym_accesses.
UPDATE identity.users u
SET role = 'superadmin', gym_id = NULL
WHERE u.is_super_admin;

UPDATE identity.users u
SET role = best.role, gym_id = best.gym_id, is_active = (u.is_active AND best.is_active)
FROM (
    SELECT DISTINCT ON (user_id) user_id, gym_id, role, is_active
    FROM identity.gym_accesses
    ORDER BY user_id, is_active DESC, is_team DESC, last_used_at DESC NULLS LAST, created_at
) best
WHERE best.user_id = u.id
  AND NOT u.is_super_admin;
