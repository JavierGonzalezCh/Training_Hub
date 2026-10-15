-- L2: logros de antiguedad y referidos. condition_type gana 'tenure_months' y 'referrals'.
-- ADD VALUE no se revierte (Down no lo toca) y no puede usarse en la misma transaccion en que se crea:
-- nada de esta migracion inserta filas con esos valores.
ALTER TYPE achievements.condition_type ADD VALUE IF NOT EXISTS 'tenure_months';
ALTER TYPE achievements.condition_type ADD VALUE IF NOT EXISTS 'referrals';
