# LIN.Cloud.Node.Monitor — API Specification

> **Instructions for the AI building this API**
>
> This document describes the HTTP API that the `LIN.Cloud.Node.Monitor` .NET 10 Worker Service
> calls to persist Docker container logs and metrics.
> Build this API as a **.NET 10 ASP.NET Core Minimal API** project.
> Implement every endpoint exactly as specified: same route, same JSON property names (camelCase),
> same HTTP status codes. The monitor client will break on any deviation.

---

## Technology Stack

| Layer | Choice |
|---|---|
| Framework | .NET 10 — ASP.NET Core Minimal API |
| Serialization | `System.Text.Json` with `PropertyNamingPolicy = CamelCase` |
| Persistence | Your choice (SQL Server / PostgreSQL / SQLite / in-memory for dev) |
| Authentication | Optional API key via `X-Api-Key` request header |

---

## Authentication

Every request from the monitor **may** include an `X-Api-Key` header.
If you want to enforce authentication, reject requests with a missing or wrong key with **`401 Unauthorized`**.
If you do not enforce it, simply ignore the header.

```
X-Api-Key: <secret>
```

---

## Environment Variables consumed by the Monitor (for reference)

| Variable | Default | Purpose |
|---|---|---|
| `API_URI` | `http://localhost:5000` | Base URL of **this** API |
| `API_KEY` | *(empty)* | Value sent in `X-Api-Key` |

---

## Endpoints

### 1. `POST /api/logs` — Index a container log line

Called by `DockerLogService` for every log line read from a running container.

#### Request body

```json
{
  "id":          "a3f1c2d4...",
  "containerId": "abc123def456...",
  "timestamp":   "2024-06-01T12:00:00.000Z",
  "message":     "Server started on port 8080"
}
```

| Field | Type | Description |
|---|---|---|
| `id` | `string` | SHA-1 deduplication key (`hex`, 40 chars). If a document with this `id` already exists, **upsert / ignore** — do not create a duplicate. |
| `containerId` | `string` | Full Docker container ID (64 hex chars) |
| `timestamp` | `string` (ISO 8601 UTC) | Timestamp parsed from the Docker log line |
| `message` | `string` | Raw log message text |

#### Response

| Status | Meaning |
|---|---|
| `200 OK` or `201 Created` | Log entry stored (or already existed — idempotent) |
| `400 Bad Request` | Malformed JSON or missing required fields |

---

### 2. `POST /api/metrics` — Index a container metrics snapshot

Called by `ContainerMetricsService` every **10 seconds** for each running container.

#### Request body

```json
{
  "containerId":     "abc123def456...",
  "containerName":   "my-nginx",
  "timestamp":       "2024-06-01T12:00:00.000Z",
  "memoryUsageKb":   131072,
  "memoryLimitKb":   524288,
  "cpuUsageCurrent": 4830000000,
  "cpuUsageMax":     980000000000
}
```

| Field | Type | Description |
|---|---|---|
| `containerId` | `string` | Full Docker container ID |
| `containerName` | `string` | First name from Docker (`Names[0]`, leading `/` stripped) |
| `timestamp` | `string` (ISO 8601 UTC) | Time the snapshot was taken |
| `memoryUsageKb` | `uint64` | Current memory usage in KB (`memory_stats.usage / 1024`) |
| `memoryLimitKb` | `uint64` | Memory limit in KB (`memory_stats.limit / 1024`) |
| `cpuUsageCurrent` | `uint64` | Cumulative container CPU time in **nanoseconds** (`cpu_stats.cpu_usage.total_usage`) |
| `cpuUsageMax` | `uint64` | Cumulative host system CPU time in **nanoseconds** (`cpu_stats.system_cpu_usage`) |

> **Note:** CPU values are raw nanosecond counters, not percentages.
> To compute CPU% from two consecutive snapshots apply:
> `((cpuUsageCurrent_t2 - cpuUsageCurrent_t1) / (cpuUsageMax_t2 - cpuUsageMax_t1)) * onlineCpus * 100`

#### Response

| Status | Meaning |
|---|---|
| `200 OK` or `201 Created` | Metric snapshot stored |
| `400 Bad Request` | Malformed JSON or missing required fields |

---

### 3. `POST /api/checkpoints/{containerId}` — Upsert a log checkpoint

Called by `DockerLogService` after every successfully indexed log line to record the latest
processed timestamp for a container. This is a **full upsert** keyed on `containerId`.

#### Route parameter

| Parameter | Type | Description |
|---|---|---|
| `containerId` | `string` | Full Docker container ID |

#### Request body

```json
{
  "containerId":     "abc123def456...",
  "lastTimestamp":   "2024-06-01T12:00:05.123Z"
}
```

| Field | Type | Description |
|---|---|---|
| `containerId` | `string` | Full Docker container ID (same as the route parameter) |
| `lastTimestamp` | `string` (ISO 8601 UTC) | Timestamp of the last successfully indexed log line |

#### Response

| Status | Meaning |
|---|---|
| `200 OK` | Checkpoint updated (row already existed) |
| `201 Created` | Checkpoint created for the first time |
| `400 Bad Request` | Malformed JSON |

---

### 4. `GET /api/checkpoints/{containerId}` — Read a log checkpoint

Called by `DockerLogService` at the start of each container log stream session to know
from which timestamp to resume reading.

#### Route parameter

| Parameter | Type | Description |
|---|---|---|
| `containerId` | `string` | Full Docker container ID |

#### Response — checkpoint found

**`200 OK`**

```json
{
  "containerId":   "abc123def456...",
  "lastTimestamp": "2024-06-01T12:00:05.123Z"
}
```

The monitor reads only the `lastTimestamp` field. Other fields are allowed but ignored.

#### Response — checkpoint not found

**`404 Not Found`** — any body (or empty). The monitor treats `404` as "no checkpoint exists"
and falls back to `UtcNow - 5 seconds`.

---

## JSON Serialization Rules

- All JSON property names are **camelCase** (the monitor serializes with `JsonNamingPolicy.CamelCase`).
- All `DateTime` values are **ISO 8601 UTC** strings (e.g. `"2024-06-01T12:00:00.000Z"`).
- `uint64` fields are JSON numbers. Ensure your deserializer handles 64-bit unsigned integers
  (`ulong` in C#, `bigint` / `NUMERIC(20)` in SQL).

---

## Persistence Notes

### Logs (`/api/logs`)

- Use `id` (the SHA-1 hex string) as the **primary key** or unique index.
- On duplicate `id`, do **nothing** (upsert / `INSERT OR IGNORE`). This is the deduplication guarantee.
- Recommended columns: `id TEXT PK`, `container_id TEXT`, `timestamp TIMESTAMPTZ`, `message TEXT`.

### Metrics (`/api/metrics`)

- No deduplication required. Each `POST` creates a new row.
- Recommended columns: `id BIGSERIAL PK`, `container_id TEXT`, `container_name TEXT`,
  `timestamp TIMESTAMPTZ`, `memory_usage_kb BIGINT`, `memory_limit_kb BIGINT`,
  `cpu_usage_current BIGINT`, `cpu_usage_max BIGINT`.

### Checkpoints (`/api/checkpoints`)

- One row per `containerId` — **upsert keyed on `containerId`**.
- Recommended columns: `container_id TEXT PK`, `last_timestamp TIMESTAMPTZ`.

---

## Suggested Project Structure

```
LIN.Cloud.Node.Api/
├── Endpoints/
│   ├── LogEndpoints.cs
│   ├── MetricEndpoints.cs
│   └── CheckpointEndpoints.cs
├── Models/
│   ├── LogDocument.cs
│   ├── ContainerMetric.cs
│   └── LogCheckpoint.cs
├── Data/
│   └── AppDbContext.cs
├── Program.cs
└── LIN.Cloud.Node.Api.csproj
```

---

## Minimal `Program.cs` skeleton

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(/* configure DB */);

var app = builder.Build();

// Optional: enforce X-Api-Key
// app.Use(async (ctx, next) => { /* validate header */ await next(); });

app.MapLogEndpoints();
app.MapMetricEndpoints();
app.MapCheckpointEndpoints();

app.Run();
```

---

## Call Frequency Reference

| Endpoint | Caller | Frequency |
|---|---|---|
| `POST /api/logs` | `DockerLogService` | Every log line emitted by any running container |
| `POST /api/metrics` | `ContainerMetricsService` | Every 10 s × number of running containers |
| `POST /api/checkpoints/{id}` | `DockerLogService` | After every indexed log line |
| `GET  /api/checkpoints/{id}` | `DockerLogService` | Once per container on stream (re)start |
