-- V2_S2: planes del SaaS con límite de miembros (SuperAdmin v2, §V1).
--
-- Se ejecuta DESPUÉS de que la migración cree superadmin.saas_plans y las columnas
-- gym_subscriptions.saas_plan_id / member_limit_override (y su FK). Requiere PostgreSQL 18 (uuidv7()).
-- Idempotente.
--
-- Los tres planes del enum gym_plan pasan a filas con esos nombres. Límites aprobados el 2026-10-07:
-- Basic 150, Pro 500, Enterprise sin límite. El precio sugerido es orientativo (el real es el de cada
-- suscripción): 0 hasta que el SuperAdmin lo fije desde el panel.
INSERT INTO superadmin.saas_plans (id, name, member_limit, suggested_price, is_active, created_at, updated_at)
VALUES
    (uuidv7(), 'Basic',      150,  0, true, now(), now()),
    (uuidv7(), 'Pro',        500,  0, true, now(), now()),
    (uuidv7(), 'Enterprise', NULL, 0, true, now(), now())
ON CONFLICT (name) DO NOTHING;

-- Cada suscripción existente se enlaza con la fila del plan que tenía en el enum. El enum se conserva
-- (Gym.Plan lo sigue usando) y la columna gym_subscriptions.plan no se toca.
UPDATE superadmin.gym_subscriptions s
SET saas_plan_id = p.id
FROM superadmin.saas_plans p
WHERE s.saas_plan_id IS NULL
  AND lower(p.name) = s.plan::text;
