-- RT2: las portadas de las plantillas base pasan de los SVG de muestra a la galería de fotos (routine-images).
UPDATE routines.routine_templates SET image_url = '/_content/GSM.UI.Shared/routine-images/' ||
    CASE base_key
        WHEN 'full-body-principiante' THEN 'sala-mancuernas'
        WHEN 'torso-pierna' THEN 'prensa-piernas'
        WHEN 'push-pull-legs' THEN 'press-banca'
        WHEN 'fuerza-5x5' THEN 'peso-muerto'
        WHEN 'cardio-core' THEN 'cintas-correr'
    END || '.jpg'
WHERE image_url LIKE '/_content/GSM.UI.Shared/routine-templates/%.svg'
  AND base_key IN ('full-body-principiante', 'torso-pierna', 'push-pull-legs', 'fuerza-5x5', 'cardio-core');
