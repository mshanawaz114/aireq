// AireqDbContext — placeholder for AIRMVP1-101.
// The real domain model + multi-tenant query filters land in AIRMVP1-102 / 103.
// Refs: AIRMVP1-101

using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Data;

public sealed class AireqDbContext(DbContextOptions<AireqDbContext> options) : DbContext(options)
{
    // Placeholder set — replaced by real entities in AIRMVP1-102.
    public DbSet<HealthPing> HealthPings => Set<HealthPing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<HealthPing>(b =>
        {
            b.ToTable("health_pings");
            b.HasKey(x => x.Id);
            b.Property(x => x.At).HasDefaultValueSql("now()");
        });
    }
}

/// <summary>
/// Sentinel row inserted on startup to prove DB connectivity.
/// Real entities replace this in AIRMVP1-102.
/// </summary>
public sealed record HealthPing(Guid Id, DateTimeOffset At);
