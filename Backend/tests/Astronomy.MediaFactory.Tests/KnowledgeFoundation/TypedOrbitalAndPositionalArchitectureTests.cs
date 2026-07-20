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
                Assert.False(property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IDictionary<,>));
                Assert.DoesNotContain(property.Name, forbiddenNames, StringComparer.OrdinalIgnoreCase);
            });

            Assert.DoesNotContain(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), method => forbiddenNames.Any(name => method.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Task23c_production_files_avoid_forbidden_architecture_terms()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/TypedDomains"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("/Orbital/") || path.Contains("/Positional/"))
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Backend", "Astronomy.MediaFactory.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
