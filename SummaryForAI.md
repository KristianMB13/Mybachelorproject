# SummaryForAI

This document summarizes the current state of the **dashboardAIagentDemo** project, its architecture, what has been implemented, what is running, and what remains to be done. It is intended to help another AI (or a new teammate) quickly understand the full context and current setup.

---

## Project Goal (Short)
A minimal, working “agentic observability” demo for maritime telemetry:

- Synthetic vessel telemetry ? TimescaleDB
- Grafana dashboards visualize metrics and events
- An AI agent analyzes events by querying data and calling an LLM (Ollama)
- AI analysis is stored and shown in Grafana
- MCP concepts are represented via agent endpoints

This is a **local Docker Compose** demo intended for a bachelor project collaboration with Knowit, representing a realistic telemetry context (Telenor Maritime / Color Line style data), but without real vessel data.

---

## Current Architecture

**Data flow:**

1) **Telemetry generator** writes synthetic data into TimescaleDB (Postgres + Timescale)
2) **Event records** are generated alongside telemetry based on anomaly rules
3) **Grafana** reads telemetry + events from TimescaleDB and shows them
4) **Agent service** queries telemetry around an event and builds an LLM prompt
5) **LLM (Ollama)** returns a JSON explanation
6) Agent stores explanation in `ai_analyses`
7) Grafana panel shows latest AI insight

**Mermaid version (simplified):**

```
flowchart LR
  gen[Telemetry Generator] -->|writes| db[(TimescaleDB)]
  db --> grafana[Grafana]
  db --> agent[Agent Service]
  grafana -->|Analyze link| agent
  agent -->|LLM prompt| ollama[Ollama]
  agent -->|analysis| db
  agent --> grafana
```

---

## Services (Docker Compose)

**TimescaleDB**
- Image: `timescale/timescaledb:latest-pg14`
- Ports: `5432`
- DB: `maritime`
- User: `demo`
- Password: `demo_password`

**Grafana**
- Image: `grafana/grafana:10.4.3`
- Port: `3000`
- Admin password: `admin` (can be changed via `.env`)
- Provisioned datasource + dashboard

**Agent Service (C#)**
- .NET 8 Web API
- Runs on port `8000`
- Implements event analysis and MCP endpoints

**Telemetry Generator (C#)**
- .NET 8 console service
- Background loop inserting synthetic data + events

**Ollama (LLM)**
- Containerized via docker-compose
- Port: `11434`
- Model: `llama3:8b` (configured via env)
- Model pull was attempted but may not be complete yet

---

## Database Schema (TimescaleDB)

**telemetry** (hypertable)
- `vessel_id` (text)
- `ts` (timestamp)
- `engine_rpm`, `engine_temp`, `oil_pressure`, `fuel_pressure`, `coolant_temp`
- `data_quality_score` (0–1)

**events**
- `event_id` (uuid)
- `ts`
- `vessel_id`
- `sensor_id`
- `severity` (INFO / WARNING / CRITICAL)
- `event_type` (overtemp, low_oil_pressure, rpm_anomaly, missing_data, bad_timestamp, bad_data_quality)
- `description`
- `metrics_snapshot` (json)

**ai_analyses**
- `id` (uuid)
- `created_at`
- `event_id`
- `vessel_id`
- `ai_summary` (json)
- `rag_sources` (text[])

Schema file: `db/init/001_init.sql`

---

## Fake Data / Synthetic Generator (Important)

The project **generates its own synthetic maritime telemetry data**.

Generator behavior:

- Two vessels: `vessel_001`, `vessel_002`
- Inserts telemetry every few seconds
- Simulated ranges:
  - RPM around 70–140
  - Temp around 65–95
  - Pressure ranges around realistic-ish values
  - `data_quality_score` usually 0.92–0.99
- Occasionally (8% chance) generates anomalies:
  - `overtemp` (temp spike)
  - `low_oil_pressure`
  - `rpm_anomaly`
  - `bad_timestamp` (future timestamp)
  - `missing_data` (event but skip telemetry insert)
  - `bad_data_quality` (low score)

These anomalies are written to the `events` table. The telemetry and events are used by Grafana dashboards and the AI agent.

**Generator source (C#)**:
- `generator-dotnet/Program.cs`

---

## Agent Service (C#)

The agent mirrors the original Python FastAPI service but is now fully in C# (.NET 8).

Endpoints:
- `GET /health`
- `GET /events/latest?vessel_id=...`
- `POST /analyze { event_id }`
- `GET /analyze?event_id=...`
- `GET /analyze/latest?vessel_id=...`
- `GET /mcp/tools`
- `POST /mcp/call`

Behavior:
- Queries `events` to find the event
- Builds a context window (event timestamp ± 30–35 minutes)
- Queries telemetry stats in that window
- Calls Ollama to generate JSON analysis
- Applies guardrails:
  - If no samples ? lower confidence
  - If avg_data_quality < 0.6 ? mention low data quality
  - If LLM fails ? fallback with low confidence
- Stores analysis into `ai_analyses`

Agent source (C#):
- `agent-dotnet/Program.cs`

---

## MCP (Model Context Protocol) Handling

MCP is represented by the agent itself, which exposes tool-like endpoints:

- `GET /mcp/tools`
- `POST /mcp/call`

There is no separate MCP proxy service anymore (original Python stub removed). This is still “MCP-style” rather than a full MCP plugin integration.

---

## Grafana Dashboard

Dashboard: **Maritime Vessel Overview v3**

Panels:
- Stat cards: Engine Temp, RPM, Oil Pressure, Coolant Temp
- Time series: Engine Temp and RPM
- Event Log table (24h)
- AI Insight panel (latest analysis)

Event Log includes an **Analyze** link that calls:

```
http://localhost:8000/analyze?event_id=<event_id>
```

The analysis is stored in the database and the AI Insight panel shows the latest record for the vessel.

Dashboard file:
- `grafana/dashboards/maritime_vessel_overview_v3.json`

Provisioning:
- `grafana/provisioning/datasources/datasource.yml`
- `grafana/provisioning/dashboards/dashboard.yml`

Grafana is pinned to **version 10.4.3** for stability.

---

## Migration from Python to C#

Originally, both agent + generator were Python (FastAPI + psycopg2). Those folders were removed, and C# equivalents replaced them:

- Python removed:
  - `agent/`
  - `generator/`
- New services:
  - `agent-dotnet/`
  - `generator-dotnet/`

This keeps the repo “C# only” for runtime services.

---

## Ollama Status

- Ollama is now included in docker-compose.
- Agent points to `http://ollama:11434`.
- Model pull command was initiated but **was interrupted**. It may not be fully downloaded yet.

Once pulled, the agent will produce real AI output.

Suggested command:

```
docker compose exec ollama ollama pull llama3:8b
```

---

## Files of Interest (Quick Index)

- `docker-compose.yml`
- `db/init/001_init.sql`
- `agent-dotnet/Program.cs`
- `generator-dotnet/Program.cs`
- `grafana/dashboards/maritime_vessel_overview_v3.json`
- `grafana/provisioning/datasources/datasource.yml`
- `grafana/provisioning/dashboards/dashboard.yml`

---

## Current Runtime Status (as last checked)

- Agent (C#) running on port 8000
- Generator (C#) running and inserting data
- Grafana running on port 3000
- TimescaleDB running on port 5432
- Ollama running on port 11434 but model pull may be incomplete

---

## Future Work (Planned)

- Fully integrate Ollama model (confirm model download)
- Add RAG system and domain docs
- Improve anomaly rules and explanation quality
- Potential MCP plugin integration in Grafana
- Realistic or semi-realistic dataset ingestion

---

## Summary Statement

This demo is fully working with synthetic telemetry and events, dashboards in Grafana, and a C# agent that can call Ollama to generate explanations. All runtime services are now in C#, and the demo represents the core “agentic observability” architecture required for the bachelor project. The only missing step for AI responses is to complete the Ollama model download and run analysis calls.
