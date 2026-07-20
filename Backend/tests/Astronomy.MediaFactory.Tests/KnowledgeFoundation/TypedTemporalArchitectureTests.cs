using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedTemporalArchitectureTests
{
    [Fact]
    public void Temporal_production_directory_contains_expected_principal_files()
    {
        var root = FindRepositoryRoot();
        var temporalDirectory = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains", "Temporal");
        Assert.True(Directory.Exists(temporalDirectory), $"Temporal directory was not found at '{temporalDirectory}'.");
        var files = Directory.GetFiles(temporalDirectory, "*.cs").Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(files);
        Assert.Contains("AstronomyTemporalPatternId.cs", files);
        Assert.Contains("AstronomyTemporalPattern.cs", files);
        Assert.Contains("AstronomyTemporalPatternPayload.cs", files);
    }

    [Fact]
    public void Temporal_production_files_avoid_forbidden_architecture_references()
    {
        var temporalDirectory = Path.Combine(FindRepositoryRoot(), "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains", "Temporal");
        Assert.True(Directory.Exists(temporalDirectory), $"Temporal directory was not found at '{temporalDirectory}'.");

        var files = Directory
            .GetFiles(
                temporalDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.NotEmpty(files);

        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "GoogleCalendar", "MicrosoftCalendar", "Quartz", "Hangfire", "Cron", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "DateTimeOffset.UtcNow", "CertificationCoordinator", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Calculate", "Compute", "Expand", "Generate", "Schedule", "Predict", "Forecast", "Recommend", "ResolveOccurrence", "EnumerateOccurrences", "FindNext", "FindPrevious", "Rank", "Score", "EphemerisService" };
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var term in forbidden) Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Temporal_public_api_shape_remains_immutable_and_bounded()
    {
        var assembly = typeof(AstronomyTemporalPattern).Assembly;
        var types = assembly.GetTypes().Where(t => t.Namespace == "Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal" && t.IsPublic).ToArray();
        Assert.NotEmpty(types);
        foreach (var type in types)
        {
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), p => p.SetMethod is not null && p.SetMethod.IsPublic);
            Assert.DoesNotContain(
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
                p => p.PropertyType == typeof(object) || IsDictionaryType(p.PropertyType));
            Assert.DoesNotContain(type.GetCustomAttributesData(), a => a.AttributeType.Namespace?.Contains("Serialization", StringComparison.Ordinal) == true || a.AttributeType.Name.Contains("Json", StringComparison.Ordinal));
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), m => new[] { "Calculate", "Compute", "Expand", "Generate", "Schedule", "Predict", "Forecast", "Recommend", "ResolveOccurrence", "EnumerateOccurrences", "FindNext", "FindPrevious" }.Any(term => m.Name.Contains(term, StringComparison.Ordinal)));
        }
        var constructors = typeof(AstronomyTemporalAnchor)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);

        var parameterlessConstructors = constructors
            .Where(constructor => constructor.GetParameters().Length == 0)
            .ToArray();

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
                           parameters[0].ParameterType == typeof(AstronomyTemporalAnchor);
                })
            .ToArray();

        Assert.Single(copyConstructors);

        var copyConstructor = copyConstructors[0];

        Assert.False(copyConstructor.IsPublic);
        Assert.True(copyConstructor.IsFamily || copyConstructor.IsFamilyAndAssembly);

        var expectedVariants = new[]
            {
                typeof(AstronomyCalendarDateTemporalAnchor),
                typeof(AstronomyCustomTemporalAnchor),
                typeof(AstronomyDayOfYearTemporalAnchor),
                typeof(AstronomyEpochTemporalAnchor),
                typeof(AstronomyMonthTemporalAnchor),
                typeof(AstronomyUtcTemporalAnchor)
            }
            .OrderBy(type => type.Name)
            .ToArray();

        var actualVariants = typeof(AstronomyTemporalAnchor)
            .Assembly
            .GetTypes()
            .Where(type => type.BaseType == typeof(AstronomyTemporalAnchor))
            .OrderBy(type => type.Name)
            .ToArray();

        Assert.Equal(expectedVariants, actualVariants);
    }

    private static bool IsDictionaryType(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var genericDefinition = type.GetGenericTypeDefinition();

        return genericDefinition == typeof(Dictionary<,>) ||
               genericDefinition == typeof(IDictionary<,>) ||
               genericDefinition == typeof(IReadOnlyDictionary<,>);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Backend", "src", "Astronomy.MediaFactory.Core")) && Directory.Exists(Path.Combine(current.FullName, "Backend", "tests"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException($"Repository root could not be found while walking upward from '{AppContext.BaseDirectory}'. Expected Backend/src/Astronomy.MediaFactory.Core and Backend/tests directories.");
    }
}
