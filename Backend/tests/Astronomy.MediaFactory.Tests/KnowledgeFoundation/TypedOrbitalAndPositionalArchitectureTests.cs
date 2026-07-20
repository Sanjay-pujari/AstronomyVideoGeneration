using System.Reflection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class TypedOrbitalAndPositionalArchitectureTests
{
    [Fact]
    public void Task23c_public_api_has_no_mutable_boundary_or_computational_members()
    {
        var types = Task23cTypes();
        var forbiddenNames = new[] { "Evidence", "Confidence", "Audit", "Validity", "Source", "Json", "Serialize", "ServiceCollection", "DbContext", "IQueryable", "HttpClient", "Observer", "Visibility", "Event", "Calculate", "Compute", "Convert", "Transform", "Propagate", "Predict", "Interpolate", "Solve" };
        foreach (var type in types)
        {
            Assert.All(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property =>
            {
                Assert.Null(property.SetMethod);
                Assert.NotEqual(typeof(object), property.PropertyType);
                Assert.False(IsDictionaryType(property.PropertyType));
                Assert.DoesNotContain(property.Name, forbiddenNames, StringComparer.OrdinalIgnoreCase);
            });

            Assert.DoesNotContain(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), method => forbiddenNames.Any(name => method.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Task23c_production_files_avoid_forbidden_architecture_terms()
    {
        var root = FindRepositoryRoot();
        var typedDomainsDirectory = Path.Combine(
            root,
            "Backend",
            "src",
            "Astronomy.MediaFactory.Core",
            "KnowledgeFoundation",
            "TypedDomains");

        var orbitalDirectory = Path.Combine(
            typedDomainsDirectory,
            "Orbital");

        var positionalDirectory = Path.Combine(
            typedDomainsDirectory,
            "Positional");

        Assert.True(
            Directory.Exists(orbitalDirectory),
            $"Orbital production directory was not found: {orbitalDirectory}");

        Assert.True(
            Directory.Exists(positionalDirectory),
            $"Positional production directory was not found: {positionalDirectory}");

        var orbitalFiles = Directory
            .EnumerateFiles(
                orbitalDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .ToArray();

        var positionalFiles = Directory
            .EnumerateFiles(
                positionalDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .ToArray();

        Assert.NotEmpty(orbitalFiles);
        Assert.NotEmpty(positionalFiles);

        var files = orbitalFiles
            .Concat(positionalFiles)
            .ToArray();

        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "AstronomyObservationContext", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "Stellarium", "Skyfield", "SPICE", "NAIF", "Astropy", "SOFA", "ERFA", "JPL", "Horizons", "DateTimeOffset.UtcNow", "CertificationCoordinator", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Calculate", "Compute", "ConvertTo", "Transform", "Propagate", "Interpolate", "Predict", "Solve", "EphemerisService" };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var term in forbidden)
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
    }

    private static Type[] Task23cTypes() =>
    [
        typeof(AstronomyOrbitalParameterId), typeof(AstronomyOrbitalParameter), typeof(AstronomyOrbitalReferenceContext), typeof(AstronomyKeplerianElement), typeof(AstronomyKeplerianElementsPayload), typeof(AstronomyOrbitalParametersPayload),
        typeof(AstronomyAngularCoordinateValue), typeof(AstronomyCartesianCoordinate), typeof(AstronomySphericalCoordinate), typeof(AstronomyPositionValue), typeof(AstronomyAngularPositionValue), typeof(AstronomySphericalPositionValue), typeof(AstronomyCartesianPositionValue), typeof(AstronomyPositionReferenceContext), typeof(AstronomySpatialPosition), typeof(AstronomySpatialPositionPayload)
    ];

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
