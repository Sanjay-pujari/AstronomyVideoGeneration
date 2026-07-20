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
        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "GoogleCalendar", "MicrosoftCalendar", "Quartz", "Hangfire", "Cron", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "DateTimeOffset.UtcNow", "CertificationCoordinator", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Calculate", "Compute", "Expand", "Generate", "Schedule", "Predict", "Forecast", "Recommend", "ResolveOccurrence", "EnumerateOccurrences", "FindNext", "FindPrevious", "Rank", "Score", "EphemerisService" };
        foreach (var file in Directory.GetFiles(temporalDirectory, "*.cs"))
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
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), p => p.PropertyType == typeof(object) || (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)));
            Assert.DoesNotContain(type.GetCustomAttributesData(), a => a.AttributeType.Namespace?.Contains("Serialization", StringComparison.Ordinal) == true || a.AttributeType.Name.Contains("Json", StringComparison.Ordinal));
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), m => new[] { "Calculate", "Compute", "Expand", "Generate", "Schedule", "Predict", "Forecast", "Recommend", "ResolveOccurrence", "EnumerateOccurrences", "FindNext", "FindPrevious" }.Any(term => m.Name.Contains(term, StringComparison.Ordinal)));
        }
        Assert.True(typeof(AstronomyTemporalAnchor).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Any(c => c.GetParameters().Length == 0 && c.IsFamilyAndAssembly));
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
