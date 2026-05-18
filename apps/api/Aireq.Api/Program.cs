// Aireq.Api — entrypoint.
// Wires:
//   - Serilog structured logging
//   - EF Core + Npgsql + pgvector (Neon)
//   - JWT bearer auth (placeholder; real Identity setup lands in AIRMVP1-103)
//   - OpenAPI / Swagger UI
//   - Permissive CORS for local dev (tightened in deploy story AIRMVP1-107)
//   - Health endpoints (liveness + readiness with DB ping)
// Refs: AIRMVP1-101

using Aireq.Api.Data;
using Aireq.Api.Endpoints;
using Aireq.Shared.Db;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

// Load .env.local from repo root (dev only) before building the host so values
// land in Environment.GetEnvironmentVariable() and IConfiguration alike.
// Production deployments inject env vars via the platform — TraversePath finds
// nothing and exits silently in that case.
DotNetEnv.Env.TraversePath().Load(".env.local");

var builder = WebApplication.CreateBuilder(args);

// --- Logging ---------------------------------------------------------------
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "api")
    .WriteTo.Async(a => a.Console()));

// --- Configuration ---------------------------------------------------------
var rawDb = builder.Configuration["DATABASE_URL_DEV"]
            ?? builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "DATABASE_URL_DEV not configured. Copy .env.example to .env.local and set it.");

// Neon's URI form -> canonical key=value form that all Npgsql code paths accept.
var dbConnection = NeonConnectionString.Normalize(rawDb);

// --- Services --------------------------------------------------------------
// Snake_case naming is applied inside AireqDbContext.OnModelCreating, not via
// the EFCore.NamingConventions plugin (which lags EF Core 10 as of May 2026).
builder.Services.AddDbContext<AireqDbContext>(opts => opts
    .UseNpgsql(dbConnection, npg => npg
        .UseVector()
        .MigrationsAssembly(typeof(AireqDbContext).Assembly.GetName().Name)));

builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnection, name: "postgres", tags: new[] { "ready" });

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(
        builder.Configuration["WEB_ORIGIN"] ?? "http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Minimal JWT bearer scaffolding — real Identity lands in AIRMVP1-103.
var jwtKey = builder.Configuration["JWT__SIGNING_KEY"] ?? "dev-only-do-not-use-in-prod-32bytes!!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JWT__ISSUER"] ?? "aireq",
            ValidAudience = builder.Configuration["JWT__AUDIENCE"] ?? "aireq-web",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

var app = builder.Build();

// --- Apply EF Core migrations on startup (dev only) ------------------------
// In production we run migrations explicitly from CI/CD; auto-applying at
// process start in prod is a foot-gun (concurrent instances racing).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AireqDbContext>();
    app.Logger.LogInformation("Applying EF Core migrations…");
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Migrations applied. Schema is ready.");
}

// --- Pipeline --------------------------------------------------------------
app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// --- Endpoints -------------------------------------------------------------
app.MapHealthEndpoints();
app.MapDbStatusEndpoints();

app.Run();

// Visible to the integration-test host (WebApplicationFactory).
public partial class Program;
