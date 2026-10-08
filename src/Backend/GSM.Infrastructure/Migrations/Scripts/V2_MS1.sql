-- V2_MS1: la gracia pasa de Attendance a Memberships (Memberships v2, §5.1 y §13).
--
-- La migración V2_MS1_CommercialStatus ya creó memberships.membership_settings. Este script se
-- ejecuta ANTES de borrar attendance.checkin_settings.grace_days (si una regeneración de la
-- migración lo deja después, los días de gracia configurados por cada gym se pierden).
-- Requiere PostgreSQL 18 (uuidv7()).
--
-- Solo se copian los gyms que tenían fila de ajustes de check-in: los demás siguen con los valores
-- por defecto (patrón perezoso: sin fila valen 3 de gracia y 7 de aviso). expiring_days arranca en 7,
-- el número que ya usaba el dashboard.
INSERT INTO memberships.membership_settings (id, gym_id, grace_days, expiring_days, created_at, updated_at)
SELECT uuidv7(), c.gym_id, c.grace_days, 7, now(), now()
FROM attendance.checkin_settings c
ON CONFLICT (gym_id) DO NOTHING;
