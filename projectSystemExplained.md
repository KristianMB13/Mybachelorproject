# Project System Explained

This document explains how every component in the **dashboardAIagentDemo** project works, how they communicate with each other, what technologies are used, and why certain choices were made. It is written to help you understand and explain this system even if you did not write the code yourself.

---

## Table of Contents

1. [Project Overview - The Big Picture](#1-project-overview---the-big-picture)
2. [What Problem Does This Solve?](#2-what-problem-does-this-solve)
3. [The Five Main Components](#3-the-five-main-components)
4. [How Components Talk to Each Other](#4-how-components-talk-to-each-other)
5. [Detailed Component Explanations](#5-detailed-component-explanations)
6. [The Database - TimescaleDB](#6-the-database---timescaledb)
7. [The AI System - Ollama and LLM](#7-the-ai-system---ollama-and-llm)
8. [The Agent Service - The Brain](#8-the-agent-service---the-brain)
9. [The Generator - Fake Data Creator](#9-the-generator---fake-data-creator)
10. [Grafana - The Dashboard](#10-grafana---the-dashboard)
11. [Docker and Containerization](#11-docker-and-containerization)
12. [MCP - Model Context Protocol](#12-mcp---model-context-protocol)
13. [The Complete Data Flow](#13-the-complete-data-flow)
14. [Programming Languages and Why](#14-programming-languages-and-why)
15. [File Structure Explained](#15-file-structure-explained)
16. [Key Concepts Glossary](#16-key-concepts-glossary)
17. [How to Explain This to Someone](#17-how-to-explain-this-to-someone)

---

## 1. Project Overview - The Big Picture

### What is this project?

This project is a **demo system for "agentic observability"** in a maritime context. In simple terms:

- Ships have thousands of sensors measuring things like engine temperature, oil pressure, speed (RPM), etc.
- Traditionally, these readings are shown on dashboards with graphs and numbers
- Operators (humans) must look at these dashboards and figure out what's wrong when something goes bad
- **This project adds AI** to help explain what's happening and suggest what to do

### The Core Idea

Instead of just showing data on a screen, the system:
1. Detects when something unusual happens (an "anomaly" or "event")
2. Automatically asks an AI to analyze the situation
3. The AI looks at the data and explains:
   - What happened
   - Why it might have happened
   - What to do about it
   - How confident it is in its analysis

### Visual Summary

```
[Ship Sensors] --> [Data Generator] --> [Database] --> [Grafana Dashboard]
                                             |
                                             v
                                    [AI Agent] --> [AI Model (Ollama)]
                                             |
                                             v
                                    [AI Explanations shown in Dashboard]
```

---

## 2. What Problem Does This Solve?

### The Traditional Problem

In maritime operations:
- Ships have 16,000+ sensors generating constant data
- Human operators must watch dashboards and interpret complex graphs
- When something goes wrong, operators must:
  - Notice the problem (among thousands of values)
  - Understand what caused it
  - Decide what action to take
  - Do this quickly, often under pressure

This is called **"cognitive burden"** - too much information for humans to process effectively.

### The Solution This Project Demonstrates

**Agentic Observability** means:
- The system actively watches the data (observability)
- An AI agent helps interpret and explain (agentic)
- Humans get explanations and suggestions, not just raw numbers

Think of it like having a smart assistant who watches the ship's data and tells you: "Hey, the engine temperature spiked because the oil pressure dropped. You should check the oil pump. I'm 75% confident about this."

---

## 3. The Five Main Components

The system has five main parts, all running as Docker containers:

| Component | What It Does | Port |
|-----------|--------------|------|
| **TimescaleDB** | Stores all the data (time-series database) | 5432 |
| **Grafana** | Shows the dashboard with graphs | 3000 |
| **Agent** | Analyzes events and talks to the AI | 8000 |
| **Generator** | Creates fake ship data for testing | (none) |
| **Ollama** | Runs the AI model locally | 11434 |

### Simple Analogy

Think of it like a restaurant:
- **TimescaleDB** = The pantry/storage (keeps all ingredients/data)
- **Grafana** = The menu board showing what's available (shows data visually)
- **Agent** = The head chef (decides what to cook/analyze)
- **Generator** = The supplier delivering ingredients (creates test data)
- **Ollama** = A consultant chef who gives advice (the AI that explains things)

---

## 4. How Components Talk to Each Other

### Communication Diagram

```
                    ┌─────────────────────────────────────────────────────┐
                    │                  Docker Network                      │
                    │                                                       │
    ┌───────────┐   │   ┌─────────────┐        ┌─────────────┐            │
    │           │   │   │             │        │             │            │
    │ Generator │───┼──>│ TimescaleDB │<───────│   Grafana   │            │
    │  (C#)     │   │   │  (Database) │        │ (Dashboard) │            │
    │           │   │   │             │        │             │            │
    └─────┬─────┘   │   └──────┬──────┘        └──────┬──────┘            │
          │         │          │                      │                    │
          │         │          │                      │                    │
          │         │          v                      │                    │
          │         │   ┌─────────────┐              │                    │
          │         │   │             │              │                    │
          └─────────┼──>│    Agent    │<─────────────┘                    │
                    │   │    (C#)     │   (Analyze button)                │
                    │   │             │                                    │
                    │   └──────┬──────┘                                    │
                    │          │                                           │
                    │          v                                           │
                    │   ┌─────────────┐                                    │
                    │   │             │                                    │
                    │   │   Ollama    │                                    │
                    │   │   (AI/LLM)  │                                    │
                    │   │             │                                    │
                    │   └─────────────┘                                    │
                    │                                                       │
                    └───────────────────────────────────────────────────────┘
```

### Communication Methods

All components communicate using standard protocols:

1. **HTTP/REST APIs** - The Agent exposes endpoints that other services can call
2. **PostgreSQL Protocol** - Components connect to TimescaleDB like any database
3. **HTTP to Ollama** - The Agent sends text prompts and receives AI responses

### What Each Connection Does

| From | To | What Happens |
|------|-----|--------------|
| Generator → TimescaleDB | Inserts new sensor readings every 5 seconds |
| Generator → Agent | Tells agent "new event occurred, please analyze" |
| Grafana → TimescaleDB | Queries data to show on dashboard |
| Grafana → Agent | User clicks "Analyze" button, agent processes it |
| Agent → TimescaleDB | Reads event and telemetry data for analysis |
| Agent → Ollama | Sends prompt, receives AI explanation |
| Agent → TimescaleDB | Saves AI analysis for future display |

---

## 5. Detailed Component Explanations

Let's dive deep into each component.

---

## 6. The Database - TimescaleDB

### What is TimescaleDB?

TimescaleDB is a **time-series database**. It's built on top of PostgreSQL (a popular database) but optimized for data that comes with timestamps.

### Why TimescaleDB Instead of Regular PostgreSQL?

Regular databases store data in tables. Time-series databases are special because:
- They handle millions of readings with timestamps efficiently
- They can quickly answer questions like "what was the average temperature in the last hour?"
- They automatically manage old data (compress it, delete it, etc.)

Ship sensors generate readings every few seconds. Over a day, that's:
- 1 sensor × 12 readings/minute × 60 minutes × 24 hours = 17,280 readings per sensor
- With 16,000 sensors, that's ~276 million readings per day

TimescaleDB handles this scale efficiently.

### The Database Schema (Tables)

The database has three tables:

#### 1. `telemetry` table (stores sensor readings)
```sql
CREATE TABLE telemetry (
  vessel_id text,           -- Which ship (e.g., "vessel_001")
  ts timestamptz,           -- When the reading was taken
  engine_rpm double,        -- Engine speed (rotations per minute)
  engine_temp double,       -- Engine temperature in Celsius
  oil_pressure double,      -- Oil pressure in PSI
  fuel_pressure double,     -- Fuel pressure in PSI
  coolant_temp double,      -- Coolant temperature
  data_quality_score double -- How reliable is this data (0-1)
);
```

This is a **hypertable** - TimescaleDB's special table type for time-series data.

#### 2. `events` table (stores detected anomalies)
```sql
CREATE TABLE events (
  event_id uuid,            -- Unique identifier
  ts timestamptz,           -- When event occurred
  vessel_id text,           -- Which ship
  sensor_id text,           -- Which sensor triggered it
  severity text,            -- INFO, WARNING, or CRITICAL
  event_type text,          -- overtemp, low_oil_pressure, rpm_anomaly
  description text,         -- Human-readable description
  metrics_snapshot jsonb    -- All sensor values at that moment
);
```

#### 3. `ai_analyses` table (stores AI explanations)
```sql
CREATE TABLE ai_analyses (
  id uuid,                  -- Unique identifier
  created_at timestamptz,   -- When analysis was created
  event_id uuid,            -- Which event this analyzes
  vessel_id text,           -- Which ship
  ai_summary jsonb,         -- The AI's full response (JSON format)
  rag_sources text[]        -- Future: documents used for context
);
```

### Why JSON/JSONB for Some Fields?

`metrics_snapshot` and `ai_summary` use JSONB (JSON Binary) because:
- They store flexible, nested data
- The structure might change over time
- You can query inside JSON in PostgreSQL

Example `ai_summary`:
```json
{
  "summary": "Engine overheated due to suspected oil pump failure",
  "possible_causes": ["Oil pump malfunction", "Blocked oil filter"],
  "recommended_actions": ["Check oil pump", "Inspect oil filter"],
  "confidence": 75,
  "data_quality_notes": "Good data quality in context window"
}
```

---

## 7. The AI System - Ollama and LLM

### What is an LLM?

**LLM** stands for **Large Language Model**. These are AI systems trained on massive amounts of text that can:
- Understand questions in natural language
- Generate human-like responses
- Analyze data and provide explanations

Examples: ChatGPT (OpenAI), Claude (Anthropic), Llama (Meta)

### What is Ollama?

**Ollama** is a tool that lets you run LLMs **locally on your own computer** instead of paying for cloud APIs.

Why use Ollama instead of ChatGPT API?
- **Free** - No API costs (important for students/budget constraints)
- **Private** - Data never leaves your computer
- **No internet required** - Works offline once model is downloaded
- **Good enough** - Llama3 is very capable for this use case

### The Model: Llama3 8B

The project uses `llama3:8b` which means:
- **Llama3** - The third generation of Meta's open-source AI model
- **8B** - 8 billion parameters (the "size" of the model's brain)

The 8B version is a good balance:
- Smart enough to analyze telemetry data
- Small enough to run on regular computers
- Requires about 4.7GB of disk space

### How the Agent Talks to Ollama

The Agent sends HTTP requests to Ollama's API:

```
POST http://ollama:11434/api/generate
{
  "model": "llama3:8b",
  "prompt": "You are an assistant for maritime telemetry analysis...",
  "stream": false
}
```

Ollama responds with:
```json
{
  "response": "{\"summary\": \"Engine temperature spike detected...\", ...}"
}
```

### The Prompt Engineering

The Agent sends a carefully crafted prompt to get structured JSON output:

```
You are an assistant for maritime telemetry analysis.
Use only the data provided below. Do not guess.
Return JSON only with keys: summary, possible_causes, recommended_actions, confidence, data_quality_notes.

Event: overtemp (severity CRITICAL)
Description: Engine temperature spike detected.
Vessel: vessel_001
Timestamp: 2026-01-30T10:45:00Z

Stats:
{
  "sample_count": 360,
  "avg_engine_temp": 87.5,
  "max_engine_temp": 115.2,
  "avg_oil_pressure": 28.3,
  ...
}

Rules:
- If sample_count is 0, say data is missing and lower confidence.
- If avg_data_quality is below 0.6, mention low data quality and lower confidence.
- Keep possible_causes and recommended_actions short.
```

---

## 8. The Agent Service - The Brain

### What Does the Agent Do?

The Agent is the **central coordinator** of the system. It:
1. Receives requests to analyze events
2. Queries the database for relevant data
3. Builds prompts for the AI
4. Sends prompts to Ollama and parses responses
5. Applies guardrails (safety checks)
6. Stores results back in the database
7. Returns results to the user/Grafana

### Technology: C# and .NET 8

The Agent is written in **C#** using **.NET 8** (Microsoft's modern framework).

Why C#?
- The project supervisor (Knowit) requested .NET/C# for compatibility with their systems
- C# is enterprise-grade and well-suited for web APIs
- .NET 8 is the latest version with good performance

### The Agent's API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/health` | GET | Check if agent is running |
| `/events/latest` | GET | Get the most recent event |
| `/analyze` | POST | Analyze a specific event by ID |
| `/analyze` | GET | Analyze event (supports HTML format) |
| `/analyze/latest` | GET | Analyze the most recent event |
| `/mcp/tools` | GET | List available MCP tools |
| `/mcp/call` | POST | Execute an MCP tool |

### The Analysis Flow (Step by Step)

When someone clicks "Analyze" in Grafana or calls the API:

```
1. Agent receives request with event_id
   ↓
2. Check if analysis already cached in database
   → If yes, return cached result (fast!)
   → If no, continue to step 3
   ↓
3. Query the event details from database
   ↓
4. Define a time window: 30 minutes before event to 5 minutes after
   ↓
5. Query telemetry statistics in that window:
   - min/max/avg for each sensor
   - sample count
   - average data quality
   ↓
6. Build prompt with event + stats
   ↓
7. Send prompt to Ollama
   ↓
8. Parse JSON response (with error handling)
   ↓
9. Apply guardrails:
   - If no data samples → lower confidence
   - If low data quality → lower confidence + note
   - If Ollama failed → fallback message + lower confidence
   ↓
10. Store analysis in database
    ↓
11. Return result (JSON or HTML page)
```

### Guardrails Explained

**Guardrails** are safety checks that prevent bad AI responses from being trusted:

```csharp
// If there's no data to analyze
if (sampleCount == 0)
{
    confidence = Math.Min(confidence, 20);  // Max 20% confidence
    notes = "No telemetry samples found in the context window.";
}

// If data quality is poor
if (avgQuality < 0.6)
{
    confidence = Math.Min(confidence, 40);  // Max 40% confidence
    notes += "Data quality is low in the context window.";
}

// If AI call failed
if (llmError != null)
{
    confidence = Math.Min(confidence, 30);  // Max 30% confidence
    notes += "LLM unavailable, using fallback response.";
}
```

This ensures that even if the AI says "I'm 90% confident", the system overrides it to a lower value when the data is unreliable.

### HTML Response Format

When accessing `/analyze?event_id=xxx&format=html`, the Agent returns a styled HTML page instead of raw JSON. This makes it user-friendly when clicking links in Grafana.

---

## 9. The Generator - Fake Data Creator

### Why Fake Data?

The real ship data from Knowit/Telenor Maritime hasn't been provided yet. So the project uses **synthetic (fake) data** that:
- Looks realistic
- Simulates normal operation with occasional anomalies
- Makes the demo work without real data

### Technology: C# Console Application

The Generator is a **C# console app** that runs forever in a loop, inserting data every 5 seconds.

### How It Works

```
1. Initialize: Set up starting values for each vessel
   ↓
2. Every 5 seconds:
   ├── For each vessel (vessel_001, vessel_002):
   │   ├── Slightly adjust sensor values (random walk)
   │   ├── 6% chance: Create an anomaly
   │   ├── Every ~10 minutes: Force a demo anomaly (for vessel_001)
   │   ├── Insert telemetry row into database
   │   └── If anomaly: Insert event + trigger Agent analysis
   ↓
3. Loop forever (until container stops)
```

### Normal Value Ranges

The Generator keeps values within realistic ranges:

| Sensor | Normal Range | Unit |
|--------|--------------|------|
| Engine RPM | 70-140 | rotations/min |
| Engine Temp | 65-95 | Celsius |
| Oil Pressure | 25-55 | PSI |
| Fuel Pressure | 35-70 | PSI |
| Coolant Temp | 55-85 | Celsius |
| Data Quality | 0.92-0.99 | (0-1 scale) |

### Anomaly Types

| Type | What Happens | Severity |
|------|--------------|----------|
| `overtemp` | Engine temp spikes by 15-30°C | WARNING or CRITICAL |
| `low_oil_pressure` | Oil pressure drops by 18-28 PSI | WARNING or CRITICAL |
| `rpm_anomaly` | RPM surges by 40-80 | WARNING |

### Auto-Analysis Feature

When an event is created, the Generator automatically calls the Agent:

```csharp
if (evt != null)
{
    var eventId = await InsertEventAsync(...);
    if (evt.AutoAnalyze)
    {
        await TriggerAnalyzeAsync(agentClient, eventId, logger);
    }
}
```

This means AI insights appear automatically without manual clicking.

---

## 10. Grafana - The Dashboard

### What is Grafana?

**Grafana** is a popular open-source tool for creating dashboards. It can:
- Connect to many types of databases
- Create charts, graphs, and tables
- Refresh automatically
- Set up alerts

### Why Grafana?

- Industry standard for monitoring/observability
- Free and open source
- Easy to connect to TimescaleDB (PostgreSQL compatible)
- Looks professional with minimal effort
- Aligns with the "observability" theme of the project

### The Dashboard Layout

The dashboard "Maritime Vessel Overview v3" has these panels:

```
┌─────────────────────────────────────────────────────────────────┐
│  [Engine Temp]  [RPM]  [Oil Pressure]  [Coolant Temp]          │
│      92.3°C      105     35.2 PSI        68.5°C                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│         Engine Temperature + RPM Over Time (Graph)              │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│                   AI Insights (Latest 10)                       │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Event Time │ Summary │ Confidence │ Created At         │   │
│  │ 10:45:00   │ Engine overheated...  │ 75% │ 10:45:05   │   │
│  │ 10:30:00   │ Oil pressure drop...  │ 80% │ 10:30:03   │   │
│  └─────────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────┤
│                   Event Log (Last 24h)                          │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Time │ Type │ Severity │ Description │ [Analyze]       │   │
│  │ 10:45 │ overtemp │ CRITICAL │ Temp spike │ [Analyze]   │   │
│  │ 10:30 │ low_oil  │ WARNING  │ Pressure drop │ [Analyze]│   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### Key Features

1. **Stat Cards** - Show current values at a glance
2. **Time Series Graph** - Shows trends over time, spikes visible
3. **AI Insights Table** - Shows what the AI has analyzed
4. **Event Log Table** - Lists all anomalies with "Analyze" buttons

### The "Analyze" Button

In the Event Log, each row has a link:
```
http://localhost:8000/analyze?event_id=<uuid>&format=html
```

Clicking it opens a nice HTML page showing the AI's analysis.

### Provisioning (Automatic Setup)

Grafana is **provisioned** automatically via config files:

- `datasource.yml` - Defines the TimescaleDB connection
- `dashboard.yml` - Points to the dashboard JSON file
- `maritime_vessel_overview_v3.json` - The actual dashboard definition

This means when you run `docker compose up`, Grafana is already configured with the dashboard.

---

## 11. Docker and Containerization

### What is Docker?

**Docker** is a tool that packages applications into "containers". Think of containers like shipping containers:
- Everything needed to run the app is inside
- Works the same on any computer
- Isolated from other applications

### Why Docker for This Project?

1. **Reproducibility** - Works the same on any computer
2. **Easy setup** - One command starts everything
3. **Isolation** - Each service runs separately
4. **No installation hassle** - No need to install PostgreSQL, Grafana, etc. manually

### Docker Compose

**Docker Compose** lets you define multiple containers that work together.

The `docker-compose.yml` file defines all 5 services:

```yaml
services:
  timescaledb:     # Database container
  grafana:         # Dashboard container
  agent:           # AI agent container
  generator:       # Data generator container
  ollama:          # LLM container
```

### How Containers Communicate

Docker creates a virtual network where containers can find each other by name:
- The Agent connects to `timescaledb:5432` (not `localhost`)
- The Agent connects to `ollama:11434`
- The Generator connects to `agent:8000`

### Volumes (Data Persistence)

**Volumes** store data that survives container restarts:

```yaml
volumes:
  timescale_data:   # Database files
  grafana_data:     # Grafana settings
  ollama_data:      # Downloaded AI model
```

Without volumes, restarting containers would lose all data.

### Dockerfile (Building Custom Containers)

The Agent and Generator need custom Dockerfiles because they're C# applications:

```dockerfile
# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Agent.csproj ./
RUN dotnet restore Agent.csproj
COPY . ./
RUN dotnet publish Agent.csproj -c Release -o /app/publish

# Stage 2: Run the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8000
EXPOSE 8000
ENTRYPOINT ["dotnet", "Agent.dll"]
```

This is a **multi-stage build**:
1. First stage compiles the C# code
2. Second stage runs the compiled code (smaller image)

---

## 12. MCP - Model Context Protocol

### What is MCP?

**MCP (Model Context Protocol)** is a concept/standard for connecting AI models to external data and tools.

The idea is:
- AI models need context (data) to give useful answers
- Instead of putting everything in the prompt, MCP provides a standard way to:
  - List available tools/functions
  - Let the AI call those tools
  - Feed the results back

### MCP in This Project

This project implements a **simplified MCP-style interface**:

#### `GET /mcp/tools` - List Available Tools

Returns what tools the AI can use:
```json
{
  "tools": [
    {
      "name": "query_recent_metrics",
      "description": "Get stats for recent telemetry for a vessel",
      "input_schema": {
        "type": "object",
        "properties": {
          "vessel_id": { "type": "string" },
          "minutes": { "type": "integer", "default": 30 }
        },
        "required": ["vessel_id"]
      }
    },
    {
      "name": "get_event_context",
      "description": "Fetch an event with surrounding telemetry stats"
    },
    {
      "name": "explain_event",
      "description": "Generate an AI explanation for an event"
    }
  ]
}
```

#### `POST /mcp/call` - Execute a Tool

```json
// Request
{
  "tool": "query_recent_metrics",
  "arguments": {
    "vessel_id": "vessel_001",
    "minutes": 60
  }
}

// Response
{
  "tool": "query_recent_metrics",
  "result": {
    "window_minutes": 60,
    "stats": {
      "sample_count": 720,
      "avg_engine_temp": 82.3,
      ...
    }
  }
}
```

### Why MCP Matters

MCP is important because it represents a **modern approach to AI integration**:
- AI doesn't just answer questions from memory
- AI can query real data to base answers on facts
- This is called "grounding" - the AI's response is grounded in actual data

In the future, MCP could allow:
- AI to query multiple data sources
- AI to trigger actions (not just read data)
- Standard integration with other AI systems

---

## 13. The Complete Data Flow

Let's trace what happens from start to finish:

### Startup Flow

```
1. User runs: docker compose up --build
   ↓
2. Docker starts all containers in dependency order:
   - TimescaleDB starts first (database)
   - Ollama starts (AI model host)
   - Agent starts (waits for DB and Ollama)
   - Generator starts (waits for DB)
   - Grafana starts (waits for DB)
   ↓
3. TimescaleDB runs init script (001_init.sql):
   - Creates tables: telemetry, events, ai_analyses
   - Enables TimescaleDB extension
   ↓
4. Grafana loads provisioned:
   - DataSource (TimescaleDB connection)
   - Dashboard (Maritime Vessel Overview v3)
   ↓
5. Generator connects to database and starts loop
   ↓
6. System is now running!
```

### Normal Operation Flow (Every 5 Seconds)

```
1. Generator loop runs
   ↓
2. For each vessel (001, 002):
   ├── Adjust sensor values randomly
   ├── Check for anomaly (6% chance)
   ├── INSERT INTO telemetry (...)
   └── If anomaly:
       ├── INSERT INTO events (...)
       └── POST to http://agent:8000/analyze
   ↓
3. Agent receives analyze request
   ├── Query event from database
   ├── Query stats for time window
   ├── Build prompt
   ├── POST to http://ollama:11434/api/generate
   ├── Parse AI response
   ├── Apply guardrails
   └── INSERT INTO ai_analyses (...)
   ↓
4. Grafana auto-refreshes (every few seconds)
   ├── Queries telemetry for graphs
   ├── Queries events for Event Log
   └── Queries ai_analyses for AI Insights
   ↓
5. User sees updated dashboard
```

### User Clicks "Analyze" Flow

```
1. User opens Grafana (http://localhost:3000)
   ↓
2. User sees Event Log with events
   ↓
3. User clicks "Analyze" link on an event
   ↓
4. Browser opens: http://localhost:8000/analyze?event_id=xxx&format=html
   ↓
5. Agent receives request
   ├── Check cache (ai_analyses table)
   ├── If cached: return HTML immediately
   ├── If not cached: full analysis flow
   └── Return styled HTML page
   ↓
6. User sees AI analysis in browser:
   - Summary
   - Possible causes
   - Recommended actions
   - Confidence level
   - Data quality notes
```

---

## 14. Programming Languages and Why

### C# (.NET 8) - Agent and Generator

**Why C#?**
- Supervisor requirement (Knowit uses .NET)
- Enterprise-grade, well-tested
- Good performance
- Works well with Docker

**Key Libraries Used:**
- `Npgsql` - PostgreSQL/TimescaleDB driver for .NET
- `System.Text.Json` - JSON parsing
- `System.Net.Http` - HTTP client for calling Ollama

### SQL - Database

**Why SQL/PostgreSQL?**
- Industry standard for databases
- TimescaleDB is built on PostgreSQL
- Powerful query language

### YAML - Configuration

**Why YAML?**
- Docker Compose uses YAML
- Grafana provisioning uses YAML
- Human-readable format

### JSON - Data Exchange

**Why JSON?**
- Standard format for APIs
- Both C# and Grafana understand it
- LLM responses are in JSON

### Markdown - Documentation

**Why Markdown?**
- Easy to write
- Renders nicely on GitHub
- This file is Markdown!

---

## 15. File Structure Explained

```
Mybachelorproject/
├── docker-compose.yml          # Defines all 5 services
├── .env.example                 # Template for environment variables
├── README.md                    # Project overview and quick start
├── SummaryForAI.md             # Context for AI assistants to understand project
├── projectStepByStep.md        # Step-by-step rebuild guide
├── projectSystemExplained.md   # This file!
│
├── db/
│   └── init/
│       └── 001_init.sql        # Database schema (tables)
│
├── agent-dotnet/
│   ├── Program.cs              # Main agent code (~970 lines)
│   ├── Agent.csproj            # .NET project file
│   └── Dockerfile              # How to build agent container
│
├── generator-dotnet/
│   ├── Program.cs              # Main generator code (~330 lines)
│   ├── Generator.csproj        # .NET project file
│   └── Dockerfile              # How to build generator container
│
├── grafana/
│   ├── dashboards/
│   │   └── maritime_vessel_overview_v3.json  # Dashboard definition
│   └── provisioning/
│       ├── dashboards/
│       │   └── dashboard.yml   # Tells Grafana where to find dashboards
│       └── datasources/
│           └── datasource.yml  # TimescaleDB connection config
│
├── mcp/
│   └── README.md               # Notes about MCP endpoints
│
└── schoolNotes/
    ├── Project Description...  # Official project description
    └── DAILY_LOG.md            # Development diary
```

### Key Files Explained

| File | Purpose |
|------|---------|
| `docker-compose.yml` | The "master" file that defines the entire system |
| `agent-dotnet/Program.cs` | All the AI analysis logic |
| `generator-dotnet/Program.cs` | Fake data generation logic |
| `db/init/001_init.sql` | Database structure |
| `grafana/dashboards/*.json` | Dashboard layout and queries |

---

## 16. Key Concepts Glossary

### Technical Terms

| Term | Meaning |
|------|---------|
| **API** | Application Programming Interface - a way for programs to talk to each other |
| **REST** | A style of API using HTTP methods (GET, POST, etc.) |
| **JSON** | JavaScript Object Notation - a data format like `{"key": "value"}` |
| **Container** | A packaged application that runs isolated |
| **Docker** | Software for creating and running containers |
| **Endpoint** | A URL that an API responds to (e.g., `/analyze`) |

### Database Terms

| Term | Meaning |
|------|---------|
| **Time-series data** | Data with timestamps (sensor readings over time) |
| **Hypertable** | TimescaleDB's special table type for time-series |
| **Query** | A request for data from the database |
| **INSERT** | Adding new data to a table |
| **SELECT** | Reading data from a table |

### AI Terms

| Term | Meaning |
|------|---------|
| **LLM** | Large Language Model - AI that understands and generates text |
| **Prompt** | The text you send to an AI to get a response |
| **Inference** | When the AI generates a response (as opposed to training) |
| **Guardrails** | Safety checks on AI output |
| **Hallucination** | When AI makes up false information |

### Project-Specific Terms

| Term | Meaning |
|------|---------|
| **Agentic Observability** | AI agents actively interpreting monitored data |
| **MCP** | Model Context Protocol - connecting AI to data sources |
| **RAG** | Retrieval-Augmented Generation - giving AI access to documents |
| **Telemetry** | Sensor data from remote equipment (ships) |
| **Anomaly** | Something unusual in the data (potential problem) |

---

## 17. How to Explain This to Someone

### The 30-Second Explanation

"We built a demo system that shows how AI can help ship operators. Ships have thousands of sensors, and it's hard for humans to watch everything. Our system detects when something goes wrong, asks an AI to analyze it, and shows the operator what happened, why, and what to do about it."

### The 2-Minute Explanation

"This is a bachelor project about 'agentic observability' for maritime operations.

Ships like ferries have tons of sensors - engine temperature, oil pressure, speed, etc. Normally, operators watch dashboards with graphs and numbers. When something goes wrong, they have to figure out what happened themselves.

Our system adds AI to help. We use:
- **TimescaleDB** to store sensor data
- **Grafana** to show dashboards
- **Ollama** to run an AI locally (free, no API costs)
- A **C# Agent** that connects everything

When the system detects an anomaly (like engine overheating), it automatically asks the AI: 'What happened? Why? What should we do?' The AI looks at the actual data, not just guessing, and gives an explanation with a confidence level.

The whole thing runs in Docker containers so anyone can start it with one command."

### The Technical Presentation Points

1. **Architecture**: 5 Docker containers working together
2. **Data Flow**: Generator → TimescaleDB → Agent → Ollama → Grafana
3. **Key Innovation**: AI-powered analysis grounded in real data
4. **MCP Concept**: Standard interface for AI-data interaction
5. **Guardrails**: Safety checks prevent AI overconfidence
6. **Why Local LLM**: Cost-free, private, works offline

---

## Summary

This project demonstrates a modern approach to monitoring and decision support:

1. **Traditional monitoring** shows data on dashboards
2. **Our approach** adds AI agents that interpret the data
3. **The result** is explanations and recommendations, not just numbers

The system is built with:
- **TimescaleDB** for efficient time-series storage
- **Grafana** for visualization
- **Ollama/Llama3** for free local AI
- **C# Agent** for coordination and API
- **Docker** for easy deployment

Everything works together to show how AI can reduce the cognitive burden on operators and help them make better decisions faster.

---

*This document was created to help understand and explain the dashboardAIagentDemo project for the bachelor thesis at UiA in collaboration with Knowit Srlandet.*
