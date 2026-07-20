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
        var constructors = typeof(AstronomyEventTemporalExtent).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotEmpty(constructors);

        var parameterlessConstructors = constructors.Where(constructor => constructor.GetParameters().Length == 0).ToArray();
        Assert.Single(parameterlessConstructors);
        var parameterlessConstructor = parameterlessConstructors[0];

        Assert.False(parameterlessConstructor.IsPublic);
        Assert.False(parameterlessConstructor.IsFamily);
        Assert.False(parameterlessConstructor.IsFamilyOrAssembly);
        Assert.True(parameterlessConstructor.IsFamilyAndAssembly);

        var copyConstructors = constructors
            .Where(
                constructor =>
                {
                    var parameters = constructor.GetParameters();

                    return parameters.Length == 1 &&
                           parameters[0].ParameterType == typeof(AstronomyEventTemporalExtent);
                })
            .ToArray();
        Assert.Single(copyConstructors);
        var copyConstructor = copyConstructors[0];

        Assert.False(copyConstructor.IsPublic);
        Assert.True(copyConstructor.IsFamily || copyConstructor.IsFamilyAndAssembly);

        var expectedVariants = new[]
            {
                typeof(AstronomyInstantEventTemporalExtent),
                typeof(AstronomyIntervalEventTemporalExtent)
            }
            .OrderBy(type => type.Name)
            .ToArray();

        var actualVariants = typeof(AstronomyEventTemporalExtent)
            .Assembly
            .GetTypes()
            .Where(type => type.BaseType == typeof(AstronomyEventTemporalExtent))
            .OrderBy(type => type.Name)
            .ToArray();

        Assert.Equal(expectedVariants, actualVariants);
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
