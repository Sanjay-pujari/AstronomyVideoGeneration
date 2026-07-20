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

public sealed class AstronomyObservationalMeasurementValidationTests
{
    [Fact]
    public void Valid_measurement_passes() =>
        Assert.Empty(ObservationalVisibilityValidationFixture.Validate(
            ObservationalVisibilityValidationFixture.ValidObservationPayload(),
            s => s.AddAstronomyObservationalValidation()).Issues);

    [Fact]
    public void Significant_figures_zero_is_reported_as_invalid_precision()
    {
        var result = ObservationalVisibilityValidationFixture.Validate(
            new AstronomyObservationConditionsPayload(
                new("typed.observational.conditions.v1"),
                ObservationalVisibilityValidationFixture.ObservationContext(),
                ObservationalVisibilityValidationFixture.Conditions(
                    limitingMagnitude: ObservationalVisibilityValidationFixture.Measurement(
                        1,
                        AstronomyMeasurementDimension.Magnitude,
                        "mag",
                        "mag",
                        new AstronomyMeasurementPrecision(AstronomyPrecisionKind.SignificantFigures, 0))),
                [],
                ObservationalVisibilityValidationFixture.HorizontalCoordinate()),
            s => s.AddAstronomyObservationalValidation());

        var issue = Assert.Single(
            result.Issues,
            issue =>
                issue.Code ==
                AstronomyObservationalValidationCodes
                    .MeasurementPrecisionInvalid);

        ObservationalVisibilityValidationFixture.AssertIssue(
            issue,
            AstronomyObservationalValidationCodes
                .MeasurementPrecisionInvalid,
            AstronomyKnowledgeValidationSeverity.Error,
            "$.conditions.limitingMagnitude.precision",
            AstronomyObservationConditionsValidationRule.Id,
            AstronomyKnowledgeDomain.Observational,
            AstronomyKnowledgePayloadFamily
                .ObservationCondition);
    }

    [Fact]
    public void Invalid_unit_uses_measurement_unit_code() =>
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementUnit(" ", "u", AstronomyMeasurementDimension.Angle));

    [Fact]
    public void Invalid_precision_does_not_use_context_code() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyMeasurementPrecision(AstronomyPrecisionKind.DecimalPlaces, -1));

    [Fact]
    public void Invalid_uncertainty_uses_measurement_uncertainty_code_or_is_constructor_protected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AstronomyMeasurementUncertainty.SymmetricAbsolute(-1));

    [Fact]
    public void Measurement_validator_is_actually_invoked_by_registered_rule()
    {
        var measurement =
            new AstronomyMeasurement(
                1m,
                new AstronomyMeasurementUnit(
                    "mag",
                    "mag",
                    AstronomyMeasurementDimension.Magnitude),
                new AstronomyMeasurementPrecision(
                    AstronomyPrecisionKind.SignificantFigures,
                    0));

        var payload = new AstronomyObservationConditionsPayload(
            new("typed.observational.conditions.v1"),
            ObservationalVisibilityValidationFixture.ObservationContext(),
            ObservationalVisibilityValidationFixture.Conditions(limitingMagnitude: measurement),
            [],
            ObservationalVisibilityValidationFixture.HorizontalCoordinate());

        var result = ObservationalVisibilityValidationFixture.Validate(
            payload,
            s => s.AddAstronomyObservationalValidation());

        Assert.Contains(
            result.Issues,
            issue => issue.Code == AstronomyObservationalValidationCodes.MeasurementPrecisionInvalid);
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Code == AstronomyObservationalValidationCodes.QuantityDimensionMismatch);
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Code.StartsWith("observational.context.", StringComparison.Ordinal));
    }

    [Fact]
    public void Precision_kind_policy_is_explicit_for_every_member()
    {
        var explicitlySupported = new[]
        {
            AstronomyPrecisionKind.DecimalPlaces,
            AstronomyPrecisionKind.SignificantFigures
        };
        var deliberatelyUnsupported = Array.Empty<AstronomyPrecisionKind>();

        Assert.Equal(
            Enum.GetValues<AstronomyPrecisionKind>().OrderBy(kind => kind),
            explicitlySupported.Concat(deliberatelyUnsupported).OrderBy(kind => kind));
    }

    [Theory]
    [InlineData(AstronomyPrecisionKind.DecimalPlaces, 0)]
    [InlineData(AstronomyPrecisionKind.DecimalPlaces, AstronomyMeasurementPrecision.MaxDigits)]
    [InlineData(AstronomyPrecisionKind.SignificantFigures, 1)]
    [InlineData(AstronomyPrecisionKind.SignificantFigures, AstronomyMeasurementPrecision.MaxDigits)]
    public void Supported_precision_policy_values_pass(AstronomyPrecisionKind kind, int digits)
    {
        var result = ValidateLimitingMagnitudePrecision(new AstronomyMeasurementPrecision(kind, digits));

        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Code == AstronomyObservationalValidationCodes.MeasurementPrecisionInvalid);
    }

    [Fact]
    public void Significant_figures_zero_fails_precision_policy()
    {
        var result = ValidateLimitingMagnitudePrecision(
            new AstronomyMeasurementPrecision(AstronomyPrecisionKind.SignificantFigures, 0));

        Assert.Contains(
            result.Issues,
            issue => issue.Code == AstronomyObservationalValidationCodes.MeasurementPrecisionInvalid);
    }

    [Fact]
    public void Digits_above_max_are_constructor_protected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AstronomyMeasurementPrecision(
                AstronomyPrecisionKind.DecimalPlaces,
                AstronomyMeasurementPrecision.MaxDigits + 1));

    [Fact]
    public void Undefined_precision_kind_is_constructor_protected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AstronomyMeasurementPrecision((AstronomyPrecisionKind)999, 1));

    private static AstronomyKnowledgeValidationResult ValidateLimitingMagnitudePrecision(
        AstronomyMeasurementPrecision precision) =>
        ObservationalVisibilityValidationFixture.Validate(
            new AstronomyObservationConditionsPayload(
                new("typed.observational.conditions.v1"),
                ObservationalVisibilityValidationFixture.ObservationContext(),
                ObservationalVisibilityValidationFixture.Conditions(
                    limitingMagnitude: ObservationalVisibilityValidationFixture.Measurement(
                        1,
                        AstronomyMeasurementDimension.Magnitude,
                        "mag",
                        "mag",
                        precision)),
                [],
                ObservationalVisibilityValidationFixture.HorizontalCoordinate()),
            s => s.AddAstronomyObservationalValidation());
}
