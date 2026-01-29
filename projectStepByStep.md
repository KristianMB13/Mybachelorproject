# projectStepByStep

This document is a detailed, step-by-step explanation of the entire dashboardAIagentDemo project. It is meant to be a full rebuild guide: if you follow it from top to bottom, you can reproduce the system, understand every folder, and explain why each component exists.

---

## 0) What this project is (short)

Goal: a local demo of "agentic observability" for maritime telemetry.

- Synthetic vessel telemetry is generated
- Data is stored in TimescaleDB (Postgres + Timescale)
- Grafana visualizes metrics and event logs
- A C# agent service analyzes events by querying the DB and calling a local LLM (Ollama)
- The agent stores AI analysis results back in the database
- Grafana shows the analysis in an AI Insights panel
- MCP concepts are represented through agent endpoints

This is a local Docker Compose demo suitable for a bachelor project status demo.

---

## 1) Project layout (root folders and files)

The repo structure (high level):

- docker-compose.yml
- .env.example
- .gitignore
- README.md
- SummaryForAI.md
- projectStepByStep.md   <- this file
- db/
  - init/001_init.sql
- grafana/
  - dashboards/maritime_vessel_overview_v3.json
  - provisioning/datasources/datasource.yml
  - provisioning/dashboards/dashboard.yml
- agent-dotnet/
  - Agent.csproj
  - Program.cs
  - Dockerfile
- generator-dotnet/
  - Generator.csproj
  - Program.cs
  - Dockerfile
- mcp/
  - README.md (notes about MCP concept and endpoints)

Each folder is explained later in detail.

---

## 2) Step-by-step build overview

This is the actual sequence we followed (and you can follow again):

1) Decide on architecture and containers
2) Create database schema
3) Create telemetry generator (C#)
4) Create agent service (C#)
5) Configure Ollama (local LLM)
6) Provision Grafana datasource + dashboard
7) Wire all services with docker-compose
8) Validate data in Grafana
9) Debug issues (panel config, time filtering)
10) Improve Analyze UX (HTML view + caching)
11) Update documentation

---

## 3) Docker Compose - why and how

File: docker-compose.yml

Purpose:
- Make the system runnable with a single command
- Spin up TimescaleDB, Grafana, Ollama, and the two C# services

Important configuration:

TimescaleDB service:
- Image: timescale/timescaledb:latest-pg14
- Environment:
  - POSTGRES_USER=demo
  - POSTGRES_PASSWORD=demo_password
  - POSTGRES_DB=maritime
- Volume for persistent DB storage
- Mounts ./db/init so the schema runs on first startup

Grafana service:
- Image: grafana/grafana:10.4.3 (pinned for stable JSON compatibility)
- Port 3000
- Provisioned with datasource + dashboard
- Volume mount for dashboards and provisioning config

Agent service:
- Build from ./agent-dotnet
- Port 8000
- Env vars for DB and Ollama

Generator service:
- Build from ./generator-dotnet
- No public port
- Env vars for DB
- Env var AGENT_URL used to auto-trigger analysis

Ollama service:
- Image: ollama/ollama:latest
- Port 11434
- Volume for model cache

---

## 4) Database schema

File: db/init/001_init.sql

Purpose:
- Create Timescale extension
- Create tables for telemetry, events, and AI analyses
- Convert telemetry to a hypertable

Tables:

telemetry (hypertable):
- vessel_id (text)
- ts (timestamp)
- engine_rpm (double)
- engine_temp (double)
- oil_pressure (double)
- fuel_pressure (double)
- coolant_temp (double)
- data_quality_score (double)

events:
- event_id (uuid)
- ts (timestamp)
- vessel_id (text)
- sensor_id (text)
- severity (INFO/WARNING/CRITICAL)
- event_type (overtemp, low_oil_pressure, rpm_anomaly)
- description (text)
- metrics_snapshot (json)

ai_analyses:
- id (uuid)
- created_at (timestamp)
- event_id (uuid)
- vessel_id (text)
- ai_summary (json)
- rag_sources (text[])

Why this design:
- telemetry is time-series -> hypertable
- events are discrete anomalies tied to telemetry
- ai_analyses stores JSON so the LLM response is preserved and queryable

---

## 5) Telemetry generator (C#)

Folder: generator-dotnet/

Files:
- Generator.csproj (net8.0)
- Program.cs
- Dockerfile

What it does:
- Maintains in-memory metrics for each vessel
- Every loop:
  - Slightly adjusts values within normal ranges
  - Occasionally creates anomalies
  - Inserts telemetry into the DB
  - Inserts event row if anomaly happens
  - If event happens, auto-calls the agent /analyze

Key details from Program.cs:

- Two vessels: vessel_001 and vessel_002
- Sleep between inserts controlled by SLEEP_SECONDS (default 5)
- Random anomalies (about 6% chance per loop):
  - overtemp: engine_temp spikes
  - low_oil_pressure: oil_pressure drops
  - rpm_anomaly: RPM surge
- Scheduled demo anomaly:
  - For vessel_001, every ~10 minutes (loopCounter % 120 with 5s loop)
  - Ensures visible events during demos

Why these choices:
- Keeps the system lively with regular events
- Makes demo reliable: you can always show an anomaly soon

Auto-analyze:
- generator triggers analysis via HTTP call to agent
- This means AI Insights can update without manual clicking

---

## 6) Agent service (C#)

Folder: agent-dotnet/

Files:
- Agent.csproj (net8.0)
- Program.cs
- Dockerfile

Purpose:
- Expose endpoints for events and AI analysis
- Query TimescaleDB for context window around an event
- Call Ollama to generate JSON analysis
- Store analysis in ai_analyses
- Provide MCP-style tool endpoints

Main endpoints:
- GET /health
- GET /events/latest?vessel_id=...
- POST /analyze { event_id }
- GET /analyze?event_id=...
- GET /analyze/latest?vessel_id=...
- GET /mcp/tools
- POST /mcp/call

Analysis flow:
1) Validate event_id
2) Query event row
3) Compute window (event time - 30 min to +5 min)
4) Query telemetry stats in that window
5) Build a strict prompt that demands JSON output
6) Call Ollama /api/generate
7) Parse JSON safely (fallback if LLM returns weird output)
8) Apply guardrails (data quality, sample count)
9) Store analysis JSON in ai_analyses
10) Return result

Why guardrails:
- LLMs can hallucinate. If data quality is low or samples missing, the response must say so.
- Confidence is lowered if data or LLM call is weak.

---

## 7) HTML Analyze view (user-friendly)

Problem:
- The Analyze link initially opened raw JSON in the browser.
- Users wanted a readable, styled page.

Solution:
- /analyze supports format=html
- It returns an HTML page with summary, causes, actions, confidence, etc.

Caching:
- If an analysis already exists in ai_analyses, the HTML view uses it instead of calling the LLM again.
- This prevents long waits or blank pages.
- You can force a re-run with: &force=true

Example:
- http://localhost:8000/analyze?event_id=<id>&format=html
- http://localhost:8000/analyze?event_id=<id>&format=html&force=true

---

## 8) Grafana provisioning

Folders:
- grafana/provisioning/datasources/datasource.yml
- grafana/provisioning/dashboards/dashboard.yml

Datasource provisioning:
- Creates a Postgres datasource with uid "timescaledb"
- Connects to TimescaleDB using demo credentials

Dashboard provisioning:
- Points to the dashboard JSON file in grafana/dashboards
- Auto-loads it on Grafana startup

Why provisioning:
- Ensures repeatable setup without manual steps
- Makes demo setup fast for supervisors

---

## 9) Grafana dashboard design

File: grafana/dashboards/maritime_vessel_overview_v3.json

Panels:
- Stat cards (Engine Temp, RPM, Oil Pressure, Coolant Temp)
- Time series (Engine Temp + RPM)
- AI Insights (latest 10)
- Event Log (last 24h)

Event Log:
- Query filtered to last 24h
- Includes an "Analyze" link column
- Link opens /analyze?format=html

AI Insights:
- Shows latest 10 analyses for the vessel from last 24h
- Includes event_ts and created_at

Important fixes made:
- Grafana error "byValue not found" fixed by using palette-classic-by-name
- Event Log panel forced to 24h via panel time override
- Event Log query uses __timeFilter(ts) and ts AS time

---

## 10) MCP concept (minimal)

There is no full MCP plugin yet, but the agent exposes MCP-style endpoints:

- GET /mcp/tools
  - Returns tool definitions

- POST /mcp/call
  - Supports:
    - query_recent_metrics
    - get_event_context
    - explain_event

This is enough to demonstrate the MCP concept in the architecture.

---

## 11) Running the system

Commands:

1) Start everything:

```
docker compose up --build
```

2) Pull LLM model (once):

```
docker compose exec ollama ollama pull llama3:8b
```

3) Open Grafana:
- http://localhost:3000
- admin / admin

4) Open dashboard:
- Maritime Vessel Overview v3

---

## 12) Demo flow (what to show)

1) Show the stat cards updating
2) Show time series graph with spikes
3) Show Event Log (last 24h) with events
4) Click Analyze -> HTML analysis page
5) Show AI Insights updating

---

## 13) Known troubleshooting history

Issue: Event Log empty
- Root cause: invalid Grafana color mode (byValue)
- Fix: palette-classic-by-name

Issue: Event Log empty even with data
- Root cause: time filter mismatch
- Fix: panel time override + __timeFilter(ts)

Issue: Analyze link opened raw JSON
- Fix: HTML response + cache

Issue: HTML page stuck loading
- Root cause: waiting for slow LLM call
- Fix: cache AI results and return immediately

---

## 14) Why C# instead of Python

- Project goal shifted to .NET C# to match supervisor requirements
- Python agent/generator removed
- C# versions created with the same behavior
- Docker + .NET 8 ensures consistent runtime

---

## 15) Next steps (optional improvements)

- Add RAG retrieval for domain docs
- Add per-vessel context and technical manuals
- Add alerting rules in Grafana
- Add a proper MCP plugin integration
- Improve AI analysis prompts

---

## 16) File-by-file summary (quick index)

- docker-compose.yml
  - Defines all services and networks

- db/init/001_init.sql
  - Creates Timescale extension and schema

- generator-dotnet/Program.cs
  - Inserts telemetry + events + auto-analyze

- agent-dotnet/Program.cs
  - Exposes API endpoints, calls Ollama, stores analyses

- grafana/dashboards/maritime_vessel_overview_v3.json
  - Dashboard layout and queries

- grafana/provisioning/datasources/datasource.yml
  - Grafana datasource

- grafana/provisioning/dashboards/dashboard.yml
  - Dashboard provisioning

---

## 17) Rebuild instructions (clean run)

If you want to rebuild the project from scratch on a new machine:

1) Clone repo
2) Install Docker Desktop
3) Run docker compose up --build
4) Pull LLM model
5) Open Grafana and verify data
6) Click Analyze to see HTML

---

## 18) Final notes

This project is a working minimal pilot. It is intentionally simple but fully end-to-end. The main goal is to show how telemetry data can be enhanced with AI explanations through a modern observability stack.
