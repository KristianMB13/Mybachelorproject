using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "INFO";
if (!Enum.TryParse<LogLevel>(logLevel, true, out var parsedLevel))
{
    parsedLevel = LogLevel.Information;
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(parsedLevel);

var app = builder.Build();
var logger = app.Logger;

string DbHost = GetEnv("DB_HOST", "localhost");
int DbPort = int.Parse(GetEnv("DB_PORT", "5432"));
string DbUser = GetEnv("DB_USER", "demo");
string DbPassword = GetEnv("DB_PASSWORD", "demo_password");
string DbName = GetEnv("DB_NAME", "maritime");

string OllamaHost = GetEnv("OLLAMA_HOST", "http://localhost:11434").TrimEnd('/');
string OllamaModel = GetEnv("OLLAMA_MODEL", "llama3:8b");

string ConnString = $"Host={DbHost};Port={DbPort};Username={DbUser};Password={DbPassword};Database={DbName}";

app.MapGet("/health", () => Results.Ok(new { status = "ok", model = OllamaModel }));

app.MapGet("/events/latest", async ([FromQuery] string? vessel_id) =>
{
    await using var conn = new NpgsqlConnection(ConnString);
    await conn.OpenAsync();

    EventRecord? record = await QueryLatestEventAsync(conn, vessel_id);
    if (record == null)
    {
        return Results.NotFound(new { detail = "No events found" });
    }

    return Results.Ok(EventToDictionary(record));
});

app.MapPost("/analyze", async (AnalyzeRequest req) =>
{
    return await AnalyzeEventAsync(req.event_id);
});

app.MapGet("/analyze", async (HttpRequest request, [FromQuery] string event_id, [FromQuery] string? format, [FromQuery] bool? force) =>
{
    bool asHtml = WantsHtml(request, format);
    return await AnalyzeEventAsync(event_id, asHtml, force ?? false);
});

app.MapGet("/analyze/latest", async ([FromQuery] string? vessel_id) =>
{
    await using var conn = new NpgsqlConnection(ConnString);
    await conn.OpenAsync();

    EventRecord? record = await QueryLatestEventAsync(conn, vessel_id);
    if (record == null)
    {
        return Results.NotFound(new { detail = "No events found" });
    }

    return await AnalyzeEventAsync(record.EventId.ToString());
});

app.MapGet("/mcp/tools", () =>
{
    var tools = new object[]
    {
        new
        {
            name = "query_recent_metrics",
            description = "Get stats for recent telemetry for a vessel",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    vessel_id = new { type = "string" },
                    minutes = new { type = "integer", @default = 30 }
                },
                required = new[] { "vessel_id" }
            }
        },
        new
        {
            name = "get_event_context",
            description = "Fetch an event with surrounding telemetry stats",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    event_id = new { type = "string" }
                },
                required = new[] { "event_id" }
            }
        },
        new
        {
            name = "explain_event",
            description = "Generate an AI explanation for an event",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    event_id = new { type = "string" }
                },
                required = new[] { "event_id" }
            }
        }
    };

    return Results.Ok(new { tools });
});

app.MapPost("/mcp/call", async (McpCallRequest req) =>
{
    var args = req.arguments ?? new Dictionary<string, JsonElement>();
    await using var conn = new NpgsqlConnection(ConnString);
    await conn.OpenAsync();

    if (req.tool == "query_recent_metrics")
    {
        if (!args.TryGetValue("vessel_id", out var vesselIdElement) || vesselIdElement.ValueKind != JsonValueKind.String)
        {
            return Results.BadRequest(new { detail = "vessel_id is required" });
        }

        string vesselId = vesselIdElement.GetString() ?? "";
        int minutes = args.TryGetValue("minutes", out var minutesElement) && minutesElement.TryGetInt32(out var parsed)
            ? parsed
            : 30;

        var endTs = DateTime.UtcNow;
        var startTs = endTs.AddMinutes(-minutes);

        var stats = await QueryStatsAsync(conn, vesselId, startTs, endTs);
        var coerced = CoerceStats(stats);

        return Results.Ok(new
        {
            tool = req.tool,
            result = new
            {
                window_minutes = minutes,
                stats = coerced
            }
        });
    }

    if (req.tool == "get_event_context")
    {
        if (!args.TryGetValue("event_id", out var eventIdElement) || eventIdElement.ValueKind != JsonValueKind.String)
        {
            return Results.BadRequest(new { detail = "event_id is required" });
        }

        if (!Guid.TryParse(eventIdElement.GetString(), out var eventId))
        {
            return Results.BadRequest(new { detail = "Invalid event_id" });
        }

        var record = await QueryEventAsync(conn, eventId);
        if (record == null)
        {
            return Results.NotFound(new { detail = "Event not found" });
        }

        var windowEnd = record.Ts.AddMinutes(5);
        var windowStart = record.Ts.AddMinutes(-30);
        var stats = await QueryStatsAsync(conn, record.VesselId, windowStart, windowEnd);
        var coerced = CoerceStats(stats);

        return Results.Ok(new
        {
            tool = req.tool,
            result = new
            {
                @event = EventToDictionary(record),
                window = $"{windowStart:o} to {windowEnd:o}",
                stats = coerced
            }
        });
    }

    if (req.tool == "explain_event")
    {
        if (!args.TryGetValue("event_id", out var eventIdElement) || eventIdElement.ValueKind != JsonValueKind.String)
        {
            return Results.BadRequest(new { detail = "event_id is required" });
        }

        return await AnalyzeEventAsync(eventIdElement.GetString() ?? "");
    }

    return Results.BadRequest(new { detail = "Unknown tool" });
});

app.Run();

string GetEnv(string key, string fallback) =>
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))
        ? fallback
        : Environment.GetEnvironmentVariable(key)!.Trim();

async Task<IResult> AnalyzeEventAsync(string eventId, bool asHtml = false, bool force = false)
{
    if (!Guid.TryParse(eventId, out var eventUuid))
    {
        return Results.BadRequest(new { detail = "Invalid event_id" });
    }

    await using var conn = new NpgsqlConnection(ConnString);
    await conn.OpenAsync();

    var record = await QueryEventAsync(conn, eventUuid);
    if (record == null)
    {
        return Results.NotFound(new { detail = "Event not found" });
    }

    var cached = await QueryLatestAnalysisAsync(conn, record.EventId);
    if (!force && cached != null)
    {
        if (asHtml)
        {
            string html = BuildHtmlResponse(record, cached.Analysis, cached.CreatedAt, true);
            return Results.Content(html, "text/html");
        }

        return Results.Ok(cached.Analysis);
    }

    var windowEnd = record.Ts.AddMinutes(5);
    var windowStart = record.Ts.AddMinutes(-30);
    var stats = await QueryStatsAsync(conn, record.VesselId, windowStart, windowEnd);

    var analysis = await BuildAnalysisAsync(record, stats, windowStart, windowEnd);
    var ragSources = Array.Empty<string>();

    await StoreAnalysisAsync(conn, record, analysis, ragSources);

    if (asHtml)
    {
        string html = BuildHtmlResponse(record, analysis, DateTime.UtcNow, false);
        return Results.Content(html, "text/html");
    }

    return Results.Ok(analysis);
}

async Task<EventRecord?> QueryEventAsync(NpgsqlConnection conn, Guid eventId)
{
    const string sql = @"SELECT event_id, ts, vessel_id, sensor_id, severity, event_type, description, metrics_snapshot
FROM events WHERE event_id = @event_id";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("event_id", eventId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return ReadEvent(reader);
}

async Task<EventRecord?> QueryLatestEventAsync(NpgsqlConnection conn, string? vesselId)
{
    string sql;
    if (!string.IsNullOrWhiteSpace(vesselId))
    {
        sql = @"SELECT event_id, ts, vessel_id, sensor_id, severity, event_type, description, metrics_snapshot
FROM events WHERE vessel_id = @vessel_id ORDER BY ts DESC LIMIT 1";
    }
    else
    {
        sql = @"SELECT event_id, ts, vessel_id, sensor_id, severity, event_type, description, metrics_snapshot
FROM events ORDER BY ts DESC LIMIT 1";
    }

    await using var cmd = new NpgsqlCommand(sql, conn);
    if (!string.IsNullOrWhiteSpace(vesselId))
    {
        cmd.Parameters.AddWithValue("vessel_id", vesselId!);
    }

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return ReadEvent(reader);
}

EventRecord ReadEvent(NpgsqlDataReader reader)
{
    return new EventRecord(
        reader.GetGuid(0),
        DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7)
    );
}

async Task<Dictionary<string, object?>> QueryStatsAsync(NpgsqlConnection conn, string vesselId, DateTime startTs, DateTime endTs)
{
    const string sql = @"SELECT
  count(*) as sample_count,
  min(engine_rpm) as min_engine_rpm,
  max(engine_rpm) as max_engine_rpm,
  avg(engine_rpm) as avg_engine_rpm,
  min(engine_temp) as min_engine_temp,
  max(engine_temp) as max_engine_temp,
  avg(engine_temp) as avg_engine_temp,
  min(oil_pressure) as min_oil_pressure,
  max(oil_pressure) as max_oil_pressure,
  avg(oil_pressure) as avg_oil_pressure,
  min(fuel_pressure) as min_fuel_pressure,
  max(fuel_pressure) as max_fuel_pressure,
  avg(fuel_pressure) as avg_fuel_pressure,
  min(coolant_temp) as min_coolant_temp,
  max(coolant_temp) as max_coolant_temp,
  avg(coolant_temp) as avg_coolant_temp,
  avg(data_quality_score) as avg_data_quality
FROM telemetry
WHERE vessel_id = @vessel_id AND ts BETWEEN @start_ts AND @end_ts";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("vessel_id", vesselId);
    cmd.Parameters.AddWithValue("start_ts", startTs);
    cmd.Parameters.AddWithValue("end_ts", endTs);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return new Dictionary<string, object?>();
    }

    var stats = new Dictionary<string, object?>();
    for (int i = 0; i < reader.FieldCount; i++)
    {
        stats[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
    }

    return stats;
}

async Task<Dictionary<string, object?>> BuildAnalysisAsync(EventRecord record, Dictionary<string, object?> stats, DateTime windowStart, DateTime windowEnd)
{
    var coerced = CoerceStats(stats);
    string prompt = BuildPrompt(record, coerced, windowStart, windowEnd);

    Dictionary<string, object?> llmPayload = new();
    string? llmError = null;

    try
    {
        string llmText = await CallOllamaAsync(prompt);
        llmPayload = ParseLlmJson(llmText);
    }
    catch (Exception ex)
    {
        llmError = ex.Message;
        logger.LogWarning("Ollama call failed: {Error}", llmError);
    }

    int sampleCount = GetIntStat(stats, "sample_count");
    double? avgQuality = GetDoubleStat(stats, "avg_data_quality");

    string dataQualityNotes = GetStringPayload(llmPayload, "data_quality_notes") ?? string.Empty;

    if (avgQuality.HasValue && avgQuality.Value < 0.6)
    {
        dataQualityNotes = AppendNote(dataQualityNotes, "Data quality is low in the context window.");
    }

    if (sampleCount == 0)
    {
        dataQualityNotes = AppendNote(dataQualityNotes, "No telemetry samples found in the context window.");
    }

    if (!string.IsNullOrWhiteSpace(llmError))
    {
        dataQualityNotes = AppendNote(dataQualityNotes, "LLM unavailable, using fallback response.");
    }

    int confidence = GetIntPayload(llmPayload, "confidence") ?? 50;
    if (sampleCount == 0)
    {
        confidence = Math.Min(confidence, 20);
    }
    if (avgQuality.HasValue && avgQuality.Value < 0.6)
    {
        confidence = Math.Min(confidence, 40);
    }
    if (!string.IsNullOrWhiteSpace(llmError))
    {
        confidence = Math.Min(confidence, 30);
    }

    var analysis = new Dictionary<string, object?>
    {
        ["event_id"] = record.EventId.ToString(),
        ["summary"] = GetStringPayload(llmPayload, "summary") ?? "No summary returned by model.",
        ["possible_causes"] = GetStringListPayload(llmPayload, "possible_causes"),
        ["recommended_actions"] = GetStringListPayload(llmPayload, "recommended_actions"),
        ["confidence"] = confidence,
        ["data_quality_notes"] = dataQualityNotes,
        ["data_sources"] = new[] { "timescaledb.telemetry", "timescaledb.events" },
        ["evidence"] = new Dictionary<string, object?>
        {
            ["window"] = $"{windowStart:o} to {windowEnd:o}",
            ["stats"] = coerced
        }
    };

    return analysis;
}

string BuildPrompt(EventRecord record, Dictionary<string, object?> stats, DateTime windowStart, DateTime windowEnd)
{
    string statsJson = JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });

    var sb = new StringBuilder();
    sb.Append("You are an assistant for maritime telemetry analysis. ");
    sb.Append("Use only the data provided below. Do not guess. ");
    sb.Append("Return JSON only with keys: summary, possible_causes, recommended_actions, confidence, data_quality_notes.\n\n");
    sb.AppendLine($"Event: {record.EventType} (severity {record.Severity})");
    sb.AppendLine($"Description: {record.Description}");
    sb.AppendLine($"Vessel: {record.VesselId}");
    sb.AppendLine($"Timestamp: {record.Ts:o}");
    sb.AppendLine($"Data window: {windowStart:o} to {windowEnd:o}");
    sb.AppendLine();
    sb.AppendLine("Stats:");
    sb.AppendLine(statsJson);
    sb.AppendLine();
    sb.AppendLine("Rules:");
    sb.AppendLine("- If sample_count is 0, say data is missing and lower confidence.");
    sb.AppendLine("- If avg_data_quality is below 0.6, mention low data quality and lower confidence.");
    sb.AppendLine("- Keep possible_causes and recommended_actions short.");

    return sb.ToString();
}

bool WantsHtml(HttpRequest request, string? format)
{
    if (!string.IsNullOrWhiteSpace(format) && format.Equals("html", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (request.Headers.TryGetValue("Accept", out var accept))
    {
        return accept.Any(value => value.Contains("text/html", StringComparison.OrdinalIgnoreCase));
    }

    return false;
}

string BuildHtmlResponse(EventRecord record, Dictionary<string, object?> analysis, DateTime createdAt, bool fromCache)
{
    string summary = GetStringPayload(analysis, "summary") ?? "No summary returned by model.";
    var causes = GetStringListPayload(analysis, "possible_causes");
    var actions = GetStringListPayload(analysis, "recommended_actions");
    int confidence = GetIntPayload(analysis, "confidence") ?? 50;
    string dataQualityNotes = GetStringPayload(analysis, "data_quality_notes") ?? string.Empty;

    string causesHtml = causes.Count == 0 ? "<li>None</li>" : string.Join("", causes.Select(c => $"<li>{System.Net.WebUtility.HtmlEncode(c)}</li>"));
    string actionsHtml = actions.Count == 0 ? "<li>None</li>" : string.Join("", actions.Select(a => $"<li>{System.Net.WebUtility.HtmlEncode(a)}</li>"));

    string window = string.Empty;
    if (analysis.TryGetValue("evidence", out var evidenceObj) && evidenceObj is Dictionary<string, object?> evidence)
    {
        if (evidence.TryGetValue("window", out var windowObj) && windowObj != null)
        {
            window = windowObj.ToString() ?? string.Empty;
        }
    }

    string title = $"{record.EventType} - {record.VesselId}";
    string summaryEscaped = System.Net.WebUtility.HtmlEncode(summary);
    string notesEscaped = System.Net.WebUtility.HtmlEncode(dataQualityNotes);
    string sourceNote = fromCache ? "Cached analysis" : "New analysis";

    return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>AI Analysis - {System.Net.WebUtility.HtmlEncode(title)}</title>
  <style>
    body {{ font-family: Arial, sans-serif; background: #f5f6f8; margin: 0; padding: 24px; color: #1f2933; }}
    .card {{ background: #ffffff; border-radius: 12px; box-shadow: 0 2px 8px rgba(0,0,0,0.08); padding: 20px; max-width: 900px; margin: 0 auto; }}
    h1 {{ font-size: 20px; margin: 0 0 6px 0; }}
    .meta {{ color: #52606d; font-size: 13px; margin-bottom: 12px; }}
    .section {{ margin-top: 16px; }}
    .label {{ font-weight: 600; margin-bottom: 6px; }}
    ul {{ margin: 6px 0 0 20px; }}
    .confidence {{ font-weight: 600; }}
    .pill {{ display: inline-block; padding: 2px 8px; border-radius: 12px; background: #e1e8f0; font-size: 12px; margin-left: 8px; }}
    .summary {{ background: #f0f4f8; padding: 12px; border-radius: 8px; }}
    .notes {{ background: #fff7ed; padding: 10px; border-radius: 8px; }}
  </style>
</head>
<body>
  <div class=""card"">
    <h1>AI Analysis</h1>
    <div class=""meta"">{System.Net.WebUtility.HtmlEncode(record.VesselId)} - {System.Net.WebUtility.HtmlEncode(record.EventType)} - {record.Ts:o}</div>
    <div class=""meta"">{sourceNote} - Created at {createdAt:o}</div>
    <div class=""section"">
      <div class=""label"">Summary <span class=""pill confidence"">Confidence: {confidence}</span></div>
      <div class=""summary"">{summaryEscaped}</div>
    </div>
    <div class=""section"">
      <div class=""label"">Possible causes</div>
      <ul>{causesHtml}</ul>
    </div>
    <div class=""section"">
      <div class=""label"">Recommended actions</div>
      <ul>{actionsHtml}</ul>
    </div>
    <div class=""section"">
      <div class=""label"">Data quality notes</div>
      <div class=""notes"">{notesEscaped}</div>
    </div>
    <div class=""section"">
      <div class=""label"">Context window</div>
      <div>{System.Net.WebUtility.HtmlEncode(window)}</div>
    </div>
  </div>
</body>
</html>";
}

async Task<string> CallOllamaAsync(string prompt)
{
    var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
    var payload = new { model = OllamaModel, prompt, stream = false };

    using var response = await client.PostAsJsonAsync($"{OllamaHost}/api/generate", payload);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(content);
    if (doc.RootElement.TryGetProperty("response", out var responseElement))
    {
        return responseElement.GetString() ?? string.Empty;
    }

    return string.Empty;
}

Dictionary<string, object?> ParseLlmJson(string text)
{
    if (string.IsNullOrWhiteSpace(text))
    {
        return new Dictionary<string, object?>();
    }

    string trimmed = text.Trim();
    if (TryParseJson(trimmed, out var doc))
    {
        return ExtractLlmPayload(doc!.RootElement);
    }

    int start = trimmed.IndexOf('{');
    int end = trimmed.LastIndexOf('}');
    if (start >= 0 && end > start)
    {
        string slice = trimmed.Substring(start, end - start + 1);
        if (TryParseJson(slice, out doc))
        {
            return ExtractLlmPayload(doc!.RootElement);
        }
    }

    return new Dictionary<string, object?> { ["summary"] = trimmed };
}

bool TryParseJson(string text, out JsonDocument? document)
{
    try
    {
        document = JsonDocument.Parse(text);
        return true;
    }
    catch
    {
        document = null;
        return false;
    }
}

Dictionary<string, object?> ExtractLlmPayload(JsonElement root)
{
    var payload = new Dictionary<string, object?>();
    if (root.ValueKind != JsonValueKind.Object)
    {
        return payload;
    }

    if (root.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.String)
    {
        payload["summary"] = summaryElement.GetString();
    }

    if (root.TryGetProperty("possible_causes", out var causesElement))
    {
        payload["possible_causes"] = ExtractStringList(causesElement);
    }

    if (root.TryGetProperty("recommended_actions", out var actionsElement))
    {
        payload["recommended_actions"] = ExtractStringList(actionsElement);
    }

    if (root.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetInt32(out var confidence))
    {
        payload["confidence"] = confidence;
    }

    if (root.TryGetProperty("data_quality_notes", out var notesElement) && notesElement.ValueKind == JsonValueKind.String)
    {
        payload["data_quality_notes"] = notesElement.GetString();
    }

    return payload;
}

List<string> ExtractStringList(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Array)
    {
        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                list.Add(item.GetString()!);
            }
        }
        return list;
    }

    if (element.ValueKind == JsonValueKind.String)
    {
        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? new List<string>() : new List<string> { value! };
    }

    return new List<string>();
}

Dictionary<string, object?> CoerceStats(Dictionary<string, object?> stats)
{
    var coerced = new Dictionary<string, object?>();
    foreach (var (key, value) in stats)
    {
        if (value == null)
        {
            coerced[key] = null;
            continue;
        }

        if (value is decimal dec)
        {
            coerced[key] = (double)dec;
        }
        else if (value is double dbl)
        {
            coerced[key] = dbl;
        }
        else if (value is float flt)
        {
            coerced[key] = (double)flt;
        }
        else if (value is int i)
        {
            coerced[key] = (double)i;
        }
        else if (value is long l)
        {
            coerced[key] = (double)l;
        }
        else
        {
            coerced[key] = value;
        }
    }
    return coerced;
}

int GetIntStat(Dictionary<string, object?> stats, string key)
{
    if (!stats.TryGetValue(key, out var value) || value == null)
    {
        return 0;
    }

    try
    {
        return Convert.ToInt32(value);
    }
    catch
    {
        return 0;
    }
}

double? GetDoubleStat(Dictionary<string, object?> stats, string key)
{
    if (!stats.TryGetValue(key, out var value) || value == null)
    {
        return null;
    }

    try
    {
        return Convert.ToDouble(value);
    }
    catch
    {
        return null;
    }
}

string? GetStringPayload(Dictionary<string, object?> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value == null)
    {
        return null;
    }

    return value.ToString();
}

int? GetIntPayload(Dictionary<string, object?> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value == null)
    {
        return null;
    }

    if (value is int i)
    {
        return i;
    }

    if (int.TryParse(value.ToString(), out var parsed))
    {
        return parsed;
    }

    return null;
}

List<string> GetStringListPayload(Dictionary<string, object?> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value == null)
    {
        return new List<string>();
    }

    if (value is List<string> stringList)
    {
        return stringList;
    }

    if (value is List<object?> objList)
    {
        var values = new List<string>();
        foreach (var item in objList)
        {
            if (item == null)
            {
                continue;
            }
            var text = item.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text);
            }
        }
        return values;
    }

    if (value is string str)
    {
        return string.IsNullOrWhiteSpace(str) ? new List<string>() : new List<string> { str };
    }

    return new List<string>();
}

string AppendNote(string existing, string note)
{
    if (string.IsNullOrWhiteSpace(existing))
    {
        return note;
    }

    return $"{existing} {note}";
}

Dictionary<string, object?> EventToDictionary(EventRecord record)
{
    return new Dictionary<string, object?>
    {
        ["event_id"] = record.EventId.ToString(),
        ["ts"] = record.Ts.ToString("o"),
        ["vessel_id"] = record.VesselId,
        ["sensor_id"] = record.SensorId,
        ["severity"] = record.Severity,
        ["event_type"] = record.EventType,
        ["description"] = record.Description,
        ["metrics_snapshot"] = record.MetricsSnapshotJson
    };
}

async Task StoreAnalysisAsync(NpgsqlConnection conn, EventRecord record, Dictionary<string, object?> analysis, string[] ragSources)
{
    const string sql = @"INSERT INTO ai_analyses (id, created_at, event_id, vessel_id, ai_summary, rag_sources)
VALUES (@id, @created_at, @event_id, @vessel_id, @ai_summary, @rag_sources)";

    string analysisJson = JsonSerializer.Serialize(analysis);
    var createdAt = DateTime.UtcNow;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", Guid.NewGuid());
    cmd.Parameters.AddWithValue("created_at", createdAt);
    cmd.Parameters.AddWithValue("event_id", record.EventId);
    cmd.Parameters.AddWithValue("vessel_id", record.VesselId);

    var jsonParam = new NpgsqlParameter("ai_summary", NpgsqlDbType.Jsonb) { Value = analysisJson };
    cmd.Parameters.Add(jsonParam);

    var sourcesParam = new NpgsqlParameter("rag_sources", NpgsqlDbType.Array | NpgsqlDbType.Text)
    {
        Value = ragSources
    };
    cmd.Parameters.Add(sourcesParam);

    await cmd.ExecuteNonQueryAsync();
}

async Task<StoredAnalysis?> QueryLatestAnalysisAsync(NpgsqlConnection conn, Guid eventId)
{
    const string sql = @"SELECT created_at, ai_summary
FROM ai_analyses
WHERE event_id = @event_id
ORDER BY created_at DESC
LIMIT 1";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("event_id", eventId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    var createdAt = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
    string json = reader.GetString(1);

    var parsed = ParseStoredAnalysis(json);
    return parsed == null ? null : new StoredAnalysis(createdAt, parsed);
}

Dictionary<string, object?>? ParseStoredAnalysis(string json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return null;
    }

    try
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return (Dictionary<string, object?>)ConvertJsonElement(doc.RootElement);
    }
    catch
    {
        return null;
    }
}

object ConvertJsonElement(JsonElement element)
{
    switch (element.ValueKind)
    {
        case JsonValueKind.Object:
            var dict = new Dictionary<string, object?>();
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = ConvertJsonElement(prop.Value);
            }
            return dict;
        case JsonValueKind.Array:
            var list = new List<object?>();
            foreach (var item in element.EnumerateArray())
            {
                list.Add(ConvertJsonElement(item));
            }
            return list;
        case JsonValueKind.String:
            return element.GetString() ?? string.Empty;
        case JsonValueKind.Number:
            if (element.TryGetInt64(out var l))
            {
                return (double)l;
            }
            if (element.TryGetDouble(out var d))
            {
                return d;
            }
            return element.ToString();
        case JsonValueKind.True:
            return true;
        case JsonValueKind.False:
            return false;
        case JsonValueKind.Null:
        case JsonValueKind.Undefined:
            return null;
        default:
            return element.ToString();
    }
}

record EventRecord(
    Guid EventId,
    DateTime Ts,
    string VesselId,
    string SensorId,
    string Severity,
    string EventType,
    string Description,
    string? MetricsSnapshotJson
);

record AnalyzeRequest(string event_id);

record McpCallRequest(string tool, Dictionary<string, JsonElement>? arguments);

record StoredAnalysis(DateTime CreatedAt, Dictionary<string, object?> Analysis);
