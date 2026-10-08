-- S5: solo una cuenta puede ser SuperAdmin. Si la base ya tiene mas de una, no se elige cual se queda
-- (eso es una decision de negocio): la migracion falla con un mensaje claro antes de crear el indice.
DO $$
BEGIN
    IF (SELECT count(*) FROM identity.users WHERE is_super_admin) > 1 THEN
        RAISE EXCEPTION 'V2_S5: hay mas de una cuenta con is_super_admin; deja solo una (desmarca las demas) antes de migrar.';
    END IF;
END $$;
