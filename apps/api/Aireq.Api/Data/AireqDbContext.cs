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
//   - **Apply multi-tenant query filter on User, Consultant, Match** so that
//     every query is automatically scoped to the current ITenantContext.TenantId.
//     Auth endpoints and admin reads use .IgnoreQueryFilters() explicitly.
//
// Refs: AIRMVP1-102, AIRMVP1-103

using Aireq.Api.Auth;
using Aireq.Api.Data.Common;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;

// Aliased to avoid collision with our Match entity — System.Text.RegularExpressions
// exports its own Match class, and a wildcard import would make "Match" ambiguous.
using Regex = System.Text.RegularExpressions.Regex;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace Aireq.Api.Data;

public sealed class AireqDbContext(
    DbContextOptions<AireqDbContext> options,
    ITenantContext? tenantContext = null) : DbContext(options)
{
    // ITenantContext may be null in design-time tooling (dotnet ef) where no
    // request scope exists. In that case the query filter passes all rows
    // through — design-time only ever reads schema, never user data.
    private readonly ITenantContext? _tenant = tenantContext;

    /// <summary>
    /// Current tenant id, surfaced as a DbContext property so EF Core's query
    /// compiler treats it as a query parameter and **re-evaluates per query**.
    /// If we referenced <c>_tenant.TenantId</c> directly inside query filters,
    /// EF would parameterize the chain off the first observed value and cache
    /// it for the lifetime of the DbContext — so any mid-context tenant switch
    /// (the test pattern in <c>QueryFilterTests</c>) would silently return
    /// stale-tenant rows.
    /// </summary>
    public Guid? CurrentTenantId => _tenant?.TenantId;

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
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<GmailAccount> GmailAccounts => Set<GmailAccount>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<BillingSubscription> BillingSubscriptions => Set<BillingSubscription>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Provider-aware config: Postgres-specific bits (jsonb, vector(1536),
        // pgvector extension) are skipped for non-Npgsql providers so unit
        // tests can run against EF InMemory.
        var isNpgsql = this.Database.IsNpgsql();

        // Extensions.
        if (isNpgsql)
        {
            mb.HasPostgresExtension("vector");
        }

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
            b.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512);
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.Role).IsRequired().HasMaxLength(16);
            b.HasIndex(x => x.Email).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Email });
            b.HasOne(x => x.Tenant).WithMany(t => t.Users).HasForeignKey(x => x.TenantId);
            // Tenant-scoped read. Auth endpoints (signup / login) deliberately
            // bypass this with .IgnoreQueryFilters() since they run before the
            // tenant context exists. Uses the CurrentTenantId property so EF
            // re-evaluates per query (see property doc on AireqDbContext).
            b.HasQueryFilter(x =>
                CurrentTenantId == null || x.TenantId == CurrentTenantId);
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
            // Combined filter: not-soft-deleted AND tenant matches current request.
            b.HasQueryFilter(x =>
                x.DeletedAt == null
                && (CurrentTenantId == null || x.TenantId == CurrentTenantId));
        });

        // ---- Resume ----
        mb.Entity<Resume>(b =>
        {
            b.HasKey(x => x.Id);
            if (isNpgsql)
            {
                b.Property(x => x.ParsedJson).HasColumnType("jsonb");
                b.Property(x => x.Embedding).HasColumnType($"vector({EmbeddingConfig.Dimensions})");
            }
            else
            {
                // InMemory / SQLite test providers can't map Pgvector.Vector.
                // Tests don't exercise vector search; safe to drop the column.
                b.Ignore(x => x.Embedding);
            }
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
            if (isNpgsql)
            {
                b.Property(x => x.RawJson).HasColumnType("jsonb");
                b.Property(x => x.Embedding).HasColumnType($"vector({EmbeddingConfig.Dimensions})");
                // Backfill existing rows + any insert that forgets to set it with
                // now(), so they aren't immediately treated as stale. (AIRMVP1-203)
                b.Property(x => x.LastSeenAt).HasDefaultValueSql("now()");
            }
            else
            {
                // InMemory / SQLite test providers can't map Pgvector.Vector.
                b.Ignore(x => x.Embedding);
            }
            b.Property(x => x.ContentHash).HasMaxLength(64);
            b.HasIndex(x => new { x.Source, x.SourceExternalId }).IsUnique();
            b.HasIndex(x => x.PostedAt);
            b.HasIndex(x => x.IsActive);
            // Freshness sweep scans by LastSeenAt; dedupe groups by ContentHash;
            // matching filters out duplicates by CanonicalJobId. (AIRMVP1-203)
            b.HasIndex(x => x.LastSeenAt);
            b.HasIndex(x => x.ContentHash);
            b.HasIndex(x => x.CanonicalJobId);
        });

        // ---- Match ----
        mb.Entity<Match>(b =>
        {
            b.HasKey(x => x.Id);
            if (isNpgsql) b.Property(x => x.ReasoningJson).HasColumnType("jsonb");
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasIndex(x => new { x.ConsultantId, x.JobId }).IsUnique();
            b.HasOne(x => x.Tenant).WithMany(t => t.Matches).HasForeignKey(x => x.TenantId);
            b.HasOne(x => x.Consultant).WithMany(c => c.Matches).HasForeignKey(x => x.ConsultantId);
            b.HasOne(x => x.Job).WithMany(j => j.Matches).HasForeignKey(x => x.JobId);
            b.HasQueryFilter(x =>
                CurrentTenantId == null || x.TenantId == CurrentTenantId);
        });

        // ---- TailoredResume ----
        mb.Entity<TailoredResume>(b =>
        {
            b.HasKey(x => x.Id);
            if (isNpgsql) b.Property(x => x.DiffJson).HasColumnType("jsonb");
            b.HasOne(x => x.Match).WithMany(m => m.TailoredResumes).HasForeignKey(x => x.MatchId);
        });

        // ---- Submission ----
        mb.Entity<Submission>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.ResponseStatus).HasMaxLength(32);
            if (isNpgsql) b.Property(x => x.ResponsePayloadJson).HasColumnType("jsonb");
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
            b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            b.HasIndex(x => new { x.ThreadId, x.SentAt });
            // Inbound dedupe: skip a Gmail message we've already threaded. (AIRMVP1-401)
            b.HasIndex(x => x.ProviderMessageId);
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

        // ---- LlmCall (audit log + budget source-of-truth) ----
        // Deliberately NOT tenant-filtered: the gateway must read calls across
        // tenants to sum budgets, and admin reporting needs the same.
        // Cross-tenant reads from product code MUST go through .Where(x => x.TenantId == ...)
        // explicitly; reviewers will flag missing filters in PR review.
        mb.Entity<LlmCall>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Model).IsRequired().HasMaxLength(64);
            b.Property(x => x.Purpose).IsRequired().HasMaxLength(64);
            b.Property(x => x.CostUsdEstimate).HasPrecision(12, 6);
            b.Property(x => x.PromptText).IsRequired().HasMaxLength(LlmCall.MaxPayloadChars);
            b.Property(x => x.ResponseText).IsRequired().HasMaxLength(LlmCall.MaxPayloadChars);
            // Budget queries: (tenant_id, model, created_at) covering index.
            b.HasIndex(x => new { x.TenantId, x.Model, x.CreatedAt });
            // For per-purpose cost analytics (and the dev dashboard).
            b.HasIndex(x => new { x.Purpose, x.CreatedAt });
        });

        // ---- EmailLog (audit + warmup throttle source-of-truth) ----
        // Not tenant-filtered: warmup counts + admin reporting read across the
        // tenant explicitly. (AIRMVP1-305)
        mb.Entity<EmailLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ToAddress).IsRequired().HasMaxLength(254);
            b.Property(x => x.Subject).IsRequired().HasMaxLength(500);
            b.Property(x => x.Purpose).IsRequired().HasMaxLength(32);
            b.Property(x => x.Status).IsRequired().HasMaxLength(16);
            b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            b.Property(x => x.Body).HasMaxLength(EmailLog.MaxBodyChars);
            // Warmup throttle query: (tenant_id, status, created_at).
            b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            // Reply correlation: look up by recipient address. (AIRMVP1-401)
            b.HasIndex(x => x.ToAddress);
        });

        // ---- GmailAccount (per-tenant connected mailbox + OAuth tokens) ----
        mb.Entity<GmailAccount>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EmailAddress).IsRequired().HasMaxLength(254);
            b.Property(x => x.RefreshToken).IsRequired().HasMaxLength(512);
            b.Property(x => x.AccessToken).HasMaxLength(2048);
            b.Property(x => x.LastHistoryId).HasMaxLength(64);
            // One connected mailbox per tenant in v1.
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        // ---- Notification (in-app notifications + unread badge) ----
        mb.Entity<Notification>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).IsRequired().HasMaxLength(32);
            b.Property(x => x.Title).IsRequired().HasMaxLength(200);
            b.Property(x => x.Body).HasMaxLength(Notification.MaxBodyChars);
            b.Property(x => x.Link).HasMaxLength(500);
            // Unread-first feed query: (tenant_id, read_at, created_at).
            b.HasIndex(x => new { x.TenantId, x.ReadAt, x.CreatedAt });
            b.HasQueryFilter(x =>
                CurrentTenantId == null || x.TenantId == CurrentTenantId);
        });

        // ---- FollowUp (planned recruiter nudges; owner-approval default) ----
        mb.Entity<FollowUp>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Recipient).IsRequired().HasMaxLength(254);
            b.Property(x => x.DraftSubject).IsRequired().HasMaxLength(500);
            b.Property(x => x.DraftBody).IsRequired().HasMaxLength(FollowUp.MaxBodyChars);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.FailureReason).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasIndex(x => new { x.MatchId, x.Status });
            b.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
            b.HasQueryFilter(x =>
                CurrentTenantId == null || x.TenantId == CurrentTenantId);
        });

        // ---- WaitlistEntry (anonymous marketing signups; NOT tenant-scoped) ----
        mb.Entity<WaitlistEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).IsRequired().HasMaxLength(254);
            b.Property(x => x.Persona).HasMaxLength(64);
            b.Property(x => x.Source).HasMaxLength(128);
            b.HasIndex(x => x.Email).IsUnique();
        });

        // ---- BillingSubscription (Stripe state cache; one per tenant) ----
        mb.Entity<BillingSubscription>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.StripeCustomerId).HasMaxLength(64);
            b.Property(x => x.StripeSubscriptionId).HasMaxLength(64);
            b.Property(x => x.PriceId).HasMaxLength(64);
            b.Property(x => x.Status).IsRequired().HasMaxLength(16);
            b.HasIndex(x => x.TenantId).IsUnique();
            b.HasIndex(x => x.StripeCustomerId);
            b.HasQueryFilter(x =>
                CurrentTenantId == null || x.TenantId == CurrentTenantId);
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
