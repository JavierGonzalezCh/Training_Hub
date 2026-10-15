-- LG2: rechazos de negocio, eventos de seguridad y peticiones lentas (docs/GSM-logs.md §4.2, §4.7, §4.8).
-- Las tres van particionadas por mes sobre `occurred_at` (la PK incluye la columna de partición), igual que
-- `logs.errors`. Las particiones se nombran <tabla>_YYYYMM; la purga diaria crea las que falten y suelta las de
-- más de un año (LogMaintenance).
CREATE TABLE IF NOT EXISTS logs.business_rejections (
    id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    method varchar(16) NULL,
    endpoint varchar(512) NULL,
    status_code integer NOT NULL,
    exception_type varchar(256) NOT NULL,
    title varchar(512) NULL,
    detail text NULL,
    field_errors text NULL,
    CONSTRAINT pk_business_rejections PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX IF NOT EXISTS ix_business_rejections_trace_id ON logs.business_rejections (trace_id);
CREATE INDEX IF NOT EXISTS ix_business_rejections_endpoint ON logs.business_rejections (endpoint, status_code, occurred_at);
CREATE INDEX IF NOT EXISTS ix_business_rejections_gym_id ON logs.business_rejections (gym_id, occurred_at);

CREATE TABLE IF NOT EXISTS logs.security_events (
    id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    event_type varchar(64) NOT NULL,
    ip varchar(64) NULL,
    user_agent varchar(512) NULL,
    detail text NULL,
    CONSTRAINT pk_security_events PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX IF NOT EXISTS ix_security_events_trace_id ON logs.security_events (trace_id);
CREATE INDEX IF NOT EXISTS ix_security_events_event_type ON logs.security_events (event_type, occurred_at);
CREATE INDEX IF NOT EXISTS ix_security_events_ip ON logs.security_events (ip, occurred_at);

CREATE TABLE IF NOT EXISTS logs.slow_requests (
    id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    trace_id varchar(64) NULL,
    gym_id uuid NULL,
    user_id uuid NULL,
    role varchar(32) NULL,
    environment varchar(32) NULL,
    app_version varchar(64) NULL,
    method varchar(16) NULL,
    endpoint varchar(512) NULL,
    duration_ms integer NOT NULL,
    status_code integer NOT NULL,
    CONSTRAINT pk_slow_requests PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);

CREATE INDEX IF NOT EXISTS ix_slow_requests_trace_id ON logs.slow_requests (trace_id);
CREATE INDEX IF NOT EXISTS ix_slow_requests_endpoint ON logs.slow_requests (endpoint, occurred_at);

-- Particiones mensuales: el mes en curso y los 12 siguientes.
DO $$
DECLARE
    first_month date := date_trunc('month', now() AT TIME ZONE 'UTC')::date;
    m date;
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['business_rejections', 'security_events', 'slow_requests'] LOOP
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
