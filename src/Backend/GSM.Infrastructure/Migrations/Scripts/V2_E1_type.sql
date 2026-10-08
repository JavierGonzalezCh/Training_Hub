-- E1: el enum de equipos pasa de Routines al módulo Equipment, sin tocar sus valores.
-- Corre ANTES de las operaciones de esquema de la migración V2_E1_Equipment (el schema `equipment` ya existe).
-- Al mover el tipo, `routines.exercises.equipment` (equipment[]) y todo lo que lo usa lo siguen por OID:
-- no hay que reescribir datos. Después, el AlterDatabase generado de la migración ya no encuentra nada que
-- crear ni borrar (Npgsql usa IF NOT EXISTS / IF EXISTS).
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'routines' AND t.typname = 'equipment') THEN
        ALTER TYPE routines.equipment SET SCHEMA equipment;
        ALTER TYPE equipment.equipment RENAME TO equipment_type;
    END IF;
END $$;
