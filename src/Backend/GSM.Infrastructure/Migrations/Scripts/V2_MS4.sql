-- V2_MS4: congelamientos con reglas por plan y programados (Memberships v2, §3, §5.2 y §13).
--
-- Orden esperado de la migración V2_MS4_Congelamientos (el integrador la genera con `dotnet ef`):
--   1. AddColumn memberships.membership_plans.max_freeze_days integer NOT NULL default 0
--      (§13: los planes existentes migran con MaxFreezeDays = 0, o sea "no se congela"; el Owner puede
--      saltarse el límite con motivo, o editar el plan).
--   2. AddColumn memberships.membership_plans.min_freeze_days integer NOT NULL default 1.
--   3. CreateTable memberships.membership_freezes (id, gym_id, membership_id FK -> memberships ON DELETE CASCADE,
--      starts_on date, ends_on date NULL, reason varchar(500), override_limit bool, days_credited int,
--      created_by uuid NULL, created_at, updated_at) + ck_membership_freezes_range + ck_membership_freezes_days_credited
--      + ix_membership_freezes_membership_id + ix_membership_freezes_gym_id_starts_on.
--   4. migrationBuilder.SqlScript("MS4")  <- este script: copia los `paused` actuales a congelamientos
--      abiertos y los devuelve a `active`. DEBE ir DESPUÉS de crear la tabla y ANTES de soltar paused_on.
--   5. DropColumn memberships.memberships.paused_on.
--
-- No hay ALTER TYPE: el label 'paused' sigue existiendo en memberships.membership_status (ya no se escribe).
-- Idempotente: no duplica el congelamiento si se ejecuta dos veces.

-- Las congeladas hoy (`paused` + `paused_on`) pasan a un congelamiento ABIERTO desde paused_on, con el
-- motivo "Migrado". override_limit = true para que, al reanudar, se acrediten todos los días que estuvieron
-- congeladas aunque el plan migrado tenga max_freeze_days = 0.

-- (paused_on puede no existir ya si el script se ejecuta después de soltar la columna: se resuelve en el DO.)
DO $ms4$
DECLARE
    start_expr text := CASE WHEN EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'memberships' AND table_name = 'memberships' AND column_name = 'paused_on')
        THEN 'COALESCE(m.paused_on, m.updated_at::date)' ELSE 'm.updated_at::date' END;
BEGIN
    EXECUTE 'INSERT INTO memberships.membership_freezes (id, gym_id, membership_id, starts_on, ends_on, reason, override_limit, days_credited, created_by, created_at, updated_at) SELECT gen_random_uuid(), m.gym_id, m.id, ' || start_expr || ', NULL, ''Migrado'', TRUE, 0, NULL, now(), now() FROM memberships.memberships m WHERE m.status = ''paused'' AND NOT EXISTS (SELECT 1 FROM memberships.membership_freezes f WHERE f.membership_id = m.id AND f.ends_on IS NULL)';
END
$ms4$;

-- `paused` deja de guardarse: se calcula con los congelamientos.
UPDATE memberships.memberships SET status = 'active' WHERE status = 'paused';
