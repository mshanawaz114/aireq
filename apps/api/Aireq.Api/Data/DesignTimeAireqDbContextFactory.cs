// Design-time DbContext factory.
//
// Picked up by `dotnet ef migrations add ...` and `dotnet ef database update`
// so the tool can construct an AireqDbContext WITHOUT running Program.cs
// (which would try to also start the web host, apply migrations, etc.).
//
// This factory:
//   1. Loads .env.local so DATABASE_URL_DEV is available even outside the
//      web host.
//   2. Normalizes the Neon URI form via NeonConnectionString.Normalize.
//   3. Builds the same DbContextOptions the runtime uses.
//
// Refs: AIRMVP1-102

using Aireq.Shared.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aireq.Api.Data;

public sealed class DesignTimeAireqDbContextFactory : IDesignTimeDbContextFactory<AireqDbContext>
{
    public AireqDbContext CreateDbContext(string[] args)
    {
        DotNetEnv.Env.TraversePath().Load(".env.local");

        var raw = Environment.GetEnvironmentVariable("DATABASE_URL_DEV")
                  ?? throw new InvalidOperationException(
                      "DATABASE_URL_DEV not configured. Design-time tooling reads .env.local; " +
                      "make sure that file exists and has a valid Neon connection string.");

        var conn = NeonConnectionString.Normalize(raw);

        var options = new DbContextOptionsBuilder<AireqDbContext>()
            .UseNpgsql(conn, npg => npg
                .UseVector()
                .MigrationsAssembly(typeof(AireqDbContext).Assembly.GetName().Name))
            .Options;

        return new AireqDbContext(options);
    }
}
