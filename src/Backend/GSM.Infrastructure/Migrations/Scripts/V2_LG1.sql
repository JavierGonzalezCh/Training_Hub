-- LG1: schema `logs` (docs/GSM-logs.md). Las tablas no son de ningún gym ni del modelo de GsmDbContext:
-- las mapea LogsDbContext, con su propia conexión. Todo SQL a mano porque `errors` va particionada por mes
-- (la PK incluye la columna de partición: PostgreSQL lo exige).
CREATE SCHEMA IF NOT EXISTS logs;

CREATE TABLE IF NOT EXISTS logs.errors (
    id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    source varchar(16) NOT NULL,
    method varchar(16) NULL,
    path varchar(512) NULL,
    endpoint varchar(512) NULL,
    exception_type varchar(256) NOT NULL,
    message text NOT NULL,
    stack_trace text NULL,
    inner_exceptions text NULL,
    request_summary text NULL,
    fingerprint varchar(64) NOT NULL,
    CONSTRAINT pk_errors PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX IF NOT EXISTS ix_errors_trace_id ON logs.errors (trace_id);
CREATE INDEX IF NOT EXISTS ix_errors_fingerprint ON logs.errors (fingerprint, occurred_at);

CREATE TABLE IF NOT EXISTS logs.event_handler_failures (
    id uuid NOT NULL PRIMARY KEY,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    event_type varchar(512) NOT NULL,
    handler_type varchar(512) NOT NULL,
    payload jsonb NOT NULL,
    exception_type varchar(256) NOT NULL,
    message text NOT NULL,
    stack_trace text NULL,
    retry_status varchar(16) NOT NULL DEFAULT 'pending',
    retried_at timestamptz NULL,
    retried_by uuid NULL,
    CONSTRAINT ck_event_handler_failures_retry_status
        CHECK (retry_status IN ('pending', 'retried_ok', 'retried_failed', 'ignored'))
);

CREATE INDEX IF NOT EXISTS ix_event_handler_failures_trace_id ON logs.event_handler_failures (trace_id);
CREATE INDEX IF NOT EXISTS ix_event_handler_failures_occurred_at ON logs.event_handler_failures (occurred_at);

CREATE TABLE IF NOT EXISTS logs.job_runs (
    id uuid NOT NULL PRIMARY KEY,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    job varchar(64) NOT NULL,
    started_at timestamptz NOT NULL,
    finished_at timestamptz NOT NULL,
    status varchar(16) NOT NULL,
    items_processed integer NOT NULL DEFAULT 0,
    items_failed integer NOT NULL DEFAULT 0,
    error text NULL,
    CONSTRAINT ck_job_runs_status CHECK (status IN ('ok', 'partial', 'failed'))
);

CREATE INDEX IF NOT EXISTS ix_job_runs_job ON logs.job_runs (job, occurred_at);

CREATE TABLE IF NOT EXISTS logs.heartbeats (
    process varchar(64) NOT NULL PRIMARY KEY,
    last_beat_at timestamptz NOT NULL
);

-- Particiones mensuales de `errors`: el mes en curso y los 12 siguientes (se nombran errors_YYYYMM).
-- La purga diaria crea las que falten y suelta las de más de un año.
DO $$
DECLARE
    first_month date := date_trunc('month', now() AT TIME ZONE 'UTC')::date;
    m date;
BEGIN
    FOR i IN 0..12 LOOP
        m := (first_month + make_interval(months => i))::date;
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS logs.%I PARTITION OF logs.errors FOR VALUES FROM (%L) TO (%L)',
            'errors_' || to_char(m, 'YYYYMM'),
            m::timestamp AT TIME ZONE 'UTC',
            (m + interval '1 month')::timestamp AT TIME ZONE 'UTC');
    END LOOP;
END $$;
