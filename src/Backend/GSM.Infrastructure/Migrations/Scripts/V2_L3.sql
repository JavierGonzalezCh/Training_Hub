-- L3: logros de records personales y volumen. condition_type gana 'personal_records' y 'lifted_volume'.
-- ADD VALUE no se revierte (Down no lo toca) y no puede usarse en la misma transaccion en que se crea:
-- nada de esta migracion inserta filas con esos valores.
ALTER TYPE achievements.condition_type ADD VALUE IF NOT EXISTS 'personal_records';
ALTER TYPE achievements.condition_type ADD VALUE IF NOT EXISTS 'lifted_volume';
