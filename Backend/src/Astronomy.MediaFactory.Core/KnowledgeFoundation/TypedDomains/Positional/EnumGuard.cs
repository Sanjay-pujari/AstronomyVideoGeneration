using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

internal static class EnumGuard
{
    public static AstronomyAngularCoordinateComponent RequireDefined(AstronomyAngularCoordinateComponent value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy angular coordinate component is not defined.");

    public static AstronomyPositionRepresentationKind RequireDefined(AstronomyPositionRepresentationKind value, string parameterName)
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy position representation kind is not defined.");
}
