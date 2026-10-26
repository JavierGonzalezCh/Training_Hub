-- A8: sin registro de entrada (QR y documento apagados), el miembro cuenta su dia al iniciar el entrenamiento.
-- El metodo de asistencia gana el valor 'routine'. ADD VALUE no se revierte (Down no lo toca) y no puede usarse
-- en la misma transaccion en que se crea: nada de esta migracion inserta filas con ese valor.
ALTER TYPE attendance.attendance_method ADD VALUE IF NOT EXISTS 'routine';
