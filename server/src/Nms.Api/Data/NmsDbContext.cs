using Microsoft.EntityFrameworkCore;
using Nms.Api.Domain;

namespace Nms.Api.Data;

public class NmsDbContext(DbContextOptions<NmsDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceInterface> Interfaces => Set<DeviceInterface>();
    public DbSet<Link> Links => Set<Link>();
    public DbSet<DeviceSample> DeviceSamples => Set<DeviceSample>();
    public DbSet<InterfaceSample> InterfaceSamples => Set<InterfaceSample>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<DiagnosticRun> DiagnosticRuns => Set<DiagnosticRun>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DailyAvailability> DailyAvailability => Set<DailyAvailability>();
    public DbSet<HourlyInterfaceUtilization> HourlyInterfaceUtilization => Set<HourlyInterfaceUtilization>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.Hostname).IsUnique();
            e.Property(d => d.DeviceType).HasConversion<string>();
            e.Property(d => d.Status).HasConversion<string>();
            e.Property(d => d.CreatedAt).HasDefaultValueSql("now()");
            e.Property(d => d.UpdatedAt).HasDefaultValueSql("now()");
            e.HasMany(d => d.Interfaces).WithOne(i => i.Device!).HasForeignKey(i => i.DeviceId);
        });

        model.Entity<DeviceInterface>(e =>
        {
            e.ToTable("device_interfaces");
            e.HasKey(i => i.Id);
            e.HasIndex(i => new { i.DeviceId, i.IfIndex }).IsUnique();
            e.Property(i => i.CreatedAt).HasDefaultValueSql("now()");
        });

        model.Entity<Link>(e =>
        {
            e.ToTable("links");
            e.HasKey(l => l.Id);
            e.Property(l => l.LinkType).HasConversion<string>();
            e.Property(l => l.Status).HasConversion<string>();
            e.Property(l => l.CreatedAt).HasDefaultValueSql("now()");
            e.HasOne(l => l.ADevice).WithMany().HasForeignKey(l => l.ADeviceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.BDevice).WithMany().HasForeignKey(l => l.BDeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<DeviceSample>(e =>
        {
            e.ToTable("device_samples");
            e.HasKey(s => new { s.DeviceId, s.SampledAt });
        });

        model.Entity<InterfaceSample>(e =>
        {
            e.ToTable("interface_samples");
            e.HasKey(s => new { s.InterfaceId, s.SampledAt });
        });

        model.Entity<AlertRule>(e =>
        {
            e.ToTable("alert_rules");
            e.HasKey(r => r.Id);
            e.Property(r => r.Metric).HasConversion<string>();
            e.Property(r => r.Comparison).HasConversion<string>();
            e.Property(r => r.Severity).HasConversion<string>();
            e.Property(r => r.TargetKind).HasConversion<string>();
            e.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
            e.Property(r => r.UpdatedAt).HasDefaultValueSql("now()");
        });

        model.Entity<Alert>(e =>
        {
            e.ToTable("alerts");
            e.HasKey(a => a.Id);
            e.Property(a => a.Severity).HasConversion<string>();
            e.Property(a => a.State).HasConversion<string>();
            e.Property(a => a.RaisedAt).HasDefaultValueSql("now()");
            e.Property(a => a.LastSeenAt).HasDefaultValueSql("now()");
            e.HasOne(a => a.Device).WithMany().HasForeignKey(a => a.DeviceId);
            e.HasOne(a => a.Rule).WithMany().HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(a => a.Events).WithOne().HasForeignKey(ev => ev.AlertId);
        });

        model.Entity<AlertEvent>(e =>
        {
            e.ToTable("alert_events");
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Kind).HasConversion<string>();
            e.Property(ev => ev.CreatedAt).HasDefaultValueSql("now()");
        });

        model.Entity<DiagnosticRun>(e =>
        {
            e.ToTable("diagnostic_runs");
            e.HasKey(d => d.Id);
            e.Property(d => d.Kind).HasConversion<string>();
            e.Property(d => d.State).HasConversion<string>();
            e.Property(d => d.Output).HasColumnType("jsonb");
            e.Property(d => d.StartedAt).HasDefaultValueSql("now()");
            e.HasOne(d => d.Device).WithMany().HasForeignKey(d => d.DeviceId);
        });

        model.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            // The column is citext so uniqueness and lookups ignore case; EF
            // sends it as plain text and lets Postgres do the comparison.
            e.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        });

        model.Entity<DailyAvailability>().ToView("device_availability_daily").HasNoKey();
        model.Entity<HourlyInterfaceUtilization>().ToView("interface_utilization_hourly").HasNoKey();

        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName();
                if (column is not null)
                {
                    property.SetColumnName(ToSnakeCase(column));
                }
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && !char.IsUpper(name[i + 1]))))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
