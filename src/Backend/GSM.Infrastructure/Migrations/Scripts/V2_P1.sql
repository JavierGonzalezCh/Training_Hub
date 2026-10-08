-- P1: payments.payment_method pasa de 4 a 8 valores (cash, card, nequi, daviplata, breb, bank_transfer,
-- bank_deposit, other). 'transfer' se renombra a 'bank_transfer' (los pagos existentes cambian de valor solos,
-- porque la columna guarda el valor del enum). ADD VALUE no se revierte (Down no lo deshace).
-- Los valores nuevos no se usan en esta misma transaccion, asi que ADD VALUE es valido (PG 12+).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_enum e JOIN pg_type t ON t.oid = e.enumtypid
               JOIN pg_namespace n ON n.oid = t.typnamespace
               WHERE n.nspname = 'payments' AND t.typname = 'payment_method' AND e.enumlabel = 'transfer')
    THEN
        ALTER TYPE payments.payment_method RENAME VALUE 'transfer' TO 'bank_transfer';
    END IF;
END $$;
ALTER TYPE payments.payment_method ADD VALUE IF NOT EXISTS 'nequi';
ALTER TYPE payments.payment_method ADD VALUE IF NOT EXISTS 'daviplata';
ALTER TYPE payments.payment_method ADD VALUE IF NOT EXISTS 'breb';
ALTER TYPE payments.payment_method ADD VALUE IF NOT EXISTS 'bank_deposit';
