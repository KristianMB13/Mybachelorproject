## **Project Description – Bachelor Project in Collaboration with Knowit**

### **Project Agreement and Collaboration**

This bachelor project is conducted in collaboration with **Knowit Sørlandet** as an industry-based development project. As part of the collaboration, the student group is given access to Knowit’s offices in Kristiansand, including workspace and access cards, allowing the group to work physically on-site in a manner similar to professional project work within a consultancy environment.

Knowit has intentionally kept the initial project definition open, providing an overall direction rather than a fixed solution or implementation plan. This approach allows the student group to actively participate in shaping the project’s direction, problem area, and technical solution, while receiving guidance and feedback from experienced developers and technical advisors at Knowit.

---

### **Project Background and Context**

The project is rooted in Knowit’s collaboration with **Telenor Maritime** and related maritime stakeholders, including ferry operators such as **Color Line**. These organizations work with large volumes of telemetry data collected from ships and vessels, including real-time and near real-time sensor data describing technical and operational conditions on board.

Examples of such data include engine speed (RPM), temperature, pressure, fuel consumption, timestamps, and operational events. This data is typically stored as time-series data and used for monitoring system health, performance, and safety. While the student group will not work directly on board vessels, the project is based on test datasets that are intended to represent realistic maritime telemetry data. These datasets provide a practical foundation for system design, experimentation, and evaluation.

In addition, the project context is informed through dialogue and planned interviews with personnel familiar with maritime operations and existing monitoring solutions. This helps ensure that the system design and assumptions are grounded in real operational needs and constraints.

---

### **Project Description and Objectives**

The overall goal of the project is to explore how maritime telemetry data can be used more effectively to support operational decision-making. Traditionally, such data has been presented through dashboards that visualize raw sensor values, trends, and alarms. While dashboards provide access to information, they often place a heavy cognitive burden on users, who must interpret complex signals, assess data quality, and decide on appropriate actions under time pressure.

In this project, the group will create a dashboard that is used as a visualization layer and remains an important part of the solution. However, the primary focus has shifted toward integrating **AI-based agents** and modern observability concepts on top of traditional dashboards. Rather than building a dashboard as the sole end product, the project investigates how dashboards can be enhanced through AI-driven interpretation, explanation, and decision support.

A key concept explored in the project is **agentic observability**, where AI agents assist users by:

* interpreting telemetry data and detected events,

* providing explanations for why certain situations occur,

* highlighting data quality issues and uncertainty,

* and suggesting possible actions based on available information.

To enable this interaction between AI models and operational data, the project introduces the **Model Context Protocol (MCP)** as a central architectural component. MCP is used to connect large language models (LLMs) with data sources and tools, ensuring that AI-generated responses are grounded in actual data rather than unsupported assumptions.

In addition to time-series data stored in a database such as **PostgreSQL with TimescaleDB**, the project may incorporate **Retrieval-Augmented Generation (RAG)** to provide contextual knowledge from documents or system descriptions. This allows AI agents to combine live data with domain knowledge when generating explanations.

---

### **Project Execution and Technical Focus**

The project emphasizes early architectural design and the creation of a small but functional pilot solution. The primary technical objective is to establish a working architecture that integrates:

* a time-series database for telemetry data,

* Grafana for visualization,

* an MCP server to mediate between data and AI agents,

* one or more AI agents powered by a selected LLM,

* and optional knowledge sources for contextual support.

The pilot solution is developed using synthetic or test datasets provided by Knowit, allowing experimentation without access to sensitive production data. The architecture is designed to be extendable, enabling further development by Knowit or other teams beyond the scope of the bachelor project.

---

### **Project Scope and Learning Objectives**

The project is exploratory and developmental in nature. A lot of the focus is on:

* architectural design,

* integration of modern technologies,

* and evaluation of how AI agents can enhance observability and decision support.

From an educational perspective, the project requires the group to engage with new domains and technologies, including maritime telemetry, industrial data concepts, time-series databases, AI agents, and cloud-based infrastructure. This learning process is considered a central part of the project’s value, both academically and professionally.

---

### **Summary**

In summary, this bachelor project combines practical software development with exploratory research into modern observability and AI-based decision support. Through close collaboration with Knowit Sørlandet and exposure to real-world maritime contexts, the project aims to demonstrate how traditional dashboards can be extended with AI agents to better support users in understanding complex systems. The result is a functional pilot solution and a well-documented report that reflects both technical decisions and lessons learned when working at the intersection of data, AI, and maritime operations.

