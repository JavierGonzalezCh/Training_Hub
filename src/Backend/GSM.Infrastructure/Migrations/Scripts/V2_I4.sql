-- I4 + G4 — Varios Owners, traspaso, ascender staff y contacto de facturación
-- (docs/GSM-modulo-identity.md y docs/GSM-modulo-gyms.md §6).
--
-- ORDEN: este script debe correr DESPUÉS de crear la columna gyms.gyms.billing_contact_user_id. Si el
-- integrador regenera la migración sobre el modelo unido, debe conservar la llamada SqlScript("I4") después
-- del AddColumn.
--
-- 1) identity.staff_access_audit_action gana los cambios de Owners. ADD VALUE no se revierte (Down no lo
--    deshace) y los valores nuevos no se usan en esta misma transacción (válido en PG 12+).
ALTER TYPE identity.staff_access_audit_action ADD VALUE IF NOT EXISTS 'owner_added';
ALTER TYPE identity.staff_access_audit_action ADD VALUE IF NOT EXISTS 'owner_promoted';
ALTER TYPE identity.staff_access_audit_action ADD VALUE IF NOT EXISTS 'owner_removed';
ALTER TYPE identity.staff_access_audit_action ADD VALUE IF NOT EXISTS 'owner_demoted';

-- 2) El Owner fundador pasa a ser el contacto de facturación de cada gym existente. Idempotente.
UPDATE gyms.gyms
SET billing_contact_user_id = owner_id
WHERE billing_contact_user_id IS NULL;
