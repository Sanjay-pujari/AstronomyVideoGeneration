using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;

public sealed class AstronomyOrbitalParametersValidationRule : AstronomyKnowledgeValidationRule<AstronomyOrbitalParametersPayload>
{
    public const string Id = "orbital.parameters.integrity";
    public override string RuleId => Id;
    public override AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Orbital;
    public override AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.OrbitalParameter;
    public override int Order => 200;
    protected override IEnumerable<AstronomyKnowledgeValidationIssue> ValidateTyped(AstronomyOrbitalParametersPayload payload, AstronomyKnowledgeValidationContext context)
    {
        if (payload.Parameters.Count == 0) yield return Issue(AstronomyOrbitalValidationCodes.ParameterMissing, AstronomyKnowledgeValidationSeverity.Error, "At least one orbital parameter is required.", "$.parameters");
        var seen = new HashSet<ParameterIdentity>();
        for (var i = 0; i < payload.Parameters.Count; i++)
        {
            var p = payload.Parameters[i];
            if (!p.ParameterId.IsValid) yield return Issue(AstronomyOrbitalValidationCodes.ParameterMissing, AstronomyKnowledgeValidationSeverity.Error, "Orbital parameter ID is required.", $"$.parameters[{i}].parameterId");
            if (!Enum.IsDefined(p.Category)) yield return Issue(AstronomyOrbitalValidationCodes.ParameterMissing, AstronomyKnowledgeValidationSeverity.Error, "Orbital parameter category is not defined.", $"$.parameters[{i}].category");
            if (p.Qualifier.HasValue && !Enum.IsDefined(p.Qualifier.Value)) yield return Issue(AstronomyOrbitalValidationCodes.ParameterQualifierInvalid, AstronomyKnowledgeValidationSeverity.Error, "Orbital parameter qualifier is not defined.", $"$.parameters[{i}].qualifier");
            if (p.Epoch is not null && !Enum.IsDefined(p.Epoch.Kind)) yield return Issue(AstronomyOrbitalValidationCodes.ParameterEpochInvalid, AstronomyKnowledgeValidationSeverity.Error, "Orbital parameter epoch is not defined.", $"$.parameters[{i}].epoch");
            if (p.Note is not null && string.IsNullOrWhiteSpace(p.Note)) yield return Issue(AstronomyOrbitalValidationCodes.ParameterNoteBlank, AstronomyKnowledgeValidationSeverity.Warning, "Orbital parameter note cannot be blank when supplied.", $"$.parameters[{i}].note");
            if (!seen.Add(new(p.ParameterId, p.Qualifier, p.Epoch))) yield return Issue(AstronomyOrbitalValidationCodes.ParameterDuplicate, AstronomyKnowledgeValidationSeverity.Error, "Orbital parameters must be unique by parameter ID, qualifier and epoch.", $"$.parameters[{i}]");
            if (AstronomyOrbitalParameterDimensionCatalog.TryGetExpectedDimension(p.Category, out var expected) && !AstronomyOrbitalMeasurementValidator.HasDimension(p.Measurement, expected)) yield return Issue(AstronomyOrbitalValidationCodes.ParameterDimensionMismatch, AstronomyKnowledgeValidationSeverity.Error, "Orbital parameter measurement dimension does not match the category.", $"$.parameters[{i}].measurement.unit.dimension");
        }
    }
    private AstronomyKnowledgeValidationIssue Issue(string code, AstronomyKnowledgeValidationSeverity severity, string message, string path) => new(code, severity, message, path, RuleId, Domain, Family);
    private readonly record struct ParameterIdentity(AstronomyOrbitalParameterId Id, AstronomyOrbitalParameterQualifier? Qualifier, object? Epoch);
}

internal static class AstronomyOrbitalParameterDimensionCatalog
{
    public static bool TryGetExpectedDimension(AstronomyOrbitalParameterCategory category, out AstronomyMeasurementDimension dimension)
    {
        dimension = category switch
        {
            AstronomyOrbitalParameterCategory.Distance or AstronomyOrbitalParameterCategory.Size => AstronomyMeasurementDimension.Distance,
            AstronomyOrbitalParameterCategory.Period => AstronomyMeasurementDimension.Time,
            AstronomyOrbitalParameterCategory.Rate => AstronomyMeasurementDimension.Velocity,
            AstronomyOrbitalParameterCategory.Orientation or AstronomyOrbitalParameterCategory.Phase => AstronomyMeasurementDimension.Angle,
            AstronomyOrbitalParameterCategory.Shape => AstronomyMeasurementDimension.Dimensionless,
            _ => default
        };
        return category is AstronomyOrbitalParameterCategory.Distance or AstronomyOrbitalParameterCategory.Size or AstronomyOrbitalParameterCategory.Period or AstronomyOrbitalParameterCategory.Rate or AstronomyOrbitalParameterCategory.Orientation or AstronomyOrbitalParameterCategory.Phase or AstronomyOrbitalParameterCategory.Shape;
    }
}
