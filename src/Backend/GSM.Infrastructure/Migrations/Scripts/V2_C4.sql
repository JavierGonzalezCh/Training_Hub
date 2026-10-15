-- C4: lista de espera de clases con confirmacion.
--
-- Migracion esperada (la genera el integrador sobre el modelo unido, p. ej. V2_C4_ListaEspera) y que debe
-- ejecutar este script con migrationBuilder.SqlScript("C4") DESPUES de las operaciones de esquema:
--   * enum classes.waitlist_entry_status (waiting, offered, confirmed, expired, left)  -- lo crea EF
--   * tabla classes.waitlist_entries (WaitlistEntry) con indices unicos
--       ix_waitlist_entries_session_position (session_id, position)
--       ix_waitlist_entries_session_member_open (session_id, member_id) WHERE status IN ('waiting','offered')
--   * columna classes.class_settings.waitlist_offer_minutes int NOT NULL DEFAULT 60  -- HasDefaultValue(60)
--
-- ADD VALUE no se revierte (Down no lo toca) y NO puede usarse en la misma transaccion en que se crea:
-- nada de esta migracion inserta filas con estos valores.
ALTER TYPE notifications.notification_type ADD VALUE IF NOT EXISTS 'waitlist_offer';
ALTER TYPE notifications.notification_type ADD VALUE IF NOT EXISTS 'waitlist_offer_expired';

-- Una oferta siempre tiene hora limite; una entrada que espera no. Se hace cumplir en la base.
ALTER TABLE classes.waitlist_entries DROP CONSTRAINT IF EXISTS ck_waitlist_entries_offer_expires;
ALTER TABLE classes.waitlist_entries
    ADD CONSTRAINT ck_waitlist_entries_offer_expires
    CHECK (status <> 'offered' OR offer_expires_at IS NOT NULL);

-- Rango valido del ajuste por gym (Domain: 5..720). Las filas existentes quedan en 60 por el DEFAULT de la columna.
ALTER TABLE classes.class_settings DROP CONSTRAINT IF EXISTS ck_class_settings_waitlist_offer_minutes;
ALTER TABLE classes.class_settings
    ADD CONSTRAINT ck_class_settings_waitlist_offer_minutes
    CHECK (waitlist_offer_minutes BETWEEN 5 AND 720);
