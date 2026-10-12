-- A1: cuatro motivos de rechazo nuevos (Attendance v2, §3). ALTER TYPE ... ADD VALUE no se revierte.
-- Idempotente: si la migración generada ya los añadió (AlterDatabase de Npgsql), IF NOT EXISTS lo tolera.
ALTER TYPE attendance.checkin_rejection_reason ADD VALUE IF NOT EXISTS 'membership_frozen';
ALTER TYPE attendance.checkin_rejection_reason ADD VALUE IF NOT EXISTS 'pass_exhausted';
ALTER TYPE attendance.checkin_rejection_reason ADD VALUE IF NOT EXISTS 'member_inactive';
ALTER TYPE attendance.checkin_rejection_reason ADD VALUE IF NOT EXISTS 'already_checked_in';
