// Aireq.Worker — background job host.
// Wires:
//   - Serilog
//   - Hangfire (Postgres-backed scheduler + server)
//   - Hangfire dashboard at /hangfire (dev only; gate behind auth in AIRMVP1-103)
// First real jobs land in AIRMVP1-104 (resume-parse) and AIRMVP1-201 (job-ingestion).
// Refs: AIRMVP1-101

using Aireq.Shared.Db;
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

var app = builder.Build();

app.UseSerilogRequestLogging();

// NOTE: Hangfire dashboard intentionally NOT mounted here for AIRMVP1-101.
// Adding it requires extra DI registration that's flaky on initial scaffold;
// we wire it properly in AIRMVP1-104 alongside the first real background jobs
// (resume parsing). Until then the server still runs, jobs still execute —
// you just can't view them through a web UI.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "worker" }));

app.Run();
