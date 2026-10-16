-- A7: avisos por push de Attendance. Un valor nuevo de tipo de aviso: la entrada con documento.
--
-- Migracion esperada (la genera el integrador sobre el modelo unido, p. ej. V2_A7_AvisosAsistencia) y que debe ejecutar
-- este script con migrationBuilder.SqlScript("A7"). No hay cambios de tablas ni de columnas: el modelo de EF solo
-- cambia por el valor nuevo del enum de C# (NotificationType.DocumentCheckIn), que EF no representa en el snapshot.
--
-- ADD VALUE no se revierte (Down no lo toca) y NO puede usarse en la misma transaccion en que se crea:
-- nada de esta migracion inserta filas con ese valor.
ALTER TYPE notifications.notification_type ADD VALUE IF NOT EXISTS 'document_check_in';
