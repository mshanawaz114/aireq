// Aireq.Worker — background job host.
// Wires:
//   - Serilog
//   - Hangfire (Postgres-backed scheduler + server)
//   - Hangfire dashboard at /hangfire (dev only; gate behind auth in AIRMVP1-107)
//   - EF Core (AireqDbContext) + IBlobStorage for jobs that need them
//   - ILlmGateway (Anthropic) with per-tenant budget enforcement + audit log
//   - IResumeParser (real Claude Haiku impl)
// Refs: AIRMVP1-101, AIRMVP1-104, AIRMVP1-105

using Aireq.Api.Data;
using Aireq.Api.Storage;
using Aireq.Shared.Db;
using Aireq.Shared.Jobs;
using Aireq.Shared.Llm;
using Aireq.Worker.Jobs;
using Aireq.Worker.Jobs.Sources;
using Aireq.Worker.Llm;
using Aireq.Worker.Matching;
using Aireq.Worker.Resumes;
using Aireq.Worker.Tailoring;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Load .env.local from repo root (dev only) — same reasoning as the API.
DotNetEnv.Env.TraversePath().Load(".env.local");

// QuestPDF Community license (free under $1M revenue) — must be set before any
// PDF is generated. (AIRMVP1-302)
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "worker")
    .WriteTo.Async(a => a.Console()));

var rawDb = builder.Configuration["DATABASE_URL_DEV"]
            ?? builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "DATABASE_URL_DEV not configured. Copy .env.example to .env.local and set it.");

// Neon's URI form -> canonical key=value form for Hangfire.PostgreSql.
var dbConnection = NeonConnectionString.Normalize(rawDb);

builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(dbConnection)));

builder.Services.AddHangfireServer(opts =>
{
    opts.ServerName = $"aireq-worker-{Environment.MachineName}";
    opts.Queues = new[] { "default", "discovery", "tailor", "apply", "email" };
    opts.WorkerCount = Environment.ProcessorCount;
});

// --- Persistence + storage ------------------------------------------------
// Each Hangfire job gets a fresh DbContext + scoped services thanks to the
// JobActivator wired by Hangfire.AspNetCore's AddHangfire.
builder.Services.AddDbContext<AireqDbContext>(opts => opts
    .UseNpgsql(dbConnection, npg => npg
        .UseVector()
        .MigrationsAssembly(typeof(AireqDbContext).Assembly.GetName().Name)));

builder.Services.AddSingleton<IBlobStorage, AzureBlobStorage>();

// --- LLM gateway ----------------------------------------------------------
// IHttpClientFactory wires retry / circuit-breaker policies in AIRMVP1-130.
// Provider selection: LLM__PROVIDER=groq (default, free) | anthropic (paid).
// We ship MVP on Groq + Llama 3.1 8B to keep spend at $0; flip to Anthropic
// when there's revenue.
builder.Services.Configure<LlmBudgetOptions>(
    builder.Configuration.GetSection(LlmBudgetOptions.ConfigKey));

var llmProvider = (builder.Configuration["LLM:PROVIDER"] ?? "groq").Trim().ToLowerInvariant();
switch (llmProvider)
{
    case "anthropic":
        builder.Services.AddHttpClient<ILlmGateway, AnthropicLlmGateway>(c =>
            c.Timeout = TimeSpan.FromMinutes(2));
        break;
    case "groq":
    default:
        builder.Services.AddHttpClient<ILlmGateway, GroqLlmGateway>(c =>
            c.Timeout = TimeSpan.FromMinutes(2));
        break;
}

// --- Job discovery sources (AIRMVP1-201) ----------------------------------
// Each source is registered with its own typed HttpClient and self-disables
// when its key is missing. Adding more sources = one AddHttpClient line.
builder.Services.Configure<JobIngestionOptions>(
    builder.Configuration.GetSection(JobIngestionOptions.ConfigKey));
builder.Services.Configure<AtsSeedOptions>(
    builder.Configuration.GetSection(AtsSeedOptions.ConfigKey));

// Keyword-search sources (config-keyed).
builder.Services.AddHttpClient<IJobSource, AdzunaJobSource>();
builder.Services.AddHttpClient<IJobSource, UsaJobsJobSource>();
// ATS full-board sources (keyless, freshest — straight from employer ATS).
builder.Services.AddHttpClient<IJobSource, GreenhouseJobSource>();
builder.Services.AddHttpClient<IJobSource, LeverJobSource>();
builder.Services.AddHttpClient<IJobSource, AshbyJobSource>();

builder.Services.AddScoped<JobIngestionService>();
builder.Services.AddScoped<IJobIngestionRunner, JobIngestionRunner>();

// Dedupe + freshness maintenance (AIRMVP1-203).
builder.Services.Configure<JobMaintenanceOptions>(
    builder.Configuration.GetSection(JobMaintenanceOptions.ConfigKey));
builder.Services.AddScoped<JobMaintenanceService>();
builder.Services.AddScoped<IJobMaintenanceRunner, JobMaintenanceRunner>();

// Embeddings (AIRMVP1-204a). Gemini text-embedding-004 (free), 768-dim.
builder.Services.Configure<EmbeddingOptions>(
    builder.Configuration.GetSection(EmbeddingOptions.ConfigKey));
builder.Services.AddHttpClient<IEmbeddingGateway, GeminiEmbeddingGateway>(c =>
    c.Timeout = TimeSpan.FromMinutes(1));
builder.Services.AddScoped<JobEmbedder>();
builder.Services.AddScoped<ResumeEmbedder>();
builder.Services.AddScoped<IEmbeddingRunner, EmbeddingRunner>();

// Matching (AIRMVP1-204b). pgvector cosine + rule filters -> matches.
builder.Services.Configure<MatchingOptions>(
    builder.Configuration.GetSection(MatchingOptions.ConfigKey));
builder.Services.AddScoped<IJobCandidateFinder, PgVectorJobCandidateFinder>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddScoped<IMatchingRunner, MatchingRunner>();

// LLM match scoring + reasoning (AIRMVP1-205).
builder.Services.Configure<MatchScoringOptions>(
    builder.Configuration.GetSection(MatchScoringOptions.ConfigKey));
builder.Services.AddScoped<MatchScorer>();
builder.Services.AddScoped<IMatchScoringRunner, MatchScoringRunner>();

// Resume tailoring (AIRMVP1-302). On-demand: enqueued by the API.
builder.Services.AddScoped<ResumeTailor>();
builder.Services.AddScoped<IResumeTailorJob, ResumeTailorJob>();

// Submission — Tier A API channels (AIRMVP1-303). DRY-RUN unless
// FEATURES__ENABLE_LIVE_SUBMIT=true.
builder.Services.Configure<Aireq.Worker.Submission.SubmissionOptions>(
    builder.Configuration.GetSection(Aireq.Worker.Submission.SubmissionOptions.ConfigKey));
builder.Services.AddHttpClient<Aireq.Worker.Submission.ISubmissionChannel, Aireq.Worker.Submission.GreenhouseSubmissionChannel>();
builder.Services.AddHttpClient<Aireq.Worker.Submission.ISubmissionChannel, Aireq.Worker.Submission.LeverSubmissionChannel>();

// Tier B — Playwright per-ATS templates (AIRMVP1-304). Needs browser binaries
// (`playwright install chromium`).
builder.Services.AddSingleton<Aireq.Worker.Submission.Playwright.IAtsPortalTemplate, Aireq.Worker.Submission.Playwright.GreenhouseHostedTemplate>();
builder.Services.AddSingleton<Aireq.Worker.Submission.Playwright.IAtsPortalTemplate, Aireq.Worker.Submission.Playwright.LeverHostedTemplate>();
builder.Services.AddScoped<Aireq.Worker.Submission.ISubmissionChannel, Aireq.Worker.Submission.Playwright.PlaywrightSubmissionChannel>();

// Tier C — cold email (AIRMVP1-305). Also the shared email foundation (W4).
builder.Services.Configure<Aireq.Worker.Email.EmailOptions>(
    builder.Configuration.GetSection(Aireq.Worker.Email.EmailOptions.ConfigKey));
builder.Services.AddHttpClient<Aireq.Shared.Email.IEmailSender, Aireq.Worker.Email.ResendEmailSender>();
builder.Services.AddScoped<Aireq.Worker.Submission.ISubmissionChannel, Aireq.Worker.Submission.EmailSubmissionChannel>();

builder.Services.AddScoped<Aireq.Worker.Submission.SubmissionService>();
builder.Services.AddScoped<ISubmissionJob, Aireq.Worker.Submission.SubmissionJob>();

// --- Job implementations --------------------------------------------------
// Hangfire resolves these from DI when it picks a job off the queue.
// Scoped so each invocation gets fresh DbContext / HttpClient / etc.
builder.Services.AddScoped<IResumeParser, ResumeParser>();

var app = builder.Build();

app.UseSerilogRequestLogging();

// Hangfire dashboard — read-only here (no .RequireAuthorization on the worker
// for now; the worker only listens on the internal port). When the worker
// exposes a public URL (AIRMVP1-107 deploy), wire JWT auth on this map.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // Anyone hitting the worker port can see it in dev; tighten in deploy story.
    DashboardTitle = "Aireq · jobs",
});

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "worker" }));

// Dev-only: kick off job ingestion immediately instead of waiting for the
// 6h cron. Returns the enqueued Hangfire job id. NOT mapped in production —
// ingestion there is purely schedule-driven.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/jobs/ingest", (IBackgroundJobClient jobs) =>
    {
        var id = jobs.Enqueue<IJobIngestionRunner>(r => r.RunAsync(CancellationToken.None));
        return Results.Ok(new { enqueued = id });
    });

    app.MapPost("/jobs/maintenance", (IBackgroundJobClient jobs) =>
    {
        var id = jobs.Enqueue<IJobMaintenanceRunner>(r => r.RunAsync(CancellationToken.None));
        return Results.Ok(new { enqueued = id });
    });

    app.MapPost("/jobs/embed", (IBackgroundJobClient jobs) =>
    {
        var id = jobs.Enqueue<IEmbeddingRunner>(r => r.RunAsync(CancellationToken.None));
        return Results.Ok(new { enqueued = id });
    });

    app.MapPost("/jobs/match", (IBackgroundJobClient jobs) =>
    {
        var id = jobs.Enqueue<IMatchingRunner>(r => r.RunAsync(CancellationToken.None));
        return Results.Ok(new { enqueued = id });
    });

    app.MapPost("/jobs/score", (IBackgroundJobClient jobs) =>
    {
        var id = jobs.Enqueue<IMatchScoringRunner>(r => r.RunAsync(CancellationToken.None));
        return Results.Ok(new { enqueued = id });
    });

    // Bug-bash convenience (AIRMVP1-307): run the whole discovery pipeline in
    // sequence — ingest -> embed -> match -> score — via Hangfire continuations.
    app.MapPost("/jobs/pipeline", (IBackgroundJobClient jobs) =>
    {
        var ingest = jobs.Enqueue<IJobIngestionRunner>(r => r.RunAsync(CancellationToken.None));
        var embed = jobs.ContinueJobWith<IEmbeddingRunner>(ingest, r => r.RunAsync(CancellationToken.None));
        var match = jobs.ContinueJobWith<IMatchingRunner>(embed, r => r.RunAsync(CancellationToken.None));
        var score = jobs.ContinueJobWith<IMatchScoringRunner>(match, r => r.RunAsync(CancellationToken.None));
        return Results.Ok(new { ingest, embed, match, score });
    });
}

// --- Recurring jobs --------------------------------------------------------
// Job discovery runs on a cron (default every 6h). Hangfire persists the
// schedule in Postgres, so this is idempotent across restarts — AddOrUpdate
// just refreshes the definition.
using (var scope = app.Services.CreateScope())
{
    var ingestionOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<JobIngestionOptions>>().Value;
    var maintenanceOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<JobMaintenanceOptions>>().Value;
    var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurring.AddOrUpdate<IJobIngestionRunner>(
        "job-ingestion",
        runner => runner.RunAsync(CancellationToken.None),
        ingestionOpts.Cron);
    recurring.AddOrUpdate<IJobMaintenanceRunner>(
        "job-maintenance",
        runner => runner.RunAsync(CancellationToken.None),
        maintenanceOpts.Cron);

    var embeddingOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<EmbeddingOptions>>().Value;
    recurring.AddOrUpdate<IEmbeddingRunner>(
        "embedding-pass",
        runner => runner.RunAsync(CancellationToken.None),
        embeddingOpts.Cron);

    var matchingOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<MatchingOptions>>().Value;
    recurring.AddOrUpdate<IMatchingRunner>(
        "matching-pass",
        runner => runner.RunAsync(CancellationToken.None),
        matchingOpts.Cron);

    var scoringOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<MatchScoringOptions>>().Value;
    recurring.AddOrUpdate<IMatchScoringRunner>(
        "match-scoring-pass",
        runner => runner.RunAsync(CancellationToken.None),
        scoringOpts.Cron);
}

app.Run();
