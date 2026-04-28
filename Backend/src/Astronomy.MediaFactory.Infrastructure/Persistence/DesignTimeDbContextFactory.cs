using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
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
        var basePath = Directory.GetCurrentDirectory();
        var environment =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        static IEnumerable<string> EnumerateCandidateConfigFiles(string startingDirectory, string? env)
        {
            static IEnumerable<string> ForDir(string dir, string? environmentName)
            {
                var files = new List<string>
                {
                    Path.Combine(dir, "appsettings.json"),
                };
                if (!string.IsNullOrWhiteSpace(environmentName))
                    files.Add(Path.Combine(dir, $"appsettings.{environmentName}.json"));

                // common repo layouts
                files.Add(Path.Combine(dir, "Backend", "src", "Astronomy.MediaFactory.Api", "appsettings.json"));
                files.Add(Path.Combine(dir, "Backend", "src", "Astronomy.MediaFactory.Worker", "appsettings.json"));
                if (!string.IsNullOrWhiteSpace(environmentName))
                {
                    files.Add(Path.Combine(dir, "Backend", "src", "Astronomy.MediaFactory.Api", $"appsettings.{environmentName}.json"));
                    files.Add(Path.Combine(dir, "Backend", "src", "Astronomy.MediaFactory.Worker", $"appsettings.{environmentName}.json"));
                }

                // running from inside Backend/
                files.Add(Path.Combine(dir, "src", "Astronomy.MediaFactory.Api", "appsettings.json"));
                files.Add(Path.Combine(dir, "src", "Astronomy.MediaFactory.Worker", "appsettings.json"));
                if (!string.IsNullOrWhiteSpace(environmentName))
                {
                    files.Add(Path.Combine(dir, "src", "Astronomy.MediaFactory.Api", $"appsettings.{environmentName}.json"));
                    files.Add(Path.Combine(dir, "src", "Astronomy.MediaFactory.Worker", $"appsettings.{environmentName}.json"));
                }

                return files;
            }

            var dir = new DirectoryInfo(startingDirectory);
            for (var i = 0; i < 8 && dir is not null; i++)
            {
                foreach (var file in ForDir(dir.FullName, env))
                    yield return file;

                dir = dir.Parent;
            }
        }

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(basePath);

        foreach (var file in EnumerateCandidateConfigFiles(basePath, environment).Distinct(StringComparer.OrdinalIgnoreCase))
            configBuilder.AddJsonFile(file, optional: true);

        var config = configBuilder
            .AddEnvironmentVariables()
            .Build();

        var cs =
            config.GetConnectionString("Postgres")
            ?? config["ConnectionStrings:Postgres"];

        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Missing Postgres connection string for design-time EF.\n" +
                "Set ConnectionStrings:Postgres in an appsettings.json (Api/Worker) or set env var ConnectionStrings__Postgres, then re-run dotnet ef.");
        }

        // Match the runtime safety guard: block localhost unless explicitly allowed.
        var allowLocalhost =
            config.GetValue<bool?>("DatabaseSafety:AllowLocalhostPostgres") == true
            || string.Equals(Environment.GetEnvironmentVariable("ALLOW_LOCALHOST_POSTGRES"), "true", StringComparison.OrdinalIgnoreCase);
        if (!allowLocalhost)
        {
            var csb = new NpgsqlConnectionStringBuilder(cs);
            var host = (csb.Host ?? "").Trim();
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || host.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to use a localhost Postgres connection for EF design-time. Set ALLOW_LOCALHOST_POSTGRES=true to override. Current Host='{host}'.");
            }
        }

        var optionsBuilder = new DbContextOptionsBuilder<MediaFactoryDbContext>();
        optionsBuilder.UseNpgsql(cs);
        return new MediaFactoryDbContext(optionsBuilder.Options);
    }
}

