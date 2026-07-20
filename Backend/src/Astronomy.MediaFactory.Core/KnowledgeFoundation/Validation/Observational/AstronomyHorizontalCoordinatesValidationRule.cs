using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;

public sealed class AstronomyHorizontalCoordinatesValidationRule : AstronomyKnowledgeValidationRule<AstronomyObservationConditionsPayload>
{
    public const string Id = "observational.horizontal-coordinates";
    public override string RuleId => Id; public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Observational; public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.ObservationCondition; public override int Order => 400;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyObservationConditionsPayload payload, AstronomyKnowledgeValidationContext context)
    {
        if (payload.ObservationContext.CoordinateSystem == AstronomyCoordinateSystem.Horizontal && payload.HorizontalCoordinate is null)
            yield return new(AstronomyObservationalValidationCodes.HorizontalCoordinateMissing, context.Mode == AstronomyKnowledgeValidationMode.Standard ? AstronomyKnowledgeValidationSeverity.Warning : AstronomyKnowledgeValidationSeverity.Error, "Horizontal context should include horizontal coordinates.", "$.horizontalCoordinate", RuleId, Domain, Family);
        if (payload.HorizontalCoordinate is null)
        {
            if (payload.HorizonSector.HasValue) yield return new(AstronomyObservationalValidationCodes.HorizonSectorMismatch, AstronomyKnowledgeValidationSeverity.Warning, "Horizon sector without horizontal coordinates can only be structurally validated.", "$.horizonSector", RuleId, Domain, Family);
            yield break;
        }
        var h = payload.HorizontalCoordinate;
        if (h.Azimuth.Component != AstronomyAngularCoordinateComponent.Azimuth) yield return new(AstronomyObservationalValidationCodes.HorizontalCoordinateMismatch, AstronomyKnowledgeValidationSeverity.Error, "Azimuth component is invalid.", "$.horizontalCoordinate.azimuth.component", RuleId, Domain, Family);
        if (h.Altitude.Component != AstronomyAngularCoordinateComponent.Altitude) yield return new(AstronomyObservationalValidationCodes.HorizontalCoordinateMismatch, AstronomyKnowledgeValidationSeverity.Error, "Altitude component is invalid.", "$.horizontalCoordinate.altitude.component", RuleId, Domain, Family);
        if (h.Azimuth.Angle.Unit.Dimension != AstronomyMeasurementDimension.Angle) yield return new(AstronomyObservationalValidationCodes.HorizontalCoordinateDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Azimuth must use an angular measurement.", "$.horizontalCoordinate.azimuth.angle.unit.dimension", RuleId, Domain, Family);
        if (h.Altitude.Angle.Unit.Dimension != AstronomyMeasurementDimension.Angle) yield return new(AstronomyObservationalValidationCodes.HorizontalCoordinateDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Altitude must use an angular measurement.", "$.horizontalCoordinate.altitude.angle.unit.dimension", RuleId, Domain, Family);
        if (payload.ObservationContext.CoordinateSystem != AstronomyCoordinateSystem.Horizontal || payload.ObservationContext.ReferenceOrigin != AstronomyReferenceOrigin.Topocentric) yield return new(AstronomyObservationalValidationCodes.HorizontalCoordinateMismatch, AstronomyKnowledgeValidationSeverity.Error, "Horizontal coordinates require horizontal topocentric context.", "$.observationContext", RuleId, Domain, Family);
    }
}
