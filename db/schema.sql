-- Network Management System schema. Applied on container init and re-checked by
-- the API at startup, so every statement here must be idempotent.

create extension if not exists "uuid-ossp";
create extension if not exists citext;

create table if not exists users (
    id              uuid primary key default uuid_generate_v4(),
    email           citext not null unique,
    display_name    text not null,
    password_hash   text not null,
    role            text not null default 'operator' check (role in ('admin', 'operator', 'viewer')),
    created_at      timestamptz not null default now(),
    last_login_at   timestamptz
);

create table if not exists devices (
    id                    uuid primary key default uuid_generate_v4(),
    name                  text not null,
    hostname              text not null,
    ip_address            inet not null,
    device_type           text not null check (device_type in ('Router', 'Switch', 'Firewall', 'AccessPoint', 'Server', 'Ups', 'Printer')),
    vendor                text,
    model                 text,
    os_version            text,
    site                  text not null default 'Main Campus',
    building              text,
    room                  text,
    management_url        text,
    poll_interval_seconds int not null default 60 check (poll_interval_seconds between 10 and 3600),
    is_monitored          boolean not null default true,
    status                text not null default 'Unknown' check (status in ('Up', 'Degraded', 'Down', 'Maintenance', 'Unknown')),
    last_seen_at          timestamptz,
    status_changed_at     timestamptz,
    uptime_seconds        bigint not null default 0,
    tags                  text[] not null default '{}',
    notes                 text,
    created_at            timestamptz not null default now(),
    updated_at            timestamptz not null default now(),
    unique (hostname)
);

create index if not exists ix_devices_status on devices (status) where is_monitored;
create index if not exists ix_devices_site on devices (site);
create index if not exists ix_devices_tags on devices using gin (tags);

create table if not exists device_interfaces (
    id            uuid primary key default uuid_generate_v4(),
    device_id     uuid not null references devices (id) on delete cascade,
    if_index      int not null,
    name          text not null,
    description   text,
    speed_mbps    int not null default 1000,
    admin_up      boolean not null default true,
    oper_up       boolean not null default true,
    mac_address   text,
    created_at    timestamptz not null default now(),
    unique (device_id, if_index)
);

create table if not exists links (
    id              uuid primary key default uuid_generate_v4(),
    a_device_id     uuid not null references devices (id) on delete cascade,
    a_interface_id  uuid references device_interfaces (id) on delete set null,
    b_device_id     uuid not null references devices (id) on delete cascade,
    b_interface_id  uuid references device_interfaces (id) on delete set null,
    capacity_mbps   int not null default 1000,
    link_type       text not null default 'Ethernet' check (link_type in ('Ethernet', 'Fiber', 'Wireless', 'Virtual')),
    status          text not null default 'Unknown' check (status in ('Up', 'Degraded', 'Down', 'Unknown')),
    created_at      timestamptz not null default now(),
    check (a_device_id <> b_device_id)
);

create index if not exists ix_links_a_device on links (a_device_id);
create index if not exists ix_links_b_device on links (b_device_id);

create table if not exists device_samples (
    device_id        uuid not null references devices (id) on delete cascade,
    sampled_at       timestamptz not null,
    reachable        boolean not null,
    latency_ms       double precision,
    packet_loss_pct  double precision,
    cpu_pct          double precision,
    memory_pct       double precision,
    in_mbps          double precision,
    out_mbps         double precision,
    primary key (device_id, sampled_at)
);

create index if not exists ix_device_samples_time on device_samples using brin (sampled_at);

create table if not exists interface_samples (
    interface_id     uuid not null references device_interfaces (id) on delete cascade,
    sampled_at       timestamptz not null,
    in_mbps          double precision not null default 0,
    out_mbps         double precision not null default 0,
    in_errors        bigint not null default 0,
    out_errors       bigint not null default 0,
    utilization_pct  double precision not null default 0,
    primary key (interface_id, sampled_at)
);

create index if not exists ix_interface_samples_time on interface_samples using brin (sampled_at);

create table if not exists alert_rules (
    id               uuid primary key default uuid_generate_v4(),
    name             text not null,
    description      text,
    metric           text not null check (metric in ('Reachability', 'Latency', 'PacketLoss', 'Cpu', 'Memory', 'InterfaceUtilization')),
    comparison       text not null check (comparison in ('GreaterThan', 'LessThan')),
    threshold        double precision not null,
    duration_seconds int not null default 0,
    severity         text not null check (severity in ('Info', 'Warning', 'Major', 'Critical')),
    target_kind      text not null default 'All' check (target_kind in ('All', 'Device', 'DeviceType', 'Tag', 'Site')),
    target_value     text,
    enabled          boolean not null default true,
    notify_channels  text[] not null default '{inapp}',
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now()
);

create table if not exists alerts (
    id               uuid primary key default uuid_generate_v4(),
    rule_id          uuid references alert_rules (id) on delete set null,
    device_id        uuid not null references devices (id) on delete cascade,
    interface_id     uuid references device_interfaces (id) on delete set null,
    severity         text not null check (severity in ('Info', 'Warning', 'Major', 'Critical')),
    state            text not null default 'Active' check (state in ('Active', 'Acknowledged', 'Resolved')),
    title            text not null,
    detail           text,
    metric           text not null,
    observed_value   double precision,
    threshold        double precision,
    raised_at        timestamptz not null default now(),
    last_seen_at     timestamptz not null default now(),
    occurrences      int not null default 1,
    acknowledged_at  timestamptz,
    acknowledged_by  text,
    resolved_at      timestamptz,
    resolved_by      text
);

create index if not exists ix_alerts_open on alerts (state, severity, raised_at desc);
create index if not exists ix_alerts_device on alerts (device_id, raised_at desc);
-- One open alert per rule/device/interface pair keeps flapping from spamming the queue.
create unique index if not exists ux_alerts_open_target
    on alerts (device_id, rule_id, coalesce(interface_id, '00000000-0000-0000-0000-000000000000'::uuid))
    where state <> 'Resolved';

create table if not exists alert_events (
    id         uuid primary key default uuid_generate_v4(),
    alert_id   uuid not null references alerts (id) on delete cascade,
    kind       text not null check (kind in ('Raised', 'Reoccurred', 'Acknowledged', 'Commented', 'Escalated', 'Resolved', 'Notified')),
    message    text,
    actor      text,
    created_at timestamptz not null default now()
);

create index if not exists ix_alert_events_alert on alert_events (alert_id, created_at);

create table if not exists diagnostic_runs (
    id            uuid primary key default uuid_generate_v4(),
    device_id     uuid not null references devices (id) on delete cascade,
    kind          text not null check (kind in ('Ping', 'TcpCheck', 'TraceRoute', 'HealthCheck')),
    state         text not null default 'Running' check (state in ('Running', 'Completed', 'Failed')),
    requested_by  text,
    started_at    timestamptz not null default now(),
    completed_at  timestamptz,
    success       boolean,
    summary       text,
    output        jsonb not null default '{}'::jsonb
);

create index if not exists ix_diagnostic_runs_device on diagnostic_runs (device_id, started_at desc);

create or replace view device_availability_daily as
select device_id,
       date_trunc('day', sampled_at) as day,
       count(*)                                           as samples,
       count(*) filter (where reachable)                   as reachable_samples,
       round(100.0 * count(*) filter (where reachable) / nullif(count(*), 0), 3) as availability_pct,
       avg(latency_ms) filter (where reachable)            as avg_latency_ms,
       max(latency_ms)                                     as peak_latency_ms
from device_samples
group by device_id, date_trunc('day', sampled_at);

create or replace view interface_utilization_hourly as
select i.id          as interface_id,
       i.device_id,
       date_trunc('hour', s.sampled_at) as hour,
       avg(s.utilization_pct) as avg_utilization_pct,
       max(s.utilization_pct) as peak_utilization_pct,
       avg(s.in_mbps)         as avg_in_mbps,
       avg(s.out_mbps)        as avg_out_mbps,
       sum(s.in_errors + s.out_errors) as errors
from interface_samples s
join device_interfaces i on i.id = s.interface_id
group by i.id, i.device_id, date_trunc('hour', s.sampled_at);
