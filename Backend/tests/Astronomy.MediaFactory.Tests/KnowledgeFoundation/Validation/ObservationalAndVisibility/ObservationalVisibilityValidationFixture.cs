using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.DependencyInjection;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Visibility;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.ObservationalAndVisibility;


internal static class ObservationalVisibilityValidationFixture
{
    public static readonly DateTimeOffset T0 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset T1 = new(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset T2 = new(2026, 8, 1, 2, 0, 0, TimeSpan.Zero);
    public static AstronomyKnowledgeValidationContext Context(AstronomyKnowledgeValidationMode mode = AstronomyKnowledgeValidationMode.Standard, AstronomyKnowledgeValidationSeverity minimumSeverity = AstronomyKnowledgeValidationSeverity.Information) => new(new("task-2-4d-rc1"), T0, mode, minimumSeverity);
    public static AstronomyObservationContext ObservationContext(AstronomyCoordinateSystem coordinateSystem = AstronomyCoordinateSystem.Horizontal, AstronomyReferenceOrigin origin = AstronomyReferenceOrigin.Topocentric, DateTimeOffset? observationTimeUtc = null) => new("earth:greenwich", observationTimeUtc ?? T0, AstronomyReferenceFrame.ICRS, origin, coordinateSystem);
    public static AstronomyObservationConditionsPayload ValidObservationPayload() => new(new("typed.observational.conditions.v1"), ObservationContext(), Conditions(), [Quantity()], HorizontalCoordinate(), AstronomyHorizonSector.SouthEast);
    public static AstronomyVisibilityWindowsPayload ValidVisibilityPayload() => new(new("typed.observational.visibility-windows.v1"), ObservationContext(), [VisibilityWindow()]);
    public static AstronomyObservationConditions Conditions(AstronomySkyConditionKind sky = AstronomySkyConditionKind.Clear, AstronomySeeingQuality seeing = AstronomySeeingQuality.Good, AstronomyTransparencyQuality transparency = AstronomyTransparencyQuality.VeryGood, AstronomyMeasurement? limitingMagnitude = null, AstronomyMeasurement? skyBrightness = null, string? note = "clear") => new(sky, seeing, transparency, limitingMagnitude, skyBrightness, note);
    public static AstronomyObservationalQuantity Quantity(string id = "obs.quantity.altitude", AstronomyObservationalQuantityCategory category = AstronomyObservationalQuantityCategory.HorizontalPosition, AstronomyMeasurement? measurement = null, AstronomyObservationalQuantityQualifier qualifier = AstronomyObservationalQuantityQualifier.Observed, AstronomyEpochReference? epoch = null, string? note = "Altitude") => new(new(id), category, measurement ?? Measurement(42m, AstronomyMeasurementDimension.Angle, "deg", "°"), qualifier, epoch ?? AstronomyEpochReference.ObservationTime, note);
    public static AstronomyHorizontalObservationCoordinate HorizontalCoordinate(AstronomyAngularCoordinateValue? azimuth = null, AstronomyAngularCoordinateValue? altitude = null) => new(azimuth ?? Angular(AstronomyAngularCoordinateComponent.Azimuth, 120m), altitude ?? Angular(AstronomyAngularCoordinateComponent.Altitude, 42m));
    public static AstronomyAngularCoordinateValue Angular(AstronomyAngularCoordinateComponent component, decimal value = 1m, AstronomyMeasurementDimension dimension = AstronomyMeasurementDimension.Angle) => new(component, Measurement(value, dimension, dimension == AstronomyMeasurementDimension.Angle ? "deg" : "mag", dimension == AstronomyMeasurementDimension.Angle ? "°" : "mag"));
    public static AstronomyVisibilityWindow VisibilityWindow(DateTimeOffset? start = null, DateTimeOffset? end = null, AstronomyVisibilityAssessment? assessment = null, DateTimeOffset? peak = null, AstronomyMeasurement? peakAltitude = null, string? note = "visible") => new(new(start ?? T0, end ?? T2), assessment ?? Assessment(), peak ?? T1, peakAltitude ?? Measurement(35m, AstronomyMeasurementDimension.Angle, "deg", "°"), note);
    public static AstronomyVisibilityAssessment Assessment(AstronomyVisibilityStatus status = AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod method = AstronomyVisibilityMethod.NakedEye, IReadOnlyList<AstronomyVisibilityLimitation>? limitations = null, string? summary = "Visible") => new(status, method, limitations ?? [AstronomyVisibilityLimitation.Twilight], summary);
    public static AstronomyMeasurement Measurement(decimal value = 1m, AstronomyMeasurementDimension dimension = AstronomyMeasurementDimension.Dimensionless, string code = "unit", string symbol = "u", AstronomyMeasurementPrecision? precision = null, AstronomyMeasurementUncertainty? uncertainty = null) => new(value, new AstronomyMeasurementUnit(code, symbol, dimension), precision, uncertainty);
    public static AstronomyKnowledgeValidationResult Validate(ITypedAstronomyKnowledgePayload payload, Action<IServiceCollection> add, AstronomyKnowledgeValidationContext? context = null) { var s = new ServiceCollection(); s.AddAstronomyTypedKnowledgePayloadDescriptors(); add(s); using var p=s.BuildServiceProvider(); return p.GetRequiredService<IAstronomyTypedKnowledgeValidator>().Validate(payload, context ?? Context()); }
    public static void AssertIssue(AstronomyKnowledgeValidationIssue issue, string code, AstronomyKnowledgeValidationSeverity severity, string path, string ruleId, AstronomyKnowledgeDomain domain, AstronomyKnowledgePayloadFamily family) { Assert.Equal(code, issue.Code); Assert.Equal(severity, issue.Severity); Assert.Equal(path, issue.Path); Assert.Equal(ruleId, issue.RuleId); Assert.Equal(domain, issue.Domain); Assert.Equal(family, issue.Family); }
}
