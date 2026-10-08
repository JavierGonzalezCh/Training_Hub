-- N1: la bandeja del miembro y la cola de correos pasan de Comunicaciones a Notifications, y los intentos
-- de entrada sin atender pasan a ser avisos de acción del staff. Corre dentro de la migración
-- V2_N1_Notifications, después de crear las tablas nuevas y antes de soltar las viejas.

-- 1) communications.member_notifications -> notifications.notifications.
--    Todo lo viejo ya "salió": estado sent, programado para el día en que se creó.
INSERT INTO notifications.notifications (
    id, gym_id, recipient_user_id, recipient_member_id, type, category, title, body, link, priority,
    scheduled_for, status, reference_id, requires_action, read_at, resolved_at, resolved_by, sent_at,
    created_at, updated_at)
SELECT
    n.id, n.gym_id, NULL, n.member_id,
    n.type::text::notifications.notification_type,
    (CASE n.type::text
        WHEN 'class_cancelled' THEN 'classes'
        WHEN 'membership_expiring' THEN 'membership'
        ELSE 'progress'
     END)::notifications.notification_category,
    n.title, n.body, n.link, 'normal',
    n.created_at, 'sent', n.reference_id, false, n.read_at, NULL, NULL, n.created_at,
    n.created_at, n.updated_at
FROM communications.member_notifications n;

-- 2) Los correos de anuncios no tenían aviso propio (el anuncio vive en el feed): se les crea uno ya leído,
--    de tipo anuncio, para que el correo tenga a qué colgarse. No aparece en la bandeja del miembro.
INSERT INTO notifications.notifications (
    id, gym_id, recipient_user_id, recipient_member_id, type, category, title, body, link, priority,
    scheduled_for, status, reference_id, requires_action, read_at, resolved_at, resolved_by, sent_at,
    created_at, updated_at)
SELECT
    o.id, o.gym_id, NULL, o.member_id,
    (CASE o.kind::text
        WHEN 'announcement' THEN 'announcement'
        WHEN 'class_cancelled' THEN 'class_cancelled'
        ELSE 'membership_expiring'
     END)::notifications.notification_type,
    (CASE o.kind::text
        WHEN 'announcement' THEN 'news'
        WHEN 'class_cancelled' THEN 'classes'
        ELSE 'membership'
     END)::notifications.notification_category,
    left(o.subject, 160), '', NULL, 'normal',
    o.created_at, 'sent', o.reference_id, false, o.created_at, NULL, NULL, o.created_at,
    o.created_at, o.updated_at
FROM communications.email_outbox o
WHERE NOT EXISTS (
    SELECT 1 FROM notifications.notifications n
    WHERE n.recipient_member_id = o.member_id
      AND n.reference_id = o.reference_id
      AND n.recipient_user_id IS NULL
      AND n.type::text = o.kind::text);

-- 3) communications.email_outbox -> notifications.notification_deliveries (canal email).
INSERT INTO notifications.notification_deliveries (
    id, gym_id, notification_id, channel, status, attempts, next_attempt_at, last_error, sent_at,
    to_address, subject, html_body, text_body, created_at, updated_at)
SELECT
    o.id, o.gym_id, n.id, 'email', o.status::text::notifications.delivery_status, o.attempts, NULL,
    o.last_error, o.sent_at, o.to_address, o.subject, o.html_body, o.text_body, o.created_at, o.updated_at
FROM communications.email_outbox o
JOIN notifications.notifications n
  ON n.recipient_member_id = o.member_id
 AND n.reference_id = o.reference_id
 AND n.recipient_user_id IS NULL
 AND n.type::text = o.kind::text;

-- 4) El dispatcher solo toma avisos pendientes: los correos que aún no salieron dejan su aviso pendiente
--    (y los que agotaron sus intentos lo dejan fallido).
UPDATE notifications.notifications n
SET status = 'pending', sent_at = NULL
WHERE EXISTS (
    SELECT 1 FROM notifications.notification_deliveries d
    WHERE d.notification_id = n.id AND d.status = 'pending');

UPDATE notifications.notifications n
SET status = 'failed'
WHERE n.status = 'sent'
  AND EXISTS (SELECT 1 FROM notifications.notification_deliveries d WHERE d.notification_id = n.id)
  AND NOT EXISTS (
      SELECT 1 FROM notifications.notification_deliveries d
      WHERE d.notification_id = n.id AND d.status <> 'failed');

-- 5) Intentos de entrada rechazados que nadie ha visto (últimos 90 días) -> avisos de acción del gym,
--    para que no desaparezcan de la campana. Los ya vistos no se migran. La referencia es el id del intento.
INSERT INTO notifications.notifications (
    id, gym_id, recipient_user_id, recipient_member_id, type, category, title, body, link, priority,
    scheduled_for, status, reference_id, requires_action, read_at, resolved_at, resolved_by, sent_at,
    created_at, updated_at)
SELECT
    a.id, a.gym_id, NULL, NULL, 'checkin_rejected', NULL, 'Entrada rechazada',
    coalesce(nullif(trim(m.first_name || ' ' || m.last_name), ''), 'Un miembro')
        || ' · '
        || (CASE
                WHEN a.reason::text = 'no_membership' THEN 'Sin membresía'
                WHEN a.membership_ended_on IS NOT NULL THEN 'Venció el ' || to_char(a.membership_ended_on, 'DD/MM/YYYY')
                ELSE 'Membresía vencida'
            END)
        || (CASE WHEN a.attempt_count > 1 THEN ' · ' || a.attempt_count || ' intentos' ELSE '' END),
    '/members/' || a.member_id, 'normal',
    a.attempted_at, 'sent', a.id, true, NULL, NULL, NULL, a.attempted_at,
    a.created_at, a.updated_at
FROM attendance.checkin_attempts a
LEFT JOIN members.members m ON m.id = a.member_id
WHERE a.acknowledged_at IS NULL
  AND a.attempted_at > now() - interval '90 days';
