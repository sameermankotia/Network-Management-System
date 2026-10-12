# Network Management System

Monitoring and management for a university campus network: device inventory,
live availability and utilization, topology, alerting with an acknowledgment
workflow, on-demand diagnostics, and reporting.

React + TypeScript on the front, ASP.NET Core on the back, PostgreSQL for
inventory and time-series samples, SignalR for live updates.

![stack](https://img.shields.io/badge/stack-React%20·%20TypeScript%20·%20.NET%208%20·%20PostgreSQL-informational)

## What it does

**Live monitoring.** A background poller probes every device on its own
interval, stores a sample, re-derives device and link state, and pushes the
result to connected browsers over a WebSocket. Nothing in the UI polls for a
page refresh: status changes, new alerts and cycle summaries arrive as events.

**Topology.** Links between devices and interfaces are first-class records.
The graph is laid out in tiers by hop distance from the core, so the picture
matches how the network is actually built, and a path finder answers "how do we
get from here to there, and what is in the way".

**Alerting.** Rules compare a metric (reachability, latency, loss, CPU, memory,
interface utilization) against a threshold for a duration, scoped to all
devices, a type, a tag, a site or one device. A breach opens exactly one alert
per target, raises its occurrence count while it persists, notifies the chosen
channels, and auto-resolves when the metric comes back. Operators acknowledge,
comment and resolve; every transition lands on the alert timeline.

**Diagnostics.** Ping, TCP port check, traceroute and a composite health check
that scores reachability, latency, loss, CPU, memory, interface state, uplink
headroom, 24-hour availability and open alerts in one pass.

**Reporting.** Interface utilization with peaks and errors, device uptime with
outage counts and downtime, alert trends by severity with mean time to
acknowledge and resolve, plus CSV export.

## Running it

```bash
docker compose up --build
```

- UI: http://localhost:5173
- API + Swagger: http://localhost:5080/swagger
- Sign in with `admin@campus.edu` / `ChangeMe123!`

The API applies `db/schema.sql` on startup, seeds a sample campus from
`db/seed.sql`, and creates the admin account from the `Admin:*` settings. It
starts in **synthetic** monitoring mode, which generates deterministic
telemetry so the whole stack is usable without a lab full of switches.

### Running the pieces directly

```bash
# Postgres
docker compose up -d db

# API (http://localhost:5080)
cd server && dotnet run --project src/Nms.Api

# Web (http://localhost:5173, proxies /api and /hubs to the API)
cd web && npm install && npm run dev
```

### Tests

```bash
cd server && dotnet test
```

They cover the parts worth pinning down without a database: rule targeting and
threshold edges, poll scheduling, breach timing, and the bounds the synthetic
telemetry has to respect.

# Design

## How the pieces fit

One ASP.NET Core process serves the API, hosts the SignalR hub and runs the
poller, so a sample can go from probe to browser without a queue or a second
service in between. Everything durable lives in Postgres; the only in-memory
state is the latest reading per device and the breach clocks, both of which are
cheap to lose on restart.

```mermaid
flowchart TB
    subgraph browser["Browser"]
        direction LR
        ui["React pages<br>dashboard · devices · topology<br>alerts · rules · reports"]
        qcache["TanStack Query cache"]
        hubclient["SignalR client"]
        ui --> qcache
        ui --> hubclient
    end

    proxy["nginx in production, Vite dev server locally<br>proxies /api, upgrades /hubs to a WebSocket"]

    subgraph apiproc["ASP.NET Core process"]
        direction LR
        subgraph request["Request path"]
            direction TB
            endpoints["Minimal API endpoints"]
            services["TopologyService<br>DiagnosticRunner<br>ReportQueries"]
            endpoints --> services
        end
        subgraph background["Monitoring path"]
            direction TB
            poller["PollingService<br>BackgroundService"]
            engine["AlertEngine"]
            poller --> engine
        end
        livecache["LiveTelemetryCache<br>latest reading per device"]
        hub["MonitoringHub"]
    end

    pg[(PostgreSQL<br>inventory · samples · alerts)]
    netdev["Campus devices<br>ICMP · TCP · SNMP seam"]
    channels["Email · webhook"]

    qcache <--> proxy
    hubclient <--> proxy
    proxy --> endpoints
    proxy <--> hub
    poller --> netdev
    poller --> livecache
    livecache -.->|serves list and dashboard reads| endpoints
    poller --> hub
    engine --> hub
    engine --> channels
    services --> pg
    poller --> pg
    engine --> pg
```

## What one poll cycle does

This loop is the system. Probes run in parallel because they are network-bound;
database writes are sequential because they share one `DbContext`. A cycle for
the seeded campus — 14 devices, 18 interfaces — completes in roughly 150 ms.

```mermaid
sequenceDiagram
    autonumber
    participant Timer as PeriodicTimer
    participant Poller as PollingService
    participant Probe as IDeviceProbe
    participant Sampler as IResourceSampler
    participant DB as PostgreSQL
    participant Engine as AlertEngine
    participant Hub as MonitoringHub
    participant UI as Browser

    Timer->>Poller: tick every 5s
    Poller->>DB: load monitored devices and enabled rules
    Note over Poller: keep only devices whose own<br>poll interval has elapsed
    loop each due device, 16 probes in flight
        Poller->>Probe: ProbeAsync
        Probe-->>Poller: reachable, latency, packet loss
        Poller->>Sampler: Sample and SampleInterfaces
        Sampler-->>Poller: cpu, memory, per-port throughput
    end
    Poller->>DB: insert samples, update status, uptime, last seen
    Poller->>Engine: EvaluateAsync per device
    Engine->>DB: raise, bump or resolve alerts plus timeline rows
    Engine->>Hub: AlertRaised · AlertChanged
    Poller->>DB: recompute link status from endpoint state
    Poller->>Hub: DeviceStatusChanged · SampleRecorded · CycleCompleted
    Hub-->>UI: events over the WebSocket
```

## How state is derived

Device state is a function of the sample, not something an operator sets. Link
state falls out of the two devices it joins, which is why a single switch going
down greys out everything behind it on the topology without any extra
bookkeeping.

```mermaid
flowchart TD
    maint["Maintenance window<br>or polling paused"] -.->|never probed,<br>breach clocks cleared| sample
    sample["Poll result"] --> reach{"Reachable?"}
    reach -- no --> count{"Consecutive<br>failures >= 2?"}
    count -- yes --> down["Down"]
    count -- no --> degraded["Degraded"]
    reach -- yes --> thresholds{"Loss > 1%<br>or latency > 150 ms<br>or CPU > 92%<br>or memory > 95%?"}
    thresholds -- yes --> degraded
    thresholds -- no --> up["Up"]

    down --> link{"Link state from<br>its two endpoints"}
    degraded --> link
    up --> link
    link -- either end down --> linkdown["Link Down"]
    link -- both ends up --> linkup["Link Up"]
    link -- anything else --> linkdeg["Link Degraded or Unknown"]
```

## How a rule becomes an alert

Every enabled rule whose scope matches the device is evaluated on every poll.
The duration test is what keeps a single slow ping from paging anyone, and the
"already open" test is what keeps a persistent problem to one row instead of
one row per poll.

```mermaid
flowchart TD
    arrive["Sample for a device"] --> scope{"Rule enabled and<br>scope matches this device?"}
    scope -- no --> skip["Skip"]
    scope -- yes --> kind{"Metric is<br>InterfaceUtilization?"}
    kind -- yes --> perport["Evaluate once per interface"]
    kind -- no --> perdevice["Read the metric from the sample"]
    perport --> breach{"Outside the threshold?"}
    perdevice --> breach

    breach -- no --> clearclock["Clear the breach clock"]
    clearclock --> anyopen{"Open alert for this<br>rule and target?"}
    anyopen -- yes --> autoresolve["Resolve it, write a timeline<br>row, broadcast the change"]
    anyopen -- no --> nothing["Nothing to do"]

    breach -- yes --> duration{"Breaching for the<br>full duration?"}
    duration -- no --> waitpoll["Wait for the next poll"]
    duration -- yes --> exists{"Already open?"}
    exists -- yes --> bump["Raise occurrence count and last seen;<br>timeline row every tenth poll"]
    exists -- no --> raise["Insert the alert, notify the rule's<br>channels, broadcast AlertRaised"]
```

## The life of an alert

A partial unique index on `alerts` keeps one open row per device, rule and
interface, so two overlapping cycles cannot double-raise. Resolution has three
sources: the metric recovering, an operator, or the rule itself going away —
that last one matters because an alert from a deleted rule would otherwise
never clear.

```mermaid
stateDiagram-v2
    [*] --> Breaching
    Breaching --> [*] : recovers before the duration elapses
    Breaching --> Active : duration met, alert raised, channels notified

    Active --> Active : still breaching, occurrence count rises
    Active --> Acknowledged : operator takes it, with note and name
    Acknowledged --> Acknowledged : operator adds a comment

    Active --> Resolved : metric recovers, closed by the monitor
    Acknowledged --> Resolved : metric recovers, closed by the monitor
    Active --> Resolved : operator resolves
    Acknowledged --> Resolved : operator resolves
    Active --> Resolved : rule disabled or deleted
    Resolved --> [*]
```

## The data model

Inventory is small and relational; samples are append-only and large, so they
get composite primary keys and BRIN indexes on time rather than the usual
b-tree on a surrogate id. `alert_events` is the audit trail behind the
acknowledgment workflow — every raise, notification, comment and resolution.

```mermaid
erDiagram
    devices ||--o{ device_interfaces : "has ports"
    devices ||--o{ device_samples : "sampled into"
    devices ||--o{ links : "sits at either end of"
    device_interfaces ||--o{ interface_samples : "sampled into"
    device_interfaces ||--o{ links : "terminates"
    devices ||--o{ alerts : "subject of"
    alert_rules ||--o{ alerts : "fires"
    device_interfaces ||--o{ alerts : "scoped to"
    alerts ||--o{ alert_events : "timeline of"
    devices ||--o{ diagnostic_runs : "checked by"

    devices {
        uuid id PK
        text hostname UK
        inet ip_address
        text device_type "Router Switch Firewall AccessPoint Server Ups Printer"
        text status "Up Degraded Down Maintenance Unknown"
        int poll_interval_seconds "10 to 3600"
        boolean is_monitored
        text_array tags "rule targeting"
        bigint uptime_seconds
        timestamptz last_seen_at
    }

    device_interfaces {
        uuid id PK
        uuid device_id FK
        int if_index "unique per device"
        text name
        int speed_mbps "utilization denominator"
        boolean admin_up
        boolean oper_up
    }

    links {
        uuid id PK
        uuid a_device_id FK
        uuid a_interface_id FK
        uuid b_device_id FK
        uuid b_interface_id FK
        int capacity_mbps
        text link_type "Ethernet Fiber Wireless Virtual"
        text status "derived from both endpoints"
    }

    device_samples {
        uuid device_id PK
        timestamptz sampled_at PK
        boolean reachable
        double latency_ms
        double packet_loss_pct
        double cpu_pct
        double memory_pct
        double in_mbps
        double out_mbps
    }

    interface_samples {
        uuid interface_id PK
        timestamptz sampled_at PK
        double in_mbps
        double out_mbps
        double utilization_pct
        bigint in_errors
        bigint out_errors
    }

    alert_rules {
        uuid id PK
        text name
        text metric "Reachability Latency PacketLoss Cpu Memory InterfaceUtilization"
        text comparison "GreaterThan LessThan"
        double threshold
        int duration_seconds "must breach this long"
        text severity "Info Warning Major Critical"
        text target_kind "All Device DeviceType Tag Site"
        text target_value
        boolean enabled
        text_array notify_channels "inapp email webhook"
    }

    alerts {
        uuid id PK
        uuid rule_id FK
        uuid device_id FK
        uuid interface_id FK
        text severity
        text state "Active Acknowledged Resolved"
        text title
        double observed_value
        double threshold
        int occurrences "polls spent breaching"
        timestamptz raised_at
        timestamptz acknowledged_at
        text acknowledged_by
        timestamptz resolved_at
    }

    alert_events {
        uuid id PK
        uuid alert_id FK
        text kind "Raised Reoccurred Acknowledged Commented Escalated Resolved Notified"
        text message
        text actor
        timestamptz created_at
    }

    diagnostic_runs {
        uuid id PK
        uuid device_id FK
        text kind "Ping TcpCheck TraceRoute HealthCheck"
        text state "Running Completed Failed"
        boolean success
        text summary
        jsonb output "hops, checks, timings"
    }

    users {
        uuid id PK
        text email "unique on lower(email)"
        text display_name
        text password_hash "bcrypt"
        text role "admin operator viewer"
    }
```

## How the browser stays current

Query results are the source of truth for anything a page renders; hub events
only say what went stale. The one exception is the per-device sample stream,
which feeds a bounded in-memory buffer so the live chart can draw a point every
poll without touching the cache or the network.

```mermaid
flowchart LR
    hubevents["SignalR event"] --> kind{"Which event?"}
    kind -->|SampleRecorded| buffer["Bounded per-device buffer<br>useDeviceStream, drives the live chart"]
    kind -->|DeviceStatusChanged| i1["Invalidate devices, device, topology<br>toast on down and on recovery"]
    kind -->|AlertRaised| i2["Invalidate alerts, dashboard<br>toast coloured by severity"]
    kind -->|AlertChanged| i3["Invalidate alerts, dashboard"]
    kind -->|LinkStatusChanged| i4["Invalidate topology"]
    kind -->|CycleCompleted| i5["Update the header counters;<br>heavy aggregates at most every 20s"]

    i1 --> refetch["TanStack Query refetches<br>only what is mounted"]
    i2 --> refetch
    i3 --> refetch
    i4 --> refetch
    i5 --> refetch
    refetch --> render["Pages re-render"]
    buffer --> render
```

Only the device page subscribes to a sample stream, and it subscribes to one
device: the hub sends `SampleRecorded` to a per-device group, so a browser on
the alert queue never receives telemetry it would throw away.

## Authentication

A JWT carries the user id, name and role. The hub cannot use the same header a
fetch would, because browsers do not let you set headers on a WebSocket
handshake — so the token rides in the query string and `JwtBearerEvents` lifts
it back out for paths under `/hubs`.

```mermaid
sequenceDiagram
    participant UI as Browser
    participant API as ASP.NET Core
    participant DB as PostgreSQL

    UI->>API: POST /api/auth/login
    API->>DB: find user on lower(email)
    DB-->>API: row with bcrypt hash
    API->>API: verify password, issue JWT valid 12h
    API-->>UI: token, display name, role
    Note over UI: kept in localStorage,<br>in memory if storage is blocked
    UI->>API: Authorization Bearer on every request
    UI->>API: /hubs/monitoring?access_token=... upgrade
    API->>API: JwtBearerEvents reads the query string for /hubs
    API-->>UI: WebSocket open, groups joined on demand
```

Any signed-in user can view, run diagnostics and work the alert queue.
Inventory changes — devices, interfaces, links and rules — require the `admin`
role through the `ManageInventory` policy.

## The campus the seed data models

`db/seed.sql` builds a small but realistic campus so every feature has
something to show on first boot. Thick lines are 10G fiber, thin lines 1G
copper, dashed lines wireless. Tier is not stored: the API computes it as hop
distance from the routers and firewalls, and the UI lays the graph out in those
bands.

```mermaid
flowchart TD
    isp(["Internet"]) --- fw["Border Firewall<br>PA-3220"]
    fw === cra["Core Router A<br>ASR 1001-X"]
    cra === crb["Core Router B<br>ASR 1001-X"]

    cra === engd["Engineering Dist<br>EX4400"]
    cra === libd["Library Dist<br>C9300-48P"]
    crb === resd["Res Hall Dist<br>CX 6300M"]

    engd --- enga1["Engineering Acc 1"]
    engd --- enga2["Engineering Acc 2"]
    libd --- liba1["Library Acc 1"]
    resd -.- ap3["Res Hall AP 3F"]
    resd -.- ap4["Res Hall AP 4F"]

    crb --- auth["Auth Server"]
    crb --- dns["DNS / DHCP"]
    fw --- ups["DC Power UPS"]
```

Seven alert rules ship with it: unreachable devices, a latency spike rule
scoped to the `critical` tag, fleet-wide latency and packet loss, switch CPU,
memory pressure, and uplink congestion on any interface.

## Decisions worth knowing

**SQL-first schema.** Tables, constraints and the rollup view live in
`db/schema.sql` rather than in generated migrations. The interesting parts of
this schema are a partial unique index that keeps one alert open per target,
BRIN indexes on the sample tables, and filtered aggregates in the view — all
clearer as SQL than as C#. EF Core maps onto it for inventory and CRUD;
analytics are hand-written queries where bucketing and window functions do the
work.

**Breach timing is in memory.** "Above 85% for five minutes" needs to know when
the breach started. That lives in a `BreachTracker` rather than the database,
so a restart starts the clock over instead of resurrecting a stale breach.

**One probe interface, two implementations.** `IDeviceProbe` is either real
ICMP with a TCP fallback or a deterministic generator, chosen by config.
`IResourceSampler` is the same idea for counters and the seam where SNMP or
gNMI belongs; nothing above it changes when that arrives.

**One axis per chart.** Charts are hand-rolled SVG rather than a chart library:
it keeps every series on a single scale, the categorical order
colourblind-safe, and the hover layer consistent across the app.

## Monitoring real devices

Set `Monitoring__Mode=live` and the poller sends real ICMP echo requests,
falling back to a TCP connect on 443/22/80/161 when ICMP is unavailable.
Interface and resource counters still come from the synthetic sampler:
`IResourceSampler` is the seam where an SNMP or gNMI collector plugs in, and
nothing above it changes when it does.

Useful settings (`server/src/Nms.Api/appsettings.json`, or `__`-separated
environment variables):

| Setting | Default | What it does |
| --- | --- | --- |
| `Monitoring:Mode` | `synthetic` | `live` sends real probes |
| `Monitoring:TickSeconds` | `5` | How often the poller looks for due devices |
| `Monitoring:FailuresBeforeDown` | `2` | Missed polls before a device is marked down |
| `Monitoring:DegradedLatencyMs` | `150` | Latency that counts as degraded |
| `Monitoring:RetentionDays` | `30` | Sample retention; older rows are pruned hourly |
| `Notifications:WebhookUrl` | — | Alerts POST here when a rule asks for `webhook` |
| `Auth:SigningKey` | dev value | **Replace this.** JWT signing key |

## Project layout

```
db/            schema.sql and seed.sql (SQL-first; applied at startup)
server/        ASP.NET Core API (src/Nms.Api, tests/Nms.Api.Tests)
  Domain/      entities and the rules that belong to them
  Data/        EF Core mapping and the startup bootstrapper
  Monitoring/  probes, telemetry, the poll loop, topology, diagnostics
  Alerting/    rule evaluation, breach timing, notifications
  Reporting/   analytics SQL
  Endpoints/   minimal API surface
  Hubs/        SignalR hub and broadcaster
web/           React + TypeScript UI
  charts/      SVG chart primitives
  features/    dashboard, devices, topology, alerts, reports
  realtime/    hub client and live cache patching
```
