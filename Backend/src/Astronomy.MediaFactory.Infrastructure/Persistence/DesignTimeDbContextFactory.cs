using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>
/// Enables `dotnet ef ... --project Astronomy.MediaFactory.Infrastructure` without a startup project.
/// Uses the same ConnectionStrings:Postgres key resolution as the runtime, but from environment variables.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MediaFactoryDbContext>
{
    public MediaFactoryDbContext CreateDbContext(string[] args)
    {
        var cs =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings:Postgres");

        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Missing Postgres connection string for design-time EF.\n" +
                "Set env var ConnectionStrings__Postgres to your Azure Postgres connection string, then re-run dotnet ef.");
        }

        // Match the runtime safety guard: never allow localhost.
        var csb = new NpgsqlConnectionStringBuilder(cs);
        var host = (csb.Host ?? "").Trim();
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to use a localhost Postgres connection for EF design-time. Current Host='{host}'.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MediaFactoryDbContext>();
        optionsBuilder.UseNpgsql(cs);
        return new MediaFactoryDbContext(optionsBuilder.Options);
    }
}

