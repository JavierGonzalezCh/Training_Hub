-- V2_Q7: se elimina el enum viejo gym_plan (gyms.gyms.plan y superadmin.gym_subscriptions.plan).
--
-- Se ejecuta ANTES de que la migración suelte las dos columnas y el tipo. Pasa a la suscripción su plan del SaaS
-- por el nombre del enum a las que todavía no apuntan a uno (V2_S2 ya enlazó las existentes; esto cubre las
-- creadas después sin elegirlo). Idempotente.
UPDATE superadmin.gym_subscriptions s
SET saas_plan_id = p.id
FROM superadmin.saas_plans p
WHERE s.saas_plan_id IS NULL
  AND lower(p.name) = s.plan::text;
