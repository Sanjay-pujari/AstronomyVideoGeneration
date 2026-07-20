using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Coordinates;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public class TypedVisibilityKnowledgeTests
{
    [Fact]
    public void VisibilityTaxonomies_AreFrozenAndGuarded()
    {
        Assert.Equal(new[] { "Unknown", "NotVisible", "Marginal", "Visible", "Prominent", "Obscured", "BelowHorizon", "DaylightLimited", "TwilightLimited", "ConditionLimited" }, Enum.GetNames<AstronomyVisibilityStatus>());
        Assert.Equal(new[] { "Unspecified", "NakedEye", "Binocular", "SmallTelescope", "Telescope", "Imaging", "Radio", "Infrared", "Ultraviolet", "XRay", "OtherInstrument" }, Enum.GetNames<AstronomyVisibilityMethod>());
        Assert.Equal(new[] { "None", "BelowHorizon", "Daylight", "Twilight", "Cloud", "AtmosphericExtinction", "PoorSeeing", "LowTransparency", "SkyBrightness", "Moonlight", "TerrainObstruction", "ArtificialObstruction", "InsufficientBrightness", "InsufficientAngularSeparation", "InstrumentSensitivity", "Unknown" }, Enum.GetNames<AstronomyVisibilityLimitation>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyVisibilityAssessment((AstronomyVisibilityStatus)999, AstronomyVisibilityMethod.NakedEye));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, (AstronomyVisibilityMethod)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, [(AstronomyVisibilityLimitation)999]));
    }

    [Fact]
    public void VisibilityAssessment_NormalizesLimitationsAndSummary()
    {
        var input = new List<AstronomyVisibilityLimitation> { AstronomyVisibilityLimitation.Cloud, AstronomyVisibilityLimitation.BelowHorizon };
        var assessment = new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, input, " visible but limited ");
        input.Clear();
        Assert.Equal([AstronomyVisibilityLimitation.BelowHorizon, AstronomyVisibilityLimitation.Cloud], assessment.Limitations);
        Assert.Equal("visible but limited", assessment.Summary);
        Assert.Empty(new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Unknown, AstronomyVisibilityMethod.Unspecified).Limitations);
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, [AstronomyVisibilityLimitation.Cloud, AstronomyVisibilityLimitation.Cloud]));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, [AstronomyVisibilityLimitation.None, AstronomyVisibilityLimitation.Cloud]));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, summary: "bad\n"));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, summary: new string('a', 513)));
        Assert.Equal(assessment.GetHashCode(), new AstronomyVisibilityAssessment(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye, [AstronomyVisibilityLimitation.BelowHorizon, AstronomyVisibilityLimitation.Cloud], "visible but limited").GetHashCode());
    }

    [Fact]
    public void ObservationTimeWindow_RequiresUtcOrderedInstants()
    {
        var start = Utc(0);
        var end = Utc(1);
        Assert.Equal(new AstronomyObservationTimeWindow(start, end), new AstronomyObservationTimeWindow(start, end));
        Assert.Equal(new AstronomyObservationTimeWindow(start, start).StartUtc, start);
        Assert.Throws<ArgumentException>(() => new AstronomyObservationTimeWindow(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.FromHours(1)), end));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationTimeWindow(start, new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => new AstronomyObservationTimeWindow(end, start));
        Assert.DoesNotContain(typeof(AstronomyObservationTimeWindow).GetConstructors().Select(c => c.GetParameters().Length), x => x == 0);
    }

    [Fact]
    public void VisibilityWindow_StoresSuppliedPeakOnlyWhenStructurallyValid()
    {
        var window = Window();
        var assessment = Assessment();
        var visibility = new AstronomyVisibilityWindow(window, assessment, Utc(1), Angle(30), " peak ");
        Assert.Equal("peak", visibility.Note);
        Assert.Equal(visibility, new AstronomyVisibilityWindow(window, assessment, Utc(1), Angle(30), "peak"));
        Assert.Throws<ArgumentNullException>(() => new AstronomyVisibilityWindow(null!, assessment));
        Assert.Throws<ArgumentNullException>(() => new AstronomyVisibilityWindow(window, null!));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityWindow(window, assessment, Utc(3)));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityWindow(window, assessment, new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityWindow(window, assessment, peakAltitude: Scalar()));
        Assert.DoesNotContain(typeof(AstronomyVisibilityWindow).GetMethods().Select(m => m.Name), n => n.Contains("Calculate", StringComparison.OrdinalIgnoreCase) || n.Contains("FindBest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VisibilityWindowsPayload_MapsDomainAndNormalizesWindows()
    {
        var w2 = new AstronomyVisibilityWindow(new AstronomyObservationTimeWindow(Utc(2), Utc(3)), Assessment());
        var w1 = new AstronomyVisibilityWindow(Window(), Assessment());
        var source = new List<AstronomyVisibilityWindow> { w2, w1 };
        var payload = new AstronomyVisibilityWindowsPayload(new("typed.visibility.windows.v1"), Context(), source);
        source.Clear();
        Assert.IsAssignableFrom<ITypedAstronomyKnowledgePayload>(payload);
        Assert.Equal(AstronomyKnowledgeDomain.Observational, payload.Domain);
        Assert.Equal(AstronomyKnowledgePayloadFamily.VisibilityWindow, payload.Family);
        Assert.Equal([w1, w2], payload.Windows);
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityWindowsPayload(default, Context(), [w1]));
        Assert.Throws<ArgumentNullException>(() => new AstronomyVisibilityWindowsPayload(new("typed.visibility.windows.v1"), null!, [w1]));
        Assert.Throws<ArgumentNullException>(() => new AstronomyVisibilityWindowsPayload(new("typed.visibility.windows.v1"), Context(), null!));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityWindowsPayload(new("typed.visibility.windows.v1"), Context(), []));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityWindowsPayload(new("typed.visibility.windows.v1"), Context(), [w1, null!]));
        Assert.Throws<ArgumentException>(() => new AstronomyVisibilityWindowsPayload(new("typed.visibility.windows.v1"), Context(), [w1, w1]));
        Assert.Equal(payload.GetHashCode(), new AstronomyVisibilityWindowsPayload(new("typed.visibility.windows.v1"), Context(), [w1, w2]).GetHashCode());
    }

    private static AstronomyVisibilityAssessment Assessment() => new(AstronomyVisibilityStatus.Visible, AstronomyVisibilityMethod.NakedEye);
    private static AstronomyObservationTimeWindow Window() => new(Utc(0), Utc(2));
    private static DateTimeOffset Utc(int hour) => new(2026, 7, 20, hour, 0, 0, TimeSpan.Zero);
    private static AstronomyMeasurementUnit Unit(AstronomyMeasurementDimension dimension) => new($"unit.{dimension.ToString().ToLowerInvariant()}", "u", dimension);
    private static AstronomyMeasurement Angle(decimal value) => new(value, Unit(AstronomyMeasurementDimension.Angle));
    private static AstronomyMeasurement Scalar() => new(1, Unit(AstronomyMeasurementDimension.Dimensionless));
    private static AstronomyObservationContext Context() => new("site.alpha", Utc(0), coordinateSystem: AstronomyCoordinateSystem.Horizontal);
}
