using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "INFO";
var logger = new Logger(logLevel);

string dbHost = GetEnv("DB_HOST", "localhost");
int dbPort = int.Parse(GetEnv("DB_PORT", "5432"));
string dbUser = GetEnv("DB_USER", "demo");
string dbPassword = GetEnv("DB_PASSWORD", "demo_password");
string dbName = GetEnv("DB_NAME", "maritime");
int sleepSeconds = int.Parse(GetEnv("SLEEP_SECONDS", "5"));
string agentUrl = GetEnv("AGENT_URL", "http://agent:8000").TrimEnd('/');

string connString = $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName}";

var vessels = new[] { "vessel_001", "vessel_002" };
var random = new Random();
using var agentClient = new HttpClient { BaseAddress = new Uri(agentUrl) };

var state = InitState(vessels, random);
var loopCounter = 0;

await using var conn = await ConnectWithRetryAsync(connString, logger);

while (true)
{
    var now = DateTime.UtcNow;
    await using var cmd = conn.CreateCommand();

    loopCounter++;
    foreach (var vesselId in vessels)
    {
        var metrics = state[vesselId];
        metrics.EngineRpm = Clamp(metrics.EngineRpm + NextRange(random, -2, 2), 70, 140);
        metrics.EngineTemp = Clamp(metrics.EngineTemp + NextRange(random, -0.5, 0.5), 65, 95);
        metrics.OilPressure = Clamp(metrics.OilPressure + NextRange(random, -1, 1), 25, 55);
        metrics.FuelPressure = Clamp(metrics.FuelPressure + NextRange(random, -1, 1), 35, 70);
        metrics.CoolantTemp = Clamp(metrics.CoolantTemp + NextRange(random, -0.5, 0.5), 55, 85);

        double dataQuality = NextRange(random, 0.92, 0.99);
        var evt = MaybeGenerateEvent(random, metrics);
        if (vesselId == "vessel_001" && loopCounter % 120 == 0)
        {
            evt = ForceScheduledEvent(random, metrics);
        }

        var ts = now;
        if (evt != null && (evt.EventType == "overtemp" || evt.EventType == "low_oil_pressure" || evt.EventType == "rpm_anomaly"))
        {
            dataQuality = Math.Min(dataQuality, 0.8);
        }

        if (evt != null && evt.SkipInsert)
        {
            var eventId = await InsertEventAsync(conn, vesselId, ts, evt, metrics, dataQuality);
            if (evt.AutoAnalyze)
            {
                await TriggerAnalyzeAsync(agentClient, eventId, logger);
            }
            continue;
        }

        await InsertTelemetryAsync(conn, vesselId, ts, metrics, dataQuality);

        if (evt != null)
        {
            var eventId = await InsertEventAsync(conn, vesselId, ts, evt, metrics, dataQuality);
            if (evt.AutoAnalyze)
            {
                await TriggerAnalyzeAsync(agentClient, eventId, logger);
            }
        }
    }

    logger.Info("Inserted telemetry batch");
    await Task.Delay(TimeSpan.FromSeconds(sleepSeconds));
}

static string GetEnv(string key, string fallback) =>
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))
        ? fallback
        : Environment.GetEnvironmentVariable(key)!.Trim();

static async Task<NpgsqlConnection> ConnectWithRetryAsync(string connString, Logger logger)
{
    while (true)
    {
        try
        {
            var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();
            logger.Info("Connected to database");
            return conn;
        }
        catch (Exception ex)
        {
            logger.Warn($"DB not ready ({ex.Message}), retrying in 2s");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

static Dictionary<string, Metrics> InitState(string[] vessels, Random random)
{
    var state = new Dictionary<string, Metrics>();
    foreach (var vessel in vessels)
    {
        state[vessel] = new Metrics
        {
            EngineRpm = NextRange(random, 90, 110),
            EngineTemp = NextRange(random, 70, 85),
            OilPressure = NextRange(random, 35, 50),
            FuelPressure = NextRange(random, 45, 60),
            CoolantTemp = NextRange(random, 60, 75)
        };
    }
    return state;
}

static GeneratedEvent? MaybeGenerateEvent(Random random, Metrics metrics)
{
    var roll = random.NextDouble();
    if (roll > 0.06)
    {
        return null;
    }

    var eventTypes = new[]
    {
        "overtemp",
        "low_oil_pressure",
        "rpm_anomaly",
        "overtemp",
        "low_oil_pressure",
        "rpm_anomaly"
    };

    string eventType = eventTypes[random.Next(eventTypes.Length)];

    string severity = "WARNING";
    string sensorId = "telemetry";
    string description = "Telemetry anomaly detected.";
    bool skipInsert = false;

    if (eventType == "overtemp")
    {
        metrics.EngineTemp += NextRange(random, 15, 30);
        severity = metrics.EngineTemp > 105 ? "CRITICAL" : "WARNING";
        sensorId = "engine_temp";
        description = "Engine temperature spike detected.";
    }
    else if (eventType == "low_oil_pressure")
    {
        metrics.OilPressure -= NextRange(random, 18, 28);
        severity = metrics.OilPressure < 18 ? "CRITICAL" : "WARNING";
        sensorId = "oil_pressure";
        description = "Oil pressure drop detected.";
    }
    else if (eventType == "rpm_anomaly")
    {
        metrics.EngineRpm += NextRange(random, 40, 80);
        severity = "WARNING";
        sensorId = "engine_rpm";
        description = "RPM surge detected.";
    }
    return new GeneratedEvent(eventType, severity, sensorId, description, skipInsert, true);
}

static GeneratedEvent ForceScheduledEvent(Random random, Metrics metrics)
{
    var eventTypes = new[] { "overtemp", "low_oil_pressure", "rpm_anomaly" };
    string eventType = eventTypes[random.Next(eventTypes.Length)];

    string severity = "CRITICAL";
    string sensorId = "telemetry";
    string description = "Scheduled demo anomaly.";

    if (eventType == "overtemp")
    {
        metrics.EngineTemp += 30;
        severity = "CRITICAL";
        sensorId = "engine_temp";
        description = "Scheduled over-temperature spike.";
    }
    else if (eventType == "low_oil_pressure")
    {
        metrics.OilPressure -= 20;
        severity = "CRITICAL";
        sensorId = "oil_pressure";
        description = "Scheduled oil pressure drop.";
    }
    else if (eventType == "rpm_anomaly")
    {
        metrics.EngineRpm += 50;
        severity = "WARNING";
        sensorId = "engine_rpm";
        description = "Scheduled RPM surge.";
    }

    return new GeneratedEvent(eventType, severity, sensorId, description, false, true);
}
static async Task InsertTelemetryAsync(NpgsqlConnection conn, string vesselId, DateTime ts, Metrics metrics, double dataQuality)
{
    const string sql = @"INSERT INTO telemetry (
  vessel_id, ts, engine_rpm, engine_temp, oil_pressure,
  fuel_pressure, coolant_temp, data_quality_score
) VALUES (@vessel_id, @ts, @engine_rpm, @engine_temp, @oil_pressure, @fuel_pressure, @coolant_temp, @data_quality_score)";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("vessel_id", vesselId);
    cmd.Parameters.AddWithValue("ts", ts);
    cmd.Parameters.AddWithValue("engine_rpm", metrics.EngineRpm);
    cmd.Parameters.AddWithValue("engine_temp", metrics.EngineTemp);
    cmd.Parameters.AddWithValue("oil_pressure", metrics.OilPressure);
    cmd.Parameters.AddWithValue("fuel_pressure", metrics.FuelPressure);
    cmd.Parameters.AddWithValue("coolant_temp", metrics.CoolantTemp);
    cmd.Parameters.AddWithValue("data_quality_score", dataQuality);

    await cmd.ExecuteNonQueryAsync();
}

static async Task<string> InsertEventAsync(NpgsqlConnection conn, string vesselId, DateTime ts, GeneratedEvent evt, Metrics metrics, double dataQuality)
{
    const string sql = @"INSERT INTO events (
  event_id, ts, vessel_id, sensor_id, severity, event_type, description, metrics_snapshot
) VALUES (@event_id, @ts, @vessel_id, @sensor_id, @severity, @event_type, @description, @metrics_snapshot)";

    var snapshot = new Dictionary<string, object>
    {
        ["engine_rpm"] = metrics.EngineRpm,
        ["engine_temp"] = metrics.EngineTemp,
        ["oil_pressure"] = metrics.OilPressure,
        ["fuel_pressure"] = metrics.FuelPressure,
        ["coolant_temp"] = metrics.CoolantTemp,
        ["data_quality_score"] = dataQuality
    };

    string snapshotJson = JsonSerializer.Serialize(snapshot);
    var eventId = Guid.NewGuid();

    await using var cmd = new NpgsqlCommand(sql, conn);
    var idParam = new NpgsqlParameter("event_id", NpgsqlDbType.Uuid) { Value = eventId };
    cmd.Parameters.Add(idParam);
    cmd.Parameters.AddWithValue("ts", ts);
    cmd.Parameters.AddWithValue("vessel_id", vesselId);
    cmd.Parameters.AddWithValue("sensor_id", evt.SensorId);
    cmd.Parameters.AddWithValue("severity", evt.Severity);
    cmd.Parameters.AddWithValue("event_type", evt.EventType);
    cmd.Parameters.AddWithValue("description", evt.Description);

    var jsonParam = new NpgsqlParameter("metrics_snapshot", NpgsqlDbType.Jsonb) { Value = snapshotJson };
    cmd.Parameters.Add(jsonParam);

    await cmd.ExecuteNonQueryAsync();
    return eventId.ToString();
}

static double Clamp(double value, double low, double high) => Math.Max(low, Math.Min(high, value));

static double NextRange(Random random, double min, double max) => min + (random.NextDouble() * (max - min));

static async Task TriggerAnalyzeAsync(HttpClient client, string eventId, Logger logger)
{
    try
    {
        var payload = new { event_id = eventId };
        using var response = await client.PostAsJsonAsync("/analyze", payload);
        response.EnsureSuccessStatusCode();
        logger.Info($"Auto-analyzed event {eventId}");
    }
    catch (Exception ex)
    {
        logger.Warn($"Auto-analyze failed for {eventId}: {ex.Message}");
    }
}

record Metrics
{
    public double EngineRpm { get; set; }
    public double EngineTemp { get; set; }
    public double OilPressure { get; set; }
    public double FuelPressure { get; set; }
    public double CoolantTemp { get; set; }
}

record GeneratedEvent(string EventType, string Severity, string SensorId, string Description, bool SkipInsert, bool AutoAnalyze = false);

sealed class Logger
{
    private readonly LogLevel _level;

    public Logger(string level)
    {
        _level = Parse(level);
    }

    public void Info(string message)
    {
        if (_level <= LogLevel.Information)
        {
            Console.WriteLine($"INFO:generator:{message}");
        }
    }

    public void Warn(string message)
    {
        if (_level <= LogLevel.Warning)
        {
            Console.WriteLine($"WARNING:generator:{message}");
        }
    }

    private static LogLevel Parse(string level)
    {
        if (Enum.TryParse(level, true, out LogLevel parsed))
        {
            return parsed;
        }

        return LogLevel.Information;
    }
}

enum LogLevel
{
    Information = 2,
    Warning = 3
}
