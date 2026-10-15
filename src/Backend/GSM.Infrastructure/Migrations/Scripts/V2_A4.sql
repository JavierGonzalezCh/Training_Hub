-- A4: entrada con documento en el kiosco. El metodo de asistencia gana el valor 'document'.
-- ADD VALUE no se revierte (Down no lo toca) y no puede usarse en la misma transaccion en que se crea:
-- nada de esta migracion inserta filas con ese valor.
ALTER TYPE attendance.attendance_method ADD VALUE IF NOT EXISTS 'document';
