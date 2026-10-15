-- LG4: panel de errores del SuperAdmin (docs/GSM-logs.md §7).
-- Estado y notas de los grupos de fallas. Las cifras (cantidad, gyms, primera y última vez) se calculan de las
-- tablas de logs; aquí solo vive lo que decide el SuperAdmin. Se conserva aunque las ocurrencias se purguen (§9).
CREATE TABLE IF NOT EXISTS logs.error_groups (
    kind varchar(24) NOT NULL,
    fingerprint varchar(700) NOT NULL,
    title varchar(512) NOT NULL,
    first_seen_at timestamptz NOT NULL,
    status varchar(16) NOT NULL DEFAULT 'new',
    notes text NULL,
    status_changed_at timestamptz NOT NULL,
    status_changed_by uuid NULL,
    CONSTRAINT pk_error_groups PRIMARY KEY (kind, fingerprint),
    CONSTRAINT ck_error_groups_kind CHECK (kind IN ('error', 'event_failure', 'external', 'client')),
    CONSTRAINT ck_error_groups_status CHECK (status IN ('new', 'investigating', 'resolved', 'ignored'))
);

-- La búsqueda por código corto es por prefijo (LIKE 'abc%'): con un orden de texto que no sea "C" el índice normal
-- no sirve, así que cada tabla con trace_id lleva además uno con varchar_pattern_ops.
CREATE INDEX IF NOT EXISTS ix_errors_trace_id_prefix ON logs.errors (trace_id varchar_pattern_ops);
CREATE INDEX IF NOT EXISTS ix_event_handler_failures_trace_id_prefix ON logs.event_handler_failures (trace_id varchar_pattern_ops);
CREATE INDEX IF NOT EXISTS ix_business_rejections_trace_id_prefix ON logs.business_rejections (trace_id varchar_pattern_ops);
CREATE INDEX IF NOT EXISTS ix_security_events_trace_id_prefix ON logs.security_events (trace_id varchar_pattern_ops);
CREATE INDEX IF NOT EXISTS ix_slow_requests_trace_id_prefix ON logs.slow_requests (trace_id varchar_pattern_ops);
CREATE INDEX IF NOT EXISTS ix_external_failures_trace_id_prefix ON logs.external_failures (trace_id varchar_pattern_ops);
CREATE INDEX IF NOT EXISTS ix_client_errors_trace_id_prefix ON logs.client_errors (trace_id varchar_pattern_ops);

-- Búsqueda de grupos de eventos por evento + manejador.
CREATE INDEX IF NOT EXISTS ix_event_handler_failures_event_handler
    ON logs.event_handler_failures (event_type, handler_type, retry_status);
