using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

internal static class EnumGuard
{
    public static AstronomyOrbitalParameterCategory RequireDefined(AstronomyOrbitalParameterCategory value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy orbital parameter category is not defined.");

    public static AstronomyOrbitalParameterQualifier RequireDefined(AstronomyOrbitalParameterQualifier value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy orbital parameter qualifier is not defined.");

    public static AstronomyKeplerianElementType RequireDefined(AstronomyKeplerianElementType value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy Keplerian element type is not defined.");
}
