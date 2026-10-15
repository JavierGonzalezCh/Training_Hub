-- V2_MS2: precio acordado, descuento y matrícula (Memberships v2, §4, §6 y §13).
--
-- Orden esperado de la migración V2_MS2_PricingEnrollment:
--   1. AddColumn memberships.memberships: agreed_price decimal(12,2) NOT NULL default 0,
--      discount_reason varchar(500) NULL, enrollment_fee decimal(12,2) NOT NULL default 0,
--      enrollment_waived_reason varchar(500) NULL.
--   2. AddColumn memberships.membership_settings: enrollment_fee decimal(12,2) NOT NULL default 0.
--   3. migrationBuilder.SqlScript("MS2")  <- este script (rellena agreed_price de lo ya existente).
-- Si una regeneración de la migración pone el script antes de las columnas, falla; si lo omite,
-- las membresías existentes quedan con agreed_price = 0.
--
-- §13: agreed_price = suma de los pagos ligados no anulados si los tiene; si no, el precio actual
-- del plan. "No anulado" = ni el pago es un asiento inverso (reverses_payment_id nulo) ni existe otro
-- pago que lo revierta. enrollment_fee = 0 y renewal_kind = new ya son los valores por defecto /
-- los de MS1 y no se tocan. Solo se rellenan las filas aún sin precio (agreed_price = 0), así que
-- re-ejecutarlo no pisa nada.
UPDATE memberships.memberships m
SET agreed_price = COALESCE(
    (SELECT SUM(p.amount)
       FROM payments.payments p
      WHERE p.membership_id = m.id
        AND p.reverses_payment_id IS NULL
        AND NOT EXISTS (SELECT 1 FROM payments.payments r WHERE r.reverses_payment_id = p.id)),
    (SELECT pl.price FROM memberships.membership_plans pl WHERE pl.id = m.plan_id),
    0)
WHERE m.agreed_price = 0;
