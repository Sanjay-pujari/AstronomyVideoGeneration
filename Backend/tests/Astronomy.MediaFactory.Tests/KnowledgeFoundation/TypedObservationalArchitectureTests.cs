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
        Assert.True(
            Directory.Exists(directory),
            $"Observational production directory was not found: {directory}");

        var files = Directory
            .EnumerateFiles(
                directory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.NotEmpty(files);

        var fileNames = files
            .Select(file => Path.GetFileName(file)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedFiles = new[]
        {
            "AstronomyObservationalQuantityId.cs",
            "AstronomyObservationalQuantity.cs",
            "AstronomyVisibilityAssessment.cs",
            "AstronomyObservationTimeWindow.cs",
            "AstronomyVisibilityWindow.cs",
            "AstronomyHorizontalObservationCoordinate.cs",
            "AstronomyObservationConditions.cs",
            "AstronomyObservationConditionsPayload.cs",
            "AstronomyVisibilityWindowsPayload.cs"
        };

        Assert.All(
            expectedFiles,
            expectedFile =>
                Assert.Contains(expectedFile, fileNames));

        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "WeatherClient", "ForecastClient", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "DateTimeOffset.UtcNow", "CertificationCoordinator", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Calculate", "Compute", "ConvertTo", "Transform", "Propagate", "Interpolate", "Predict", "Forecast", "Recommend", "FindBest", "EvaluateVisibility", "EphemerisService" };
        foreach (var file in files)
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
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance), p => p.PropertyType == typeof(object) || IsDictionaryType(p.PropertyType));
            Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.Name.Contains("Calculate", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Predict", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Forecast", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Recommend", StringComparison.OrdinalIgnoreCase));
        }
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
        var startingDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(startingDirectory);

        while (directory is not null)
        {
            var solutionPath = Path.Combine(
                directory.FullName,
                "Backend",
                "Astronomy.MediaFactory.slnx");

            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root could not be found while walking upward from '{startingDirectory}'. " +
            "Expected to find 'Backend/Astronomy.MediaFactory.slnx'.");
    }
}
