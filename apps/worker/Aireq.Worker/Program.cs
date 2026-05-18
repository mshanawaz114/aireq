// Aireq.Worker — background job host.
// Wires:
//   - Serilog
//   - Hangfire (Postgres-backed scheduler + server)
//   - Hangfire dashboard at /hangfire (dev only; gate behind auth in AIRMVP1-103)
// First real jobs land in AIRMVP1-104 (resume-parse) and AIRMVP1-201 (job-ingestion).
// Refs: AIRMVP1-101

using Hangfire;
using Hangfire.PostgreSql;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "worker")
    .WriteTo.Async(a => a.Console()));

var dbConnection = builder.Configuration["DATABASE_URL_DEV"]
                   ?? builder.Configuration.GetConnectionString("Default")
                   ?? throw new InvalidOperationException(
                       "DATABASE_URL_DEV not configured. Copy .env.example to .env.local and set it.");

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

// Hangfire dashboard — wide open in dev; AIRMVP1-103 gates behind auth.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Aireq · jobs",
});

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "worker" }));

app.Run();
