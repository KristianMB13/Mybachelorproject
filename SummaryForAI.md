# SummaryForAI

This document summarizes the current state of the **dashboardAIagentDemo** project, its architecture, what is implemented, what is running, and important troubleshooting history. It is written for another AI or teammate to take over the project quickly with full context.

---

## Project Goal (Short)
Minimal, working “agentic observability” demo for maritime telemetry:

- Synthetic vessel telemetry -> TimescaleDB (Postgres + Timescale)
- Grafana dashboards visualize metrics and events
- An AI agent analyzes events by querying data and calling a local LLM (Ollama)
- AI analysis is stored and shown in Grafana
- MCP concepts are represented via agent endpoints

This is a local Docker Compose demo for a bachelor project in collaboration with Knowit, representing a realistic maritime telemetry context (Telenor Maritime / Color Line style data) without real vessel data.

---

## Current Architecture (Data Flow)

1) Telemetry generator writes synthetic data into TimescaleDB
2) Events are generated based on anomaly rules
3) Grafana reads telemetry + events
4) Agent service queries telemetry around an event and builds an LLM prompt
5) Ollama returns a JSON explanation
6) Agent stores explanation in `ai_analyses`
7) Grafana shows AI insight + Event Log

Mermaid (simplified):

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
- Port: `5432`
- DB: `maritime`
- User: `demo`
- Password: `demo_password`

**Grafana**
- Image: `grafana/grafana:10.4.3`
- Port: `3000`
- Admin password: `admin` (configurable via `.env`)
- Provisioned datasource + dashboard

**Agent Service (C#)**
- .NET 8 Web API
- Port `8000`
- Implements event analysis + MCP endpoints

**Telemetry Generator (C#)**
- .NET 8 console service
- Inserts telemetry and events in a loop
- Auto-triggers analysis for events

**Ollama (LLM)**
- Containerized via docker-compose
- Port `11434`
- Model: `llama3:8b` (configurable via env)
- Model is pulled and working (analysis returns JSON)

---

## Database Schema (TimescaleDB)

**telemetry** (hypertable)
- `vessel_id` (text)
- `ts` (timestamp)
- `engine_rpm`, `engine_temp`, `oil_pressure`, `fuel_pressure`, `coolant_temp`
- `data_quality_score` (0-1)

**events**
- `event_id` (uuid)
- `ts`
- `vessel_id`
- `sensor_id`
- `severity` (INFO / WARNING / CRITICAL)
- `event_type` (overtemp, low_oil_pressure, rpm_anomaly)
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

The project generates its own synthetic maritime telemetry data.

Generator behavior (current):
- Two vessels: `vessel_001`, `vessel_002`
- Inserts telemetry every `SLEEP_SECONDS` (default 5s)
- Normal ranges (approx):
  - RPM: 70-140
  - Engine temp: 65-95
  - Oil pressure: 25-55
  - Fuel pressure: 35-70
  - Coolant temp: 55-85
  - Data quality: 0.92-0.99
- Random anomalies: 6% chance per cycle
  - `overtemp`
  - `low_oil_pressure`
  - `rpm_anomaly`
- Scheduled demo anomaly every ~10 min for `vessel_001` (loopCounter % 120 with 5s loop)
- When an anomaly happens, data_quality is reduced (<= 0.8)
- Every event auto-triggers the agent analysis (POST `/analyze`)

Generator source (C#):
- `generator-dotnet/Program.cs`

---

## Agent Service (C#)

Endpoints:
- `GET /health`
- `GET /events/latest?vessel_id=...`
- `POST /analyze { event_id }`
- `GET /analyze?event_id=...`
- `GET /analyze/latest?vessel_id=...`
- `GET /mcp/tools`
- `POST /mcp/call`

Behavior:
- Finds an event by ID
- Queries telemetry stats in a window from -30 minutes to +5 minutes around event time
- Builds a strict prompt (JSON-only) for the LLM
- Calls Ollama (`/api/generate`) and parses JSON
- Guardrails:
  - Low sample count -> lower confidence
  - Low avg data_quality -> note in response + lower confidence
  - LLM failures -> fallback summary + lower confidence
- Stores analysis into `ai_analyses`

Agent source (C#):
- `agent-dotnet/Program.cs`

---

## Grafana Dashboard

Dashboard: **Maritime Vessel Overview v3**

Panels:
- Stat cards: Engine Temp, RPM, Oil Pressure, Coolant Temp
- Time series: Engine Temp + RPM (last 6h)
- **AI Insights (latest 10)** table (latest analyses, last 24h)
- **Event Log (last 24h)** table

Event Log behavior:
- The Analyze column links to:
  `http://localhost:8000/analyze?event_id=<event_id>&format=html`
- The link opens a simple HTML view (readable summary). It uses cached analysis if available for fast load and stores any new analysis into `ai_analyses`.
- You can force regeneration with: `&force=true`

Dashboard file:
- `grafana/dashboards/maritime_vessel_overview_v3.json`

Provisioning:
- `grafana/provisioning/datasources/datasource.yml`
- `grafana/provisioning/dashboards/dashboard.yml`

Grafana is pinned to 10.4.3 for stability with the current dashboard JSON.

---

## MCP (Model Context Protocol)

MCP is represented by the agent itself:
- `GET /mcp/tools` lists tools and schemas
- `POST /mcp/call` routes tool calls

No separate MCP proxy is running; it is a lightweight MCP-style interface.

---

## Migration from Python to C#

Originally the agent + generator were Python. They were removed and replaced with C# equivalents:
- Removed Python folders: `agent/`, `generator/`
- Added C# services: `agent-dotnet/`, `generator-dotnet/`

Runtime services are now C# only.

---

## Recent Issues and Fixes (Important Troubleshooting)

1) **Event Log panel empty** even though events existed in DB.
   - Root cause: Grafana panel error in Inspect -> Data.
   - Error: `byValue not found` (invalid color mode for Grafana 10.4.3).
   - Fix: Change color mode to `palette-classic-by-name`.

2) **Event Log still empty after fix**.
   - Root cause: time filter mismatch and missing panel override.
   - Fixes:
     - Event Log panel uses `timeFrom: "24h"` to override dashboard time range.
     - Query uses `__timeFilter(ts)` and aliases `ts AS time`.

3) **AI Insight showing only one row / outdated**.
   - Fix: Query updated to show latest 10 analyses in last 24h.

4) **Timestamp confusion**.
   - `event_ts` = time of event (UTC)
   - `created_at` = time the analysis was created (UTC)
   - Grafana uses browser timezone, so there can be apparent offsets.

---

## Ollama Status

- Ollama is running in docker-compose.
- Model `llama3:8b` is pulled and responding.
- Agent calls Ollama and returns JSON analysis successfully.

Command (if needed):
```
docker compose exec ollama ollama pull llama3:8b
```

---

## Current Runtime Status (as last checked)

- Agent (C#) running on port 8000
- Generator (C#) running and inserting data + events
- Grafana running on port 3000
- TimescaleDB running on port 5432
- Ollama running on port 11434
- AI Insight and Event Log are working in Grafana

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

## How to Run (Short)

```
docker compose up --build
```

Then:
- http://localhost:3000
- Login: admin / admin
- Dashboard: Maritime Vessel Overview v3

---

## Summary Statement

The demo is working end-to-end: synthetic telemetry generates events, Grafana shows metrics and events, the C# agent queries the DB and calls Ollama, AI analyses are stored and visualized. The system is now stable and uses only C# services for runtime logic. The main gotchas were Grafana panel config errors and time filtering, both now fixed.
