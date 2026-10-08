-- V2_M1: ficha ampliada del miembro (Members v2, §9).
--
-- La migración V2_M1_MemberProfile ya creó las columnas nuevas y las tablas member_changes y
-- member_notes. Este script se ejecuta ANTES de borrar members.members.emergency_contact (si una
-- regeneración de la migración lo deja después, el contacto de emergencia anterior se pierde).
-- Requiere PostgreSQL 18 (uuidv7()).

-- 1. document_type: todos los miembros actuales son 'cc' (default de la columna al añadirla).
--    El default solo servía para rellenar las filas existentes; el modelo no lo tiene.
ALTER TABLE members.members ALTER COLUMN document_type DROP DEFAULT;
ALTER TABLE members.members ALTER COLUMN tags DROP DEFAULT;

-- 2. Teléfonos a E.164 con +57 por defecto. Misma regla que GSM.Domain.Members.PhoneNumber:
--    se quitan espacios, guiones, paréntesis y puntos; vale +<8-15 dígitos>, 00<...>, 10 dígitos
--    nacionales (móvil 3... o fijo con indicativo 6...) o 57 + esos 10 dígitos.
--    Los que no se pueden interpretar se dejan tal cual: la ficha queda "incompleta"
--    (Member.IsProfileIncomplete se deriva de que el teléfono no sea E.164). Los vacíos pasan a NULL.
UPDATE members.members SET phone = NULL WHERE phone IS NOT NULL AND btrim(phone) = '';

UPDATE members.members m
SET phone = CASE
        WHEN x.cleaned ~ '^\+[1-9][0-9]{7,14}$' THEN x.cleaned
        WHEN x.cleaned ~ '^00[1-9][0-9]{7,14}$' THEN '+' || substr(x.cleaned, 3)
        WHEN x.cleaned ~ '^[36][0-9]{9}$' THEN '+57' || x.cleaned
        WHEN x.cleaned ~ '^57[36][0-9]{9}$' THEN '+' || x.cleaned
        ELSE m.phone
    END
FROM (
    SELECT id, regexp_replace(btrim(phone), '[\s\-\(\)\.]', '', 'g') AS cleaned
    FROM members.members
    WHERE phone IS NOT NULL
) x
WHERE x.id = m.id;

-- 3. El texto libre emergency_contact no se puede separar en nombre y teléfono de forma fiable:
--    pasa tal cual a una nota interna sin autor y los campos nuevos quedan vacíos.
INSERT INTO members.member_notes (id, gym_id, member_id, text, author_user_id, created_at, updated_at)
SELECT uuidv7(),
       gym_id,
       id,
       'Contacto de emergencia (dato anterior): ' || btrim(emergency_contact),
       NULL,
       now(),
       now()
FROM members.members
WHERE emergency_contact IS NOT NULL AND btrim(emergency_contact) <> '';

-- 4. birth_date: sin cambios. Las fichas sin fecha quedan "incompletas" (se deriva, no se persiste).
