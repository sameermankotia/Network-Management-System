using System.Data;
using Microsoft.EntityFrameworkCore;
using Nms.Api.Domain;

namespace Nms.Api.Data;

/// Applies schema.sql / seed.sql on startup so a fresh checkout boots against an
/// empty Postgres without a migration step.
public class DatabaseBootstrapper(
    NmsDbContext db,
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<DatabaseBootstrapper> log)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await WaitForDatabaseAsync(ct);

        var sqlRoot = ResolveSqlRoot();
        await ExecuteFileAsync(Path.Combine(sqlRoot, "schema.sql"), ct);

        if (config.GetValue("Database:SeedInventory", env.IsDevelopment()))
        {
            await ExecuteFileAsync(Path.Combine(sqlRoot, "seed.sql"), ct);
        }

        await EnsureAdminAsync(ct);
    }

    private async Task WaitForDatabaseAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 15; attempt++)
        {
            if (await db.Database.CanConnectAsync(ct))
            {
                return;
            }

            log.LogInformation("Waiting for Postgres (attempt {Attempt})", attempt);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new InvalidOperationException("Database did not become available in time.");
    }

    private async Task ExecuteFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            log.LogWarning("SQL file {Path} not found, skipping", path);
            return;
        }

        var sql = await File.ReadAllTextAsync(path, ct);

        // Straight ADO rather than ExecuteSqlRaw: these scripts contain braces
        // (array and jsonb literals) that EF would try to treat as format
        // placeholders, and Npgsql runs the whole batch in one round trip.
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);

        log.LogInformation("Applied {File}", Path.GetFileName(path));
    }

    private async Task EnsureAdminAsync(CancellationToken ct)
    {
        var email = (config["Admin:Email"] ?? "admin@campus.edu").Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email, ct))
        {
            return;
        }

        var password = config["Admin:Password"] ?? "ChangeMe123!";
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = config["Admin:DisplayName"] ?? "Network Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "admin"
        });

        await db.SaveChangesAsync(ct);
        log.LogInformation("Created initial admin account {Email}", email);
    }

    private string ResolveSqlRoot()
    {
        var configured = config["Database:SqlDirectory"];
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "db");
            if (File.Exists(Path.Combine(candidate, "schema.sql")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "db");
    }
}
