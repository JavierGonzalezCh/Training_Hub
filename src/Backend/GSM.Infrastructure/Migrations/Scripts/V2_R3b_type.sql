-- R3b, paso 1: nuevo valor del tipo de medida (peso + distancia). Corre en su propia migracion
-- (V2_R3b_WeightDistanceType): Postgres no deja USAR un valor de enum en la misma transaccion en que se
-- agrega con ADD VALUE, asi que el UPDATE del paso 2 va en la migracion siguiente (V2_R3b_FarmerWalk).
-- ADD VALUE no se revierte (Down no lo deshace). IF NOT EXISTS lo hace idempotente: si el AlterDatabase
-- generado por EF ya lo agrego, este script no hace nada.
ALTER TYPE routines.tracking_type ADD VALUE IF NOT EXISTS 'weight_distance';
