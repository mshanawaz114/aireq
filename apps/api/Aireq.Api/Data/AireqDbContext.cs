// AireqDbContext — the root EF Core unit-of-work.
//
// Responsibilities here:
//   - Expose DbSets for every domain entity.
//   - Enable the pgvector extension on first migration.
//   - Apply snake_case naming to all tables, columns, keys, indexes
//     (hand-rolled in ApplySnakeCaseNaming — see end of OnModelCreating).
//   - Map jsonb columns explicitly.
//   - Map enums to strings (so DB rows are human-readable in pgAdmin).
//   - Map Pgvector.Vector to vector(1536) columns.
//   - Auto-set CreatedAt / UpdatedAt for ITimestamped entities on SaveChanges.
//   - Apply soft-delete query filter for ISoftDelete entities.
//
// What's intentionally NOT here yet:
//   - Multi-tenant query filter (lands in AIRMVP1-103 once auth gives us a
//     tenant_id to filter on).
//
// Refs: AIRMVP1-102

using Aireq.Api.Data.Common;
using Aireq.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

// Aliased to avoid collision with our Match entity — System.Text.RegularExpressions
// exports its own Match class, and a wildcard import would make "Match" ambiguous.
using Regex = System.Text.RegularExpressions.Regex;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Aireq.Api.Data;

public sealed class AireqDbContext(DbContextOptions<AireqDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Consultant> Consultants => Set<Consultant>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<ConsultantSkill> ConsultantSkills => Set<ConsultantSkill>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<TailoredResume> TailoredResumes => Set<TailoredResume>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<RecruiterThread> RecruiterThreads => Set<RecruiterThread>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Escalation> Escalations => Set<Escalation>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Extensions.
        mb.HasPostgresExtension("vector");

        // ---- Tenant ----
        mb.Entity<Tenant>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(120);
            b.Property(x => x.Plan).IsRequired().HasMaxLength(32);
            b.HasIndex(x => x.Name).IsUnique();
        });

        // ---- User ----
        mb.Entity<User>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).IsRequired().HasMaxLength(254);
            b.Property(x => x.Role).IsRequired().HasMaxLength(16);
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.HasOne(x => x.Tenant).WithMany(t => t.Users).HasForeignKey(x => x.TenantId);
        });

        // ---- Consultant ----
        mb.Entity<Consultant>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            b.Property(x => x.Headline).HasMaxLength(300);
            b.Property(x => x.Location).HasMaxLength(120);
            b.Property(x => x.WorkAuth).HasMaxLength(64);
            b.Property(x => x.RateTargetUsdHourly).HasPrecision(10, 2);
            b.HasIndex(x => x.TenantId);
            b.HasOne(x => x.Tenant).WithMany(t => t.Consultants).HasForeignKey(x => x.TenantId);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        // ---- Resume ----
        mb.Entity<Resume>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ParsedJson).HasColumnType("jsonb");
            b.Property(x => x.Embedding).HasColumnType("vector(1536)");
            b.Property(x => x.OriginalFilename).HasMaxLength(255);
            b.HasIndex(x => new { x.ConsultantId, x.Version }).IsUnique();
            b.HasOne(x => x.Consultant).WithMany(c => c.Resumes).HasForeignKey(x => x.ConsultantId);
        });

        // ---- Skill ----
        mb.Entity<Skill>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(120);
            b.Property(x => x.Slug).IsRequired().HasMaxLength(120);
            b.Property(x => x.Category).HasMaxLength(32);
            b.HasIndex(x => x.Slug).IsUnique();
        });

        // ---- ConsultantSkill (composite key) ----
        mb.Entity<ConsultantSkill>(b =>
        {
            b.HasKey(x => new { x.ConsultantId, x.SkillId });
            b.Property(x => x.Years).HasPrecision(4, 1);
            b.Property(x => x.Evidence).HasMaxLength(1000);
            b.HasOne(x => x.Consultant).WithMany(c => c.Skills).HasForeignKey(x => x.ConsultantId);
            b.HasOne(x => x.Skill).WithMany(s => s.Consultants).HasForeignKey(x => x.SkillId);
        });

        // ---- Job ----
        mb.Entity<Job>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Source).IsRequired().HasMaxLength(32);
            b.Property(x => x.SourceExternalId).IsRequired().HasMaxLength(256);
            b.Property(x => x.Title).IsRequired().HasMaxLength(300);
            b.Property(x => x.Company).IsRequired().HasMaxLength(200);
            b.Property(x => x.Location).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(50_000);
            b.Property(x => x.RawJson).HasColumnType("jsonb");
            b.Property(x => x.Embedding).HasColumnType("vector(1536)");
            b.HasIndex(x => new { x.Source, x.SourceExternalId }).IsUnique();
            b.HasIndex(x => x.PostedAt);
            b.HasIndex(x => x.IsActive);
        });

        // ---- Match ----
        mb.Entity<Match>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ReasoningJson).HasColumnType("jsonb");
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasIndex(x => new { x.ConsultantId, x.JobId }).IsUnique();
            b.HasOne(x => x.Tenant).WithMany(t => t.Matches).HasForeignKey(x => x.TenantId);
            b.HasOne(x => x.Consultant).WithMany(c => c.Matches).HasForeignKey(x => x.ConsultantId);
            b.HasOne(x => x.Job).WithMany(j => j.Matches).HasForeignKey(x => x.JobId);
        });

        // ---- TailoredResume ----
        mb.Entity<TailoredResume>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.DiffJson).HasColumnType("jsonb");
            b.HasOne(x => x.Match).WithMany(m => m.TailoredResumes).HasForeignKey(x => x.MatchId);
        });

        // ---- Submission ----
        mb.Entity<Submission>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.ResponseStatus).HasMaxLength(32);
            b.Property(x => x.ResponsePayloadJson).HasColumnType("jsonb");
            b.HasIndex(x => x.SubmittedAt);
            b.HasOne(x => x.Match).WithMany(m => m.Submissions).HasForeignKey(x => x.MatchId);
        });

        // ---- RecruiterThread ----
        mb.Entity<RecruiterThread>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.RecruiterEmail).IsRequired().HasMaxLength(254);
            b.Property(x => x.RecruiterName).HasMaxLength(200);
            b.Property(x => x.Sentiment).HasMaxLength(16);
            b.HasIndex(x => x.RecruiterEmail);
            b.HasIndex(x => new { x.MatchId, x.RecruiterEmail }).IsUnique();
            b.HasOne(x => x.Match).WithMany(m => m.Threads).HasForeignKey(x => x.MatchId);
        });

        // ---- Message ----
        mb.Entity<Message>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Subject).HasMaxLength(500);
            b.Property(x => x.Body).IsRequired().HasMaxLength(50_000);
            b.Property(x => x.AiModel).HasMaxLength(64);
            b.Property(x => x.PromptHash).HasMaxLength(64);
            b.HasIndex(x => new { x.ThreadId, x.SentAt });
            b.HasOne(x => x.Thread).WithMany(t => t.Messages).HasForeignKey(x => x.ThreadId);
        });

        // ---- Escalation ----
        mb.Entity<Escalation>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).IsRequired().HasMaxLength(64);
            b.Property(x => x.Summary).HasMaxLength(500);
            b.HasIndex(x => new { x.MatchId, x.CreatedAt });
            b.HasIndex(x => x.ResolvedAt);
            b.HasOne(x => x.Match).WithMany(m => m.Escalations).HasForeignKey(x => x.MatchId);
        });

        ApplySnakeCaseNaming(mb);

        base.OnModelCreating(mb);
    }

    /// <summary>
    /// Renames all tables, columns, keys, indexes, and FK constraints to
    /// snake_case. Replaces what EFCore.NamingConventions used to do for us
    /// — we hand-roll it because that package lags EF Core 10 as of May 2026.
    /// </summary>
    private static void ApplySnakeCaseNaming(ModelBuilder mb)
    {
        foreach (var entity in mb.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName();
                if (columnName is not null)
                {
                    property.SetColumnName(ToSnakeCase(columnName));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                var name = key.GetName();
                if (name is not null)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                var name = fk.GetConstraintName();
                if (name is not null)
                {
                    fk.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                var name = index.GetDatabaseName();
                if (name is not null)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    private static readonly Regex SnakeCaseBoundary =
        new("([a-z0-9])([A-Z])", RegexOptions.Compiled);

    private static string ToSnakeCase(string input) =>
        string.IsNullOrEmpty(input)
            ? input
            : SnakeCaseBoundary.Replace(input, "$1_$2").ToLowerInvariant();

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(nameof(ITimestamped.CreatedAt)).IsModified = false;
            }
        }
    }
}
