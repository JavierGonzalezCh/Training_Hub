-- V2_G5: onboarding obligatorio del gym (Gyms v2, §5).
--
-- La migración crea gyms.gym_onboarding (id, gym_id único con FK a gyms.gyms en cascada, completed_at,
-- skipped_by, skipped_at, skip_reason, created_at, updated_at). Este script solo marca el onboarding de
-- los gyms que YA existen como completado, para no bloquear a los clientes actuales (Gyms §5, «Gyms
-- existentes»). Los gyms nuevos no tienen fila: el onboarding les nace pendiente.
-- Idempotente: no toca un gym que ya tenga fila.

INSERT INTO gyms.gym_onboarding (id, gym_id, completed_at, created_at, updated_at)
SELECT uuidv7(), g.id, now(), now(), now()
FROM gyms.gyms g
WHERE NOT EXISTS (SELECT 1 FROM gyms.gym_onboarding o WHERE o.gym_id = g.id);
