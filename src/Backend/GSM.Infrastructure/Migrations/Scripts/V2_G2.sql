-- V2_G2: datos nuevos del gym (Gyms v2, §3).
--
-- La migración V2_G2_GymProfile ya creó las columnas nuevas (legal_name, address, city, latitude,
-- longitude, geofence_radius_meters, instagram_url, facebook_url, tik_tok_url, website_url,
-- whats_app_phone). Este script solo normaliza el teléfono existente a E.164 (+57 por defecto).
-- Misma regla que GSM.Domain.Members.PhoneNumber y que V2_M1.sql: se quitan espacios, guiones,
-- paréntesis y puntos; vale +<8-15 dígitos>, 00<...>, 10 dígitos nacionales (móvil 3... o fijo con
-- indicativo 6...) o 57 + esos 10 dígitos. Lo que no se puede interpretar se deja tal cual (el gym
-- sigue funcionando y el Owner lo corrige al editar; guardarlo de nuevo exige un número válido).
-- Los vacíos pasan a NULL. Idempotente.

UPDATE gyms.gyms SET phone = NULL WHERE phone IS NOT NULL AND btrim(phone) = '';

UPDATE gyms.gyms g
SET phone = CASE
        WHEN x.cleaned ~ '^\+[1-9][0-9]{7,14}$' THEN x.cleaned
        WHEN x.cleaned ~ '^00[1-9][0-9]{7,14}$' THEN '+' || substr(x.cleaned, 3)
        WHEN x.cleaned ~ '^[36][0-9]{9}$' THEN '+57' || x.cleaned
        WHEN x.cleaned ~ '^57[36][0-9]{9}$' THEN '+' || x.cleaned
        ELSE g.phone
    END
FROM (
    SELECT id, regexp_replace(btrim(phone), '[\s\-\(\)\.]', '', 'g') AS cleaned
    FROM gyms.gyms
    WHERE phone IS NOT NULL
) x
WHERE x.id = g.id;
