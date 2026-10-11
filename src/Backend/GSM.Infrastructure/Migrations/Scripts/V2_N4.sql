-- N4: las preferencias de correo pasan de un interruptor por miembro (communications.member_communication_preferences,
-- email_opt_out) a una fila por miembro y categoría (notifications.member_notification_preferences).
-- Quien tenía email_opt_out = true queda con el correo apagado en TODAS las categorías configurables
-- (membership, classes, progress, news, promotions) y el push con su valor por defecto (encendido, salvo
-- promotions). Quien no lo tenía no necesita filas: la tabla es perezosa y valen los valores por defecto.
-- Corre DESPUÉS de crear las tablas nuevas y ANTES de soltar la vieja. Idempotente: el índice único
-- (member_id, category) más ON CONFLICT DO NOTHING permiten repetirlo sin duplicar.
INSERT INTO notifications.member_notification_preferences
    (id, gym_id, member_id, category, push, email, created_at, updated_at)
SELECT
    gen_random_uuid(),
    p.gym_id,
    p.member_id,
    c.category,
    c.category <> 'promotions'::notifications.notification_category,
    FALSE,
    now(),
    now()
FROM communications.member_communication_preferences p
CROSS JOIN (
    VALUES
        ('membership'::notifications.notification_category),
        ('classes'::notifications.notification_category),
        ('progress'::notifications.notification_category),
        ('news'::notifications.notification_category),
        ('promotions'::notifications.notification_category)
) AS c(category)
WHERE p.email_opt_out
ON CONFLICT (member_id, category) DO NOTHING;
