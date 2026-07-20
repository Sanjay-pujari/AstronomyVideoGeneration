using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public class TypedObservationalArchitectureTests
{
    [Fact]
    public void ObservationalProductionFiles_DoNotReferenceForbiddenBoundaries()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "Backend", "src", "Astronomy.MediaFactory.Core", "KnowledgeFoundation", "TypedDomains", "Observational");
        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "WeatherClient", "ForecastClient", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "DateTimeOffset.UtcNow", "CertificationCoordinator", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Calculate", "Compute", "ConvertTo", "Transform", "Propagate", "Interpolate", "Predict", "Forecast", "Recommend", "FindBest", "EvaluateVisibility", "EphemerisService" };
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (var term in forbidden) Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ObservationalPublicApi_IsImmutableAndBounded()
    {
        var types = new[] { typeof(AstronomyObservationalQuantityId), typeof(AstronomyObservationalQuantity), typeof(AstronomyVisibilityAssessment), typeof(AstronomyObservationTimeWindow), typeof(AstronomyVisibilityWindow), typeof(AstronomyHorizontalObservationCoordinate), typeof(AstronomyObservationConditions), typeof(AstronomyObservationConditionsPayload), typeof(AstronomyVisibilityWindowsPayload) };
        foreach (var type in types)
        {
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.SetMethod is not null && p.SetMethod.IsPublic);
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.PropertyType == typeof(object) || (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>)));
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.Name.Contains("Calculate", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Predict", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Forecast", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Recommend", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "AstronomyVideoGeneration.sln"))) return current;
            current = Directory.GetParent(current)?.FullName;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
