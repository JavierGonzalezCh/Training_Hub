-- V2_M6: anonimizacion de miembros (Members v2, §11).
--
-- La migracion agrega members.members.deactivated_on / anonymized_at / anonymization_notice_sent_on y crea
-- members.member_settings. Este script se ejecuta DESPUES de crear las columnas: da un dia de baja a las fichas que YA
-- estan dadas de baja, para que el plazo de anonimizacion corra desde algun punto.
--
-- Decision conservadora: se les pone el dia de la migracion (America/Bogota), NO la fecha real de la baja. Asi nadie se
-- anonimiza de golpe en la primera ejecucion de la tarea por una baja de hace anios; el plazo completo del gym (24 meses
-- por defecto) corre desde hoy. Idempotente: no toca a quien ya tiene dia de baja.
UPDATE members.members
SET deactivated_on = (now() AT TIME ZONE 'America/Bogota')::date
WHERE status = 'inactive'
  AND deactivated_on IS NULL
  AND anonymized_at IS NULL;
