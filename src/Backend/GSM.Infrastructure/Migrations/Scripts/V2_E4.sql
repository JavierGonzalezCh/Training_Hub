-- E4: reportes de falla de máquinas (Equipment §3.5, §7).
--
-- Migración esperada (la genera el integrador sobre el modelo unido, p. ej. V2_E4_ReportesFallas) y que debe ejecutar
-- este script con migrationBuilder.SqlScript("E4"):
--   * enums equipment.equipment_issue_reason (not_working, damaged_part, strange_noise, missing_part, dirty,
--     unsafe, other), equipment.equipment_issue_status (open, in_progress, resolved, dismissed) y
--     equipment.equipment_issue_reporter_role (member, staff)  -- los crea EF
--   * tabla equipment.equipment_issues (EquipmentIssue) con
--       ck_equipment_issues_other_description  (reason <> 'other' OR description IS NOT NULL)
--       fk_equipment_issues_equipment_items    (item_id -> equipment.equipment_items, Restrict)
--       ix_equipment_issues_item_reporter_unclosed  único parcial (item_id, reported_by_user_id)
--           WHERE reported_by_role = 'member' AND status IN ('open', 'in_progress')
--   * columna gyms.gym_feature_flags.members_can_report_equipment boolean NOT NULL DEFAULT true
--     (los gyms existentes la heredan encendida: «permitidos por defecto»)
--
-- Los dos valores nuevos de notifications.notification_type NO se usan en esta misma migración (no se inserta ninguna
-- fila con ellos), así que ADD VALUE puede correr dentro de su transacción. ADD VALUE no se revierte: el Down no lo toca.
ALTER TYPE notifications.notification_type ADD VALUE IF NOT EXISTS 'equipment_issue_reported';
ALTER TYPE notifications.notification_type ADD VALUE IF NOT EXISTS 'equipment_issue_resolved';
