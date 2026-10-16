-- L6: ranking voluntario. El alias es unico por gym sin distinguir mayusculas (evita suplantar a otro participante).
-- EF no expresa indices funcionales, asi que va a mano. La tabla achievements.ranking_participants y la columna
-- gyms.gym_feature_flags.show_ranking (default true) las genera la migracion.
CREATE UNIQUE INDEX IF NOT EXISTS ix_ranking_participants_gym_id_alias_lower
    ON achievements.ranking_participants (gym_id, lower(alias))
    WHERE alias IS NOT NULL;
