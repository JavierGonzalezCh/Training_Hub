-- V2_Q2 (MS4-1): los planes que no congelan (max_freeze_days = 0) pasan a 30 días, igual que los nuevos.
-- Idempotente: tras correr una vez ya no queda ningún 0 (salvo los que el Owner ponga después a propósito).
UPDATE memberships.membership_plans SET max_freeze_days = 30 WHERE max_freeze_days = 0;
