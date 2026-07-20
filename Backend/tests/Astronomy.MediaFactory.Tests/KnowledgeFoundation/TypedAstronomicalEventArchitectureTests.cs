using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedAstronomicalEventArchitectureTests
{
    [Fact]
    public void Event_production_files_are_bounded_and_free_of_forbidden_references()
    {
        var root = FindRepositoryRoot();
        var eventsDirectory = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains", "Events");
        Assert.True(Directory.Exists(eventsDirectory), $"Events directory was not found at {eventsDirectory}.");
        var files = Directory.GetFiles(eventsDirectory, "*.cs", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName).ToArray();
        Assert.NotEmpty(files);
        Assert.Contains(files, file => Path.GetFileName(file) == "AstronomyEvent.cs");
        Assert.Contains(files, file => Path.GetFileName(file) == "AstronomyEventPayload.cs");
        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "WeatherClient", "ForecastClient", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "DateTimeOffset.UtcNow", "CertificationCoordinator", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Calculate", "Compute", "Detect", "Discover", "ConvertTo", "Transform", "Propagate", "Interpolate", "Predict", "Forecast", "Recommend", "FindBest", "Rank", "Score", "EvaluateVisibility", "EphemerisService" };
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var term in forbidden) Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Public_api_is_immutable_and_non_computational()
    {
        var types = typeof(AstronomyEvent).Assembly.GetTypes().Where(t => t.Namespace == typeof(AstronomyEvent).Namespace && t.IsPublic).ToArray();
        Assert.NotEmpty(types);
        foreach (var type in types)
        {
            Assert.DoesNotContain(type.GetProperties(), p => p.SetMethod?.IsPublic == true);
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(object) || p.PropertyType.Name.Contains("Dictionary", StringComparison.Ordinal));
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), m => new[] { "Calculate", "Compute", "Detect", "Discover", "Convert", "Transform", "Predict", "Forecast", "Recommend", "Rank", "Score" }.Any(term => m.Name.Contains(term, StringComparison.Ordinal)));
        }
        Assert.All(typeof(AstronomyEvent).GetProperties().Where(p => typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string)), p => Assert.True(p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)));
        Assert.True(typeof(AstronomyEventTemporalExtent).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0);
        Assert.Equal(new[] { typeof(AstronomyInstantEventTemporalExtent), typeof(AstronomyIntervalEventTemporalExtent) }, typeof(AstronomyEventTemporalExtent).Assembly.GetTypes().Where(t => t.BaseType == typeof(AstronomyEventTemporalExtent)).OrderBy(t => t.Name).ToArray());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Backend", "src", "Astronomy.MediaFactory.Core"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException($"Repository root could not be discovered from {AppContext.BaseDirectory}.");
    }
}
