-- LG3: fallas de servicios externos y errores del cliente (docs/GSM-logs.md §4.5, §4.6).
-- Particionadas por mes sobre `occurred_at` como las demás tablas de volumen; la purga diaria
-- (LogMaintenance) crea las particiones que falten y suelta las de más de un año.
CREATE TABLE IF NOT EXISTS logs.external_failures (
    id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    provider varchar(32) NOT NULL,
    operation varchar(64) NOT NULL,
    status_code integer NULL,
    response_body text NULL,
    duration_ms integer NOT NULL,
    attempt integer NOT NULL,
    CONSTRAINT pk_external_failures PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX IF NOT EXISTS ix_external_failures_trace_id ON logs.external_failures (trace_id);
CREATE INDEX IF NOT EXISTS ix_external_failures_provider ON logs.external_failures (provider, occurred_at);

CREATE TABLE IF NOT EXISTS logs.client_errors (
    id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    platform varchar(32) NOT NULL,
    route varchar(512) NULL,
    exception_type varchar(256) NOT NULL,
    message text NOT NULL,
    stack_trace text NULL,
    device_info varchar(512) NULL,
    CONSTRAINT pk_client_errors PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX IF NOT EXISTS ix_client_errors_trace_id ON logs.client_errors (trace_id);
CREATE INDEX IF NOT EXISTS ix_client_errors_platform ON logs.client_errors (platform, occurred_at);

-- Particiones mensuales: el mes en curso y los 12 siguientes.
DO $$
DECLARE
    first_month date := date_trunc('month', now() AT TIME ZONE 'UTC')::date;
    m date;
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['external_failures', 'client_errors'] LOOP
        FOR i IN 0..12 LOOP
            m := (first_month + make_interval(months => i))::date;
            EXECUTE format(
                'CREATE TABLE IF NOT EXISTS logs.%I PARTITION OF logs.%I FOR VALUES FROM (%L) TO (%L)',
                t || '_' || to_char(m, 'YYYYMM'), t,
                m::timestamp AT TIME ZONE 'UTC',
                (m + interval '1 month')::timestamp AT TIME ZONE 'UTC');
        END LOOP;
    END LOOP;
END $$;
