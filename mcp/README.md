# MCP endpoints

The agent service now exposes MCP-style endpoints directly:

- `GET /mcp/tools`
- `POST /mcp/call`

This keeps the demo fully in C# without a separate proxy service.
