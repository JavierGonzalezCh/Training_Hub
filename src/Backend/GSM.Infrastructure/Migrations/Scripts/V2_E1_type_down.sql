-- E1 (Down): devuelve el enum de equipos al schema `routines` con su nombre original.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'equipment' AND t.typname = 'equipment_type') THEN
        ALTER TYPE equipment.equipment_type RENAME TO equipment;
        ALTER TYPE equipment.equipment SET SCHEMA routines;
    END IF;
END $$;
