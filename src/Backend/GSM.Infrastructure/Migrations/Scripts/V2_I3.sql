-- I3 — Perfiles y permisos de staff (docs/GSM-modulo-identity.md §3.5 y §5).
--
-- ORDEN: este script debe correr DESPUÉS de crear la columna identity.gym_accesses.profile (y el tipo
-- identity.staff_profile) y no depende de nada más. Si el integrador regenera la migración sobre el
-- modelo unido, debe conservar la llamada SqlScript("I3") después del AddColumn de `profile`.
--
-- El staff que ya existe queda con el perfil Administrador: es lo más parecido a lo que podía hacer
-- hasta hoy (todo) y el Owner lo ajusta después. Idempotente: solo toca accesos de staff sin perfil.
UPDATE identity.gym_accesses
SET profile = 'administrator'
WHERE role = 'staff'
  AND profile IS NULL;
