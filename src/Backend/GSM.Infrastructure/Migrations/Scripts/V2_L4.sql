-- L4: logros otorgados por el staff. condition_type gana 'manual'.
-- ADD VALUE no se revierte (Down no lo toca) y no puede usarse en la misma transaccion en que se crea:
-- nada de esta migracion inserta filas con ese valor.
ALTER TYPE achievements.condition_type ADD VALUE IF NOT EXISTS 'manual';
