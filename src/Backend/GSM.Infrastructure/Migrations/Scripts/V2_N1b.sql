-- N1b: los avisos de acción de solicitud de anulación ya creados (columna required_permissions nueva, nula)
-- quedan restringidos a quien puede ver o resolver solicitudes de anulación. Idempotente.
UPDATE notifications.notifications
SET required_permissions = ARRAY['ViewFinances', 'VoidPayments']
WHERE type = 'void_requested'
  AND requires_action
  AND required_permissions IS NULL;
