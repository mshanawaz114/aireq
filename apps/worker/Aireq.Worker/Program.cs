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
using Aireq.Worker.Llm;
using Aireq.Worker.Resumes;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Load .env.local from repo root (dev only) — same reasoning as the API.
DotNetEnv.Env.TraversePath().Load(".env.local");

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
// For now this is a plain HttpClient with the default timeout.
builder.Services.Configure<LlmBudgetOptions>(
    builder.Configuration.GetSection(LlmBudgetOptions.ConfigKey));
builder.Services
    .AddHttpClient<ILlmGateway, AnthropicLlmGateway>(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(2);
    });

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

app.Run();
