using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;

public static class TypedKnowledgeEnumGuard
{
    public static AstronomyKnowledgeDomain RequireDefined(AstronomyKnowledgeDomain value, string parameterName = "domain")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy knowledge domain is not defined.");

    public static AstronomyKnowledgePayloadFamily RequireDefined(AstronomyKnowledgePayloadFamily value, string parameterName = "family")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy knowledge payload family is not defined.");

    public static AstronomyMeasurementDimension RequireDefined(AstronomyMeasurementDimension value, string parameterName = "dimension")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy measurement dimension is not defined.");

    public static AstronomyPrecisionKind RequireDefined(AstronomyPrecisionKind value, string parameterName = "kind")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy precision kind is not defined.");

    public static AstronomyUncertaintyKind RequireDefined(AstronomyUncertaintyKind value, string parameterName = "kind")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy uncertainty kind is not defined.");

    public static AstronomyReferenceFrame RequireDefined(AstronomyReferenceFrame value, string parameterName = "referenceFrame")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy reference frame is not defined.");

    public static AstronomyCoordinateSystem RequireDefined(AstronomyCoordinateSystem value, string parameterName = "coordinateSystem")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy coordinate system is not defined.");

    public static AstronomyEpochKind RequireDefined(AstronomyEpochKind value, string parameterName = "kind")
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, "Astronomy epoch kind is not defined.");
}
