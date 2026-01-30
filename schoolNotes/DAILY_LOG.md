# Daily Development Log

## Bachelor Thesis Project: Maritime Decision-Support System with Agentic Observability
**Team:** Kristian Bergedalen, Nidal Alendar, Jonas Pettersen, Onu Pheeraphan
**Institution:** University of Agder (UiA) in collaboration with Knowit Sørlandet
**Deadline:** Week 20 (May 2026)

---

## Background: The Project Pivot (January 22, 2026)

Four days prior to the first daily log entry, a significant change occurred in the project's direction. During a meeting with Arnt, our main supervisor at Knowit, the entire scope of the project was redefined.

### Original Plan
The initial project was to build a custom dashboard for maritime telemetry data. This would have been a traditional visualization tool built from scratch, without using existing platforms like Grafana.

### The New Direction
Arnt introduced the concept of **"agentic observability"** and fundamentally changed the project's focus. His key message was that "AI is the future" and that simply building dashboards is no longer differentiating — everyone can do that now. Instead, he proposed something more ambitious and valuable for our professional futures: a **decision-support system with integrated AI**.

The new system should:
- Monitor maritime telemetry data through Grafana dashboards
- Detect anomalies and events automatically
- Use AI to **proactively explain** what is happening when events occur
- Tell the crew **what might have gone wrong**
- Suggest **how to fix it**
- Provide **confidence levels** about the AI's analysis

This was a substantial increase in complexity and scope. The team had limited prior experience with AI integration, LLMs, MCP (Model Context Protocol), RAG (Retrieval-Augmented Generation), and many of the technologies Arnt mentioned.

### Immediate Challenges Identified
1. **LLM Costs:** Testing revealed that most high-quality LLMs (like GPT-4, Claude API) require paid API access
2. **Budget Uncertainty:** We have not received confirmation from Arnt about whether there is a project budget for API costs
3. **Test Data Unavailability:** The real test data from Knowit/Geir has not been provided yet
4. **Limited Domain Knowledge:** Creating accurate synthetic maritime data is difficult without understanding the actual data structure

### Temporary Solution
Given the uncertainties, the team decided to proceed with a **free-first approach**:
- Using **Ollama** (free, runs locally) instead of paid LLM APIs
- Creating **synthetic test data** until real data is provided
- Building the **architecture first** so it works before real data arrives

This aligns with Arnt's directive: *"Arkitekturen må fungere FØR disse datasettene tas i bruk"* (The architecture must work BEFORE the test datasets are used).

---

## January 26, 2026 (Monday)

### Office Visit and Data Access Discussion

Today the team visited the Knowit office. Arnt (our main supervisor) was not present — he is rarely at the office. However, we had the opportunity to speak with some of the technical staff that Arnt had recommended we consult for help.

#### Test Data Access Progress

One of the staff members (believed to be Kristoffer or someone working with the maritime data) confirmed that he has access to the test data we need. However, he requires explicit permission from Arnt before he can share it with us.

**Action item for Arnt:**
> "Can Kristoffer give us all the data in excel or json format from Prima From NCL?"

This is the message we need to send to Arnt to get access to the actual test data.

#### Key Information About the Data

The staff member provided valuable insights about the data:

1. **Format:** The data can be provided in **JSON format** (possibly also Excel). Knowing it's JSON helps us prepare the data ingestion pipeline.

2. **Scale:** The vessels generate data from **16,000+ sensors** at any given time. This is a massive amount of telemetry data and has implications for:
   - Database design and performance
   - Dashboard visualization choices
   - Which sensors to focus on for the pilot

3. **Scope Guidance:** Due to the overwhelming number of sensors, the staff member offered to identify **specific data points** for us to focus on. This will help us create a dashboard that demonstrates the concept without trying to handle all 16,000+ sensors.

### Technical Direction Discussion: Python vs C#

On the way home from the office, I (Kristian) discussed the project with my cousin, who works professionally with IT and AI systems.

#### The Problem
I suddenly remembered that Arnt had mentioned he wanted the project to be built in **.NET/C#**. However, we have been developing everything in **Python**.

#### Cousin's Advice
My cousin's perspective was valuable:

- **AI/LLM ecosystems favor Python:** Tools like LangChain, Ollama Python client, ChromaDB, and most AI frameworks are Python-first
- **Claude, Codex, and other AI assistants work best with Python** for AI-related tasks
- **Suggested architecture:** Keep the AI components in Python, but containerize them with Docker and expose a **REST API** that C# applications can consume

This hybrid approach would:
- Allow us to use the best tools for AI (Python)
- Still provide a C# interface if Knowit needs to integrate with .NET systems
- Keep everything portable via Docker containers

**Status:** This is still unclear and needs clarification from Arnt. We don't know how strict the C# requirement is.

### AI Tool Experimentation

The team has been experimenting with different AI coding assistants to explore how they approach building this kind of system:
- **GitHub Copilot**
- **Claude** (Anthropic)
- **Codex/ChatGPT** (OpenAI)

We gave them prompts describing our system requirements to see how they would architect and implement solutions.

#### Notable Finding: Codex's Continuous Data Generator

Codex created something particularly useful: a **continuous fake data generator** that updates the database in real-time. Instead of generating static data once, this generator:
- Runs continuously in the background
- Inserts new telemetry readings every few seconds
- Makes the Grafana dashboard update live, simulating a real operational environment

This is valuable because it makes the demo look like an actual working system with live data streams, rather than a static snapshot.

**Note:** We are still in early exploration stages and will likely not use the AI-generated code directly, but these experiments help us understand different architectural approaches.

### Current Mood and Status

Feeling somewhat **overwhelmed and lost** on several fronts:
- The Python vs C# question is unresolved
- LLM budget is still unknown
- Test data is blocked pending Arnt's approval
- The scope change made the project significantly more complex
- Many new technologies to learn in a short time

However, there is also progress:
- The core architecture (TimescaleDB + Grafana + Ollama + MCP + RAG) is working locally
- We have a functional AI agent that analyzes events
- AI insights now appear in the Grafana dashboard
- The team is learning rapidly

### Open Questions Requiring Answers

1. **For Arnt (URGENT):**
   - Can Kristoffer give us the test data in JSON format from Prima/NCL?
   - Is there a budget for LLM API costs, or should we continue with free alternatives?
   - How strict is the C# requirement? Can we use Python with a C# API layer?

2. **Technical Questions:**
   - What specific sensors/data points should we focus on for the pilot?
   - What format exactly is the JSON data in? (schema, structure)
   - What anomalies/events are most important in maritime operations?

3. **For Ourselves:**
   - Should we switch to or prepare for C# now, or wait for clarification?
   - How do we handle 16k+ sensors in our demo? (sampling, filtering, aggregation?)

---

## Summary of Blockers

| Blocker | Status | Dependency |
|---------|--------|------------|
| Test data access | Waiting | Arnt's approval for Kristoffer |
| LLM budget | Unknown | Arnt's response |
| C# requirement | Unclear | Arnt's clarification |
| Data schema | Unknown | Receiving actual test data |

---

### Development Work (Evening Session with Claude)

After returning from the office, continued development work with Claude AI assistance.

#### 1. Fixed Corrupted Dashboard File

Discovered that `grafana-dashboards/maritime-overview.json` was corrupted — the file contained approximately **52 duplicate copies** of the dashboard JSON concatenated together (660KB instead of ~56KB). This caused JSON parsing errors in VSCode.

**Resolution:** Extracted the correct v4 version (with AI Insights panel) and replaced the corrupted file. The dashboard is now working correctly.

#### 2. Project Health Check

Ran a comprehensive check of all project files:
- All JSON files: Valid
- All Python files: Valid syntax
- All YAML files: Valid
- No other corrupted files found

#### 3. Implemented Continuous Data Generator

Based on inspiration from Codex's approach, implemented a **continuous data generator** that makes the Grafana dashboard update in real-time.

**New file:** `scripts/generate_continuous_data.py`

**How to Use It:**

Start it:
```bash
python scripts/generate_continuous_data.py
```

Stop it: Press `Ctrl+C`

**Options:**
```bash
# Faster updates (every 2 seconds)
python scripts/generate_continuous_data.py --interval 2

# More anomalies (15% chance per cycle)
python scripts/generate_continuous_data.py --anomaly-chance 15

# Don't clear old live data on startup
python scripts/generate_continuous_data.py --no-clear
```

**What It Does:**

| Feature | Behavior |
|---------|----------|
| Interval | Inserts readings for all 9 sensors every 5 seconds (default) |
| Anomaly chance | 8% chance per cycle to trigger an anomaly |
| Anomaly types | overtemp, critical_overtemp, rpm_anomaly, low_oil_pressure, low_fuel_pressure, bad_data_quality, missing_data |
| Anomaly duration | 2-5 minutes per anomaly |
| Events | Automatically creates events in the `events` table when anomalies occur |
| Cleanup | Auto-deletes data older than 24 hours (every 5 min) |
| Vessels | vessel_001 (MS Test Ferry), vessel_002 (MV Cargo One) |

**Sample Output:**
```
======================================================================
Maritime Telemetry Generator - Continuous Mode
======================================================================
Interval: 5s | Anomaly chance: 8% per cycle
Sensors: 9 | Vessels: 2
Press Ctrl+C to stop
======================================================================
[START] Generator running...

[18:30:05] Cycle    1 | Readings: 9 | Active anomalies: 0
[18:30:10] Cycle    2 | Readings: 9 | Active anomalies: 0
[18:30:15] Cycle    3 | Readings: 9 | Active anomalies: 1 | Event: [WARNING] Temperature exceeded...
[18:30:20] Cycle    4 | Readings: 9 | Active anomalies: 1
```

When you run this and open Grafana (http://localhost:3000), you'll see the graphs updating in real-time every few seconds, with occasional anomalies appearing as red spikes or drops — just like a real operational dashboard.

**Note:** Docker needs to be running first (`docker compose up -d`).

#### 4. Analysis and Guidance Received

Claude provided analysis on several topics:

**Python vs C# Resolution:**
- Python is the de facto standard for AI/ML development
- Recommended hybrid approach: Python for AI components + REST API that C# can consume
- This allows using the best tools for AI while still providing C# integration if needed

**Understanding "Prima From NCL":**
- Likely refers to a data platform (Prima) and NCL as a vessel/data source identifier
- The important takeaway: Kristoffer has the data, just needs Arnt's permission

**Handling 16,000+ Sensors:**
- We do NOT need to handle all sensors for the pilot
- Focus on 5-15 representative sensors for one "story" (e.g., engine health)
- Take the staff member's offer to identify specific data points

#### 5. Updated Documentation

- Updated `CLAUDE.md` with new continuous generator command
- Created this daily log to track progress
- Will update `DEVELOPMENT_LOG_SESSION_1.md` with Session 5 details

---

## Summary of Today's Accomplishments

| Task | Status |
|------|--------|
| Office visit and data discussion | Done |
| Fixed corrupted dashboard JSON | Done |
| Project health check | Done |
| Implemented continuous data generator | Done |
| Received technical guidance | Done |
| Updated documentation | Done |

---

## Updated Blockers

| Blocker | Status | Dependency |
|---------|--------|------------|
| Test data access | Waiting | Arnt's approval for Kristoffer |
| LLM budget | Unknown | Arnt's response |
| C# requirement | Unclear (but have workaround) | Arnt's clarification |
| Data schema | Unknown | Receiving actual test data |

---

## Next Steps

1. Send message to Arnt requesting data access and budget clarification
2. Continue developing with Python + Ollama (free stack)
3. ~~Implement continuous data generator for live demo effect~~ **DONE**
4. Prepare architecture documentation (highest priority deliverable)
5. Research hybrid Python/C# approaches via Docker + API
6. Test the continuous generator with Grafana when Docker is running
7. Start writing thesis draft (due end of February)

---

## January 28, 2026 (Tuesday)

### Team Meeting - Thesis Writing Session

Had a productive meeting with the team where we focused heavily on **writing the bachelor thesis**. This was a collaborative effort and we made significant progress.

#### Thesis Progress

- **Current page count:** 25-30 pages
- **Maximum allowed:** 40 pages
- **Status:** Great start! We're at approximately 62-75% of the maximum length

This is encouraging progress. The thesis structure is taking shape and we're ahead of where we expected to be at this point.

#### Upcoming Deliverable: Status Video (Due February 13, 2026)

We were reminded of an important deliverable: **Status 1** - a status video that must be submitted by **Friday, February 13, 2026 at 23:59**.

**Video Requirements (max 3 minutes):**
1. **Progress** - Visual and clear demonstration of where we are
2. **Resource usage** - Person and task allocation. Are we on plan?
3. **Demo** - Show working system if possible
4. **Main message** - Key takeaways so far

**Additional Requirements:**
- **Self-evaluation document** - Each group member writes max 1 page covering:
  - Own role and contribution to the project group
  - Assessment of group collaboration (Is everyone contributing? Conflicts? Other issues?)
- **Collaboration description** - A shared description of how the team has worked together

The video will be uploaded to Canvas and published on the course website so all groups can learn from each other.

**Status:** Started working on the video. Need to plan content and record demo footage.

---

## January 29, 2026 (Wednesday)

### Grafana Development Progress

Over the past day, I made progress on the **Grafana dashboard** development. However, I had to work on my laptop instead of the main development machine, so I used **Codex** (OpenAI's coding assistant) for this work.

#### Work Done with Codex
- Made improvements to the Grafana page/dashboard
- Worked in a separate environment on laptop
- **Pushed the Codex project to GitHub** - Now need to sync/merge with main project

#### Today's Plan
- Pull the Codex project changes from GitHub
- Review and integrate the Grafana improvements into the main project
- Continue refining the dashboard to make it demo-ready
- Ensure everything works together (TimescaleDB + Grafana + continuous data generator)

### Priorities for This Week

| Priority | Task | Deadline |
|----------|------|----------|
| 1 | Finalize Grafana dashboard for demo | Before video recording |
| 2 | Continue thesis writing | Ongoing |
| 3 | Record status video | Feb 13, 2026 |
| 4 | Write self-evaluation (1 page each) | Feb 13, 2026 |

### Updated Blockers

| Blocker | Status | Dependency |
|---------|--------|------------|
| Test data access | Still waiting | Arnt's approval |
| LLM budget | Unknown | Arnt's response |
| Status video | In progress | Need demo footage |
| Codex/main project sync | Today's task | GitHub merge |

---

*Log maintained by: Kristian Bergedalen*
*Last updated: January 29, 2026*
