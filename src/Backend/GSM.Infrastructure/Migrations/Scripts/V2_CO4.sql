-- CO4: anuncios programados. El estado nuevo 'scheduled' se agrega al enum nativo. ADD VALUE IF NOT EXISTS es
-- idempotente y, desde Postgres 12, corre dentro de una transaccion siempre que el valor no se USE en ella:
-- por eso el indice de scheduled_for es parcial por columna (scheduled_for IS NOT NULL) y no por status.
-- Las columnas scheduled_for / image_url y el indice los genera EF en la misma migracion.
ALTER TYPE communications.announcement_status ADD VALUE IF NOT EXISTS 'scheduled';
