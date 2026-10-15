-- V2_MS3: planes en meses del calendario (Memberships v2, §3, §4 y §13).
--
-- Orden esperado de la migración V2_MS3_MesesCalendario (el integrador la genera con `dotnet ef`):
--   1. AlterDatabase: nuevo enum memberships.duration_unit ('days','months'), SnakeCaseNameTranslator.
--   2. RenameColumn memberships.membership_plans.duration_days -> duration_value
--      (OJO: EF genera DropColumn + AddColumn; hay que cambiarlo a RenameColumn para no perder los datos).
--   3. AddColumn memberships.membership_plans.duration_unit duration_unit NOT NULL default 'days'
--      (los planes existentes quedan como 'days' y su duración no cambia).
--   4. AddColumn memberships.memberships.cycle_day integer NULL (nulo = plan por días).
--   5. migrationBuilder.SqlScript("MS3")  <- este script.
--
-- §13: los planes actuales migran como DurationUnit = days, DurationValue = DurationDays. Las
-- membresías existentes pertenecen a planes por días, así que cycle_day queda nulo. Lo único que
-- hace este script es dejar la restricción de rango; es idempotente.
ALTER TABLE memberships.memberships DROP CONSTRAINT IF EXISTS ck_memberships_cycle_day;
ALTER TABLE memberships.memberships
    ADD CONSTRAINT ck_memberships_cycle_day CHECK (cycle_day IS NULL OR cycle_day BETWEEN 1 AND 31);
