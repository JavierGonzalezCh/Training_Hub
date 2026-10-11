-- N4 (Down): devuelve el interruptor único. Quien tiene el correo apagado en las cuatro categorías de
-- avisos habituales (todas menos promotions, que nace apagada) queda con email_opt_out = true.
INSERT INTO communications.member_communication_preferences
    (id, gym_id, member_id, email_opt_out, created_at, updated_at)
SELECT gen_random_uuid(), gym_id, member_id, TRUE, now(), now()
FROM notifications.member_notification_preferences
WHERE category IN ('membership', 'classes', 'progress', 'news') AND NOT email
GROUP BY gym_id, member_id
HAVING count(*) = 4
ON CONFLICT (member_id) DO NOTHING;
