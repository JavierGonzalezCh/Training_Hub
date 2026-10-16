-- LG5: alertas al SuperAdmin (docs/GSM-logs.md §8).
-- alert_state: la última vez que se emitió cada alerta (por clave). Es el candado atómico que impide repetir la
-- misma alerta antes de 30 minutos, aunque haya varias copias de la API evaluando a la vez.
CREATE TABLE IF NOT EXISTS logs.alert_state (
    key text NOT NULL,
    last_alerted_at timestamptz NOT NULL,
    CONSTRAINT pk_alert_state PRIMARY KEY (key)
);

CREATE INDEX IF NOT EXISTS ix_alert_state_last_alerted_at ON logs.alert_state (last_alerted_at);

-- platform_alerts: cada alerta emitida y el estado de su entrega por correo y push. Sin gym ni usuario destinatario:
-- siempre es para el único SuperAdmin.
CREATE TABLE IF NOT EXISTS logs.platform_alerts (
    id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    key text NOT NULL,
    kind varchar(24) NOT NULL,
    title varchar(200) NOT NULL,
    body text NOT NULL,
    link varchar(200) NULL,
    email_status varchar(12) NOT NULL DEFAULT 'pending',
    push_status varchar(12) NOT NULL DEFAULT 'pending',
    attempts integer NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NULL,
    last_error text NULL,
    CONSTRAINT pk_platform_alerts PRIMARY KEY (id),
    CONSTRAINT ck_platform_alerts_kind CHECK (kind IN ('new_error', 'spike', 'process_stopped', 'external_down', 'security')),
    CONSTRAINT ck_platform_alerts_email_status CHECK (email_status IN ('pending', 'sent', 'skipped', 'failed')),
    CONSTRAINT ck_platform_alerts_push_status CHECK (push_status IN ('pending', 'sent', 'skipped', 'failed'))
);

CREATE INDEX IF NOT EXISTS ix_platform_alerts_created_at ON logs.platform_alerts (created_at DESC);

-- Las que aún esperan entrega (lo que recorre cada vuelta del evaluador).
CREATE INDEX IF NOT EXISTS ix_platform_alerts_pending
    ON logs.platform_alerts (created_at)
    WHERE email_status = 'pending' OR push_status = 'pending';
