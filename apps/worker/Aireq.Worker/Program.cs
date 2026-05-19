// Aireq.Worker — background job host.
// Wires:
//   - Serilog
//   - Hangfire (Postgres-backed scheduler + server)
//   - Hangfire dashboard at /hangfire (dev only; gate behind auth in AIRMVP1-103)
//   - IResumeParser (placeholder impl; real Claude parsing lands in AIRMVP1-105)
// Refs: AIRMVP1-101, AIRMVP1-104

using Aireq.Shared.Db;
using Aireq.Shared.Jobs;
using Aireq.Worker.Resumes;
using Hangfire;
using Hangfire.PostgreSql;
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

// --- Job implementations ---------------------------------------------------
// Hangfire resolves these from DI when it picks a job off the queue.
// Scoped so each job invocation gets fresh state (HttpClient, DbContext, etc.
// will be added in AIRMVP1-105 when the real parser lands).
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
