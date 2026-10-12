-- Reference inventory for a small university campus: two core routers, a border
-- firewall, per-building distribution/access switches, wireless and a few servers.
-- Safe to re-run; everything keys off fixed ids.

insert into devices (id, name, hostname, ip_address, device_type, vendor, model, os_version, site, building, room, poll_interval_seconds, tags)
values
  ('11111111-0000-0000-0000-000000000001', 'Core Router A',      'core-rtr-a.campus.edu',  '10.0.0.1',   'Router',      'Cisco',   'ASR 1001-X', 'IOS-XE 17.9',  'Main Campus', 'Data Center',     'DC-110', 30, '{core,critical}'),
  ('11111111-0000-0000-0000-000000000002', 'Core Router B',      'core-rtr-b.campus.edu',  '10.0.0.2',   'Router',      'Cisco',   'ASR 1001-X', 'IOS-XE 17.9',  'Main Campus', 'Data Center',     'DC-110', 30, '{core,critical}'),
  ('11111111-0000-0000-0000-000000000003', 'Border Firewall',    'fw-edge-01.campus.edu',  '10.0.0.3',   'Firewall',    'Palo Alto', 'PA-3220',  'PAN-OS 11.1',  'Main Campus', 'Data Center',     'DC-112', 30, '{edge,critical}'),
  ('11111111-0000-0000-0000-000000000010', 'Engineering Dist',   'eng-dist-01.campus.edu', '10.10.1.1',  'Switch',      'Juniper', 'EX4400',     'Junos 23.2',   'Main Campus', 'Engineering',     '140',    60, '{distribution}'),
  ('11111111-0000-0000-0000-000000000011', 'Engineering Acc 1',  'eng-acc-01.campus.edu',  '10.10.1.11', 'Switch',      'Juniper', 'EX2300',     'Junos 23.2',   'Main Campus', 'Engineering',     '212',    60, '{access}'),
  ('11111111-0000-0000-0000-000000000012', 'Engineering Acc 2',  'eng-acc-02.campus.edu',  '10.10.1.12', 'Switch',      'Juniper', 'EX2300',     'Junos 23.2',   'Main Campus', 'Engineering',     '318',    60, '{access}'),
  ('11111111-0000-0000-0000-000000000020', 'Library Dist',       'lib-dist-01.campus.edu', '10.10.2.1',  'Switch',      'Cisco',   'C9300-48P',  'IOS-XE 17.12', 'Main Campus', 'Library',         'B10',    60, '{distribution}'),
  ('11111111-0000-0000-0000-000000000021', 'Library Acc 1',      'lib-acc-01.campus.edu',  '10.10.2.11', 'Switch',      'Cisco',   'C9200-24P',  'IOS-XE 17.12', 'Main Campus', 'Library',         '201',    60, '{access}'),
  ('11111111-0000-0000-0000-000000000030', 'Res Hall Dist',      'res-dist-01.campus.edu', '10.10.3.1',  'Switch',      'Aruba',   'CX 6300M',   'AOS-CX 10.13', 'Main Campus', 'Wallace Complex', '05',     60, '{distribution,residential}'),
  ('11111111-0000-0000-0000-000000000031', 'Res Hall AP 3F',     'res-ap-3f.campus.edu',   '10.10.3.31', 'AccessPoint', 'Aruba',   'AP-635',     'AOS 10.5',     'Main Campus', 'Wallace Complex', '3F',    120, '{wireless,residential}'),
  ('11111111-0000-0000-0000-000000000032', 'Res Hall AP 4F',     'res-ap-4f.campus.edu',   '10.10.3.32', 'AccessPoint', 'Aruba',   'AP-635',     'AOS 10.5',     'Main Campus', 'Wallace Complex', '4F',    120, '{wireless,residential}'),
  ('11111111-0000-0000-0000-000000000040', 'Auth Server',        'auth-01.campus.edu',     '10.20.0.10', 'Server',      'Dell',    'R650',       'Ubuntu 24.04', 'Main Campus', 'Data Center',     'DC-120', 60, '{server,identity,critical}'),
  ('11111111-0000-0000-0000-000000000041', 'DNS / DHCP',         'dns-01.campus.edu',      '10.20.0.11', 'Server',      'Dell',    'R650',       'Ubuntu 24.04', 'Main Campus', 'Data Center',     'DC-120', 60, '{server,critical}'),
  ('11111111-0000-0000-0000-000000000042', 'DC Power UPS',       'ups-dc-01.campus.edu',   '10.20.0.50', 'Ups',         'APC',     'Symmetra PX','5.2',          'Main Campus', 'Data Center',     'DC-101', 300, '{facilities}')
on conflict (id) do nothing;

insert into device_interfaces (id, device_id, if_index, name, description, speed_mbps)
values
  ('22222222-0000-0000-0000-000000000001', '11111111-0000-0000-0000-000000000001', 1, 'TenGigE0/0/0', 'Core A to Core B',        10000),
  ('22222222-0000-0000-0000-000000000002', '11111111-0000-0000-0000-000000000001', 2, 'TenGigE0/0/1', 'Core A to Firewall',      10000),
  ('22222222-0000-0000-0000-000000000003', '11111111-0000-0000-0000-000000000001', 3, 'TenGigE0/0/2', 'Core A to Engineering',   10000),
  ('22222222-0000-0000-0000-000000000004', '11111111-0000-0000-0000-000000000001', 4, 'TenGigE0/0/3', 'Core A to Library',       10000),
  ('22222222-0000-0000-0000-000000000005', '11111111-0000-0000-0000-000000000002', 1, 'TenGigE0/0/0', 'Core B to Core A',        10000),
  ('22222222-0000-0000-0000-000000000006', '11111111-0000-0000-0000-000000000002', 2, 'TenGigE0/0/1', 'Core B to Res Halls',     10000),
  ('22222222-0000-0000-0000-000000000007', '11111111-0000-0000-0000-000000000002', 3, 'TenGigE0/0/2', 'Core B to Server VLAN',   10000),
  ('22222222-0000-0000-0000-000000000008', '11111111-0000-0000-0000-000000000003', 1, 'ethernet1/1',  'Firewall uplink to ISP',  10000),
  ('22222222-0000-0000-0000-000000000010', '11111111-0000-0000-0000-000000000010', 1, 'xe-0/0/0',     'Engineering uplink',      10000),
  ('22222222-0000-0000-0000-000000000011', '11111111-0000-0000-0000-000000000010', 2, 'ge-0/0/1',     'To Engineering Acc 1',     1000),
  ('22222222-0000-0000-0000-000000000012', '11111111-0000-0000-0000-000000000010', 3, 'ge-0/0/2',     'To Engineering Acc 2',     1000),
  ('22222222-0000-0000-0000-000000000020', '11111111-0000-0000-0000-000000000020', 1, 'Te1/1/1',      'Library uplink',          10000),
  ('22222222-0000-0000-0000-000000000021', '11111111-0000-0000-0000-000000000020', 2, 'Gi1/0/1',      'To Library Acc 1',         1000),
  ('22222222-0000-0000-0000-000000000030', '11111111-0000-0000-0000-000000000030', 1, '1/1/49',       'Res Hall uplink',         10000),
  ('22222222-0000-0000-0000-000000000031', '11111111-0000-0000-0000-000000000030', 2, '1/1/5',        'To AP 3F',                 1000),
  ('22222222-0000-0000-0000-000000000032', '11111111-0000-0000-0000-000000000030', 3, '1/1/6',        'To AP 4F',                 1000),
  ('22222222-0000-0000-0000-000000000040', '11111111-0000-0000-0000-000000000040', 1, 'eno1',         'Auth server NIC',          1000),
  ('22222222-0000-0000-0000-000000000041', '11111111-0000-0000-0000-000000000041', 1, 'eno1',         'DNS server NIC',           1000)
on conflict (id) do nothing;

insert into links (id, a_device_id, a_interface_id, b_device_id, b_interface_id, capacity_mbps, link_type)
values
  ('33333333-0000-0000-0000-000000000001', '11111111-0000-0000-0000-000000000001', '22222222-0000-0000-0000-000000000001', '11111111-0000-0000-0000-000000000002', '22222222-0000-0000-0000-000000000005', 10000, 'Fiber'),
  ('33333333-0000-0000-0000-000000000002', '11111111-0000-0000-0000-000000000001', '22222222-0000-0000-0000-000000000002', '11111111-0000-0000-0000-000000000003', '22222222-0000-0000-0000-000000000008', 10000, 'Fiber'),
  ('33333333-0000-0000-0000-000000000003', '11111111-0000-0000-0000-000000000001', '22222222-0000-0000-0000-000000000003', '11111111-0000-0000-0000-000000000010', '22222222-0000-0000-0000-000000000010', 10000, 'Fiber'),
  ('33333333-0000-0000-0000-000000000004', '11111111-0000-0000-0000-000000000001', '22222222-0000-0000-0000-000000000004', '11111111-0000-0000-0000-000000000020', '22222222-0000-0000-0000-000000000020', 10000, 'Fiber'),
  ('33333333-0000-0000-0000-000000000005', '11111111-0000-0000-0000-000000000002', '22222222-0000-0000-0000-000000000006', '11111111-0000-0000-0000-000000000030', '22222222-0000-0000-0000-000000000030', 10000, 'Fiber'),
  ('33333333-0000-0000-0000-000000000006', '11111111-0000-0000-0000-000000000010', '22222222-0000-0000-0000-000000000011', '11111111-0000-0000-0000-000000000011', null, 1000, 'Ethernet'),
  ('33333333-0000-0000-0000-000000000007', '11111111-0000-0000-0000-000000000010', '22222222-0000-0000-0000-000000000012', '11111111-0000-0000-0000-000000000012', null, 1000, 'Ethernet'),
  ('33333333-0000-0000-0000-000000000008', '11111111-0000-0000-0000-000000000020', '22222222-0000-0000-0000-000000000021', '11111111-0000-0000-0000-000000000021', null, 1000, 'Ethernet'),
  ('33333333-0000-0000-0000-000000000009', '11111111-0000-0000-0000-000000000030', '22222222-0000-0000-0000-000000000031', '11111111-0000-0000-0000-000000000031', null, 1000, 'Wireless'),
  ('33333333-0000-0000-0000-00000000000a', '11111111-0000-0000-0000-000000000030', '22222222-0000-0000-0000-000000000032', '11111111-0000-0000-0000-000000000032', null, 1000, 'Wireless'),
  ('33333333-0000-0000-0000-00000000000b', '11111111-0000-0000-0000-000000000002', '22222222-0000-0000-0000-000000000007', '11111111-0000-0000-0000-000000000040', '22222222-0000-0000-0000-000000000040', 1000, 'Ethernet'),
  ('33333333-0000-0000-0000-00000000000c', '11111111-0000-0000-0000-000000000002', null,                                   '11111111-0000-0000-0000-000000000041', '22222222-0000-0000-0000-000000000041', 1000, 'Ethernet'),
  ('33333333-0000-0000-0000-00000000000d', '11111111-0000-0000-0000-000000000003', null,                                   '11111111-0000-0000-0000-000000000042', null, 1000, 'Ethernet')
on conflict (id) do nothing;

insert into alert_rules (id, name, description, metric, comparison, threshold, duration_seconds, severity, target_kind, target_value, notify_channels)
values
  ('44444444-0000-0000-0000-000000000001', 'Device unreachable',          'No ICMP response for two consecutive polls.',       'Reachability',          'LessThan',    1,  60,  'Critical', 'All',        null,       '{inapp,email}'),
  ('44444444-0000-0000-0000-000000000002', 'Core device unreachable',     'Any core or edge device stops responding.',         'Reachability',          'LessThan',    1,  0,   'Critical', 'Tag',        'critical', '{inapp,email,webhook}'),
  ('44444444-0000-0000-0000-000000000003', 'High latency',                'Round trip above 120 ms for two minutes.',          'Latency',               'GreaterThan', 120, 120, 'Warning',  'All',        null,       '{inapp}'),
  ('44444444-0000-0000-0000-000000000004', 'Packet loss',                 'Sustained loss above 2%.',                          'PacketLoss',            'GreaterThan', 2,   120, 'Major',    'All',        null,       '{inapp,email}'),
  ('44444444-0000-0000-0000-000000000005', 'Switch CPU saturated',        'CPU above 85% for five minutes.',                   'Cpu',                   'GreaterThan', 85,  300, 'Major',    'DeviceType', 'Switch',   '{inapp}'),
  ('44444444-0000-0000-0000-000000000006', 'Memory pressure',             'Memory above 90% for five minutes.',                'Memory',                'GreaterThan', 90,  300, 'Warning',  'All',        null,       '{inapp}'),
  ('44444444-0000-0000-0000-000000000007', 'Uplink congestion',           'Interface utilization above 80% for three minutes.','InterfaceUtilization',  'GreaterThan', 80,  180, 'Warning',  'All',        null,       '{inapp}')
on conflict (id) do nothing;
