CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE IF NOT EXISTS telemetry (
  vessel_id text NOT NULL,
  ts timestamptz NOT NULL,
  engine_rpm double precision,
  engine_temp double precision,
  oil_pressure double precision,
  fuel_pressure double precision,
  coolant_temp double precision,
  data_quality_score double precision,
  PRIMARY KEY (vessel_id, ts)
);

SELECT create_hypertable('telemetry', 'ts', if_not_exists => TRUE);

CREATE TABLE IF NOT EXISTS events (
  event_id uuid PRIMARY KEY,
  ts timestamptz NOT NULL,
  vessel_id text NOT NULL,
  sensor_id text NOT NULL,
  severity text NOT NULL,
  event_type text NOT NULL,
  description text NOT NULL,
  metrics_snapshot jsonb
);

CREATE INDEX IF NOT EXISTS events_ts_idx ON events (ts DESC);
CREATE INDEX IF NOT EXISTS events_vessel_ts_idx ON events (vessel_id, ts DESC);

CREATE TABLE IF NOT EXISTS ai_analyses (
  id uuid PRIMARY KEY,
  created_at timestamptz NOT NULL DEFAULT now(),
  event_id uuid NOT NULL,
  vessel_id text NOT NULL,
  ai_summary jsonb NOT NULL,
  rag_sources text[] NOT NULL DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS ai_analyses_created_idx ON ai_analyses (created_at DESC);
CREATE INDEX IF NOT EXISTS ai_analyses_event_idx ON ai_analyses (event_id);
