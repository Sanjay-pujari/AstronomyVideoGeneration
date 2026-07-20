namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Orbital;

public static class AstronomyOrbitalValidationCodes
{
    public const string ReferenceContextInvalid = "orbital.reference-context.invalid";
    public const string CentralBodyMissing = "orbital.reference-context.central-body.missing";
    public const string FrameOriginMismatch = "orbital.reference-context.frame-origin.mismatch";
    public const string EpochInvalid = "orbital.reference-context.epoch.invalid";
    public const string ElementMissing = "orbital.keplerian.element.missing";
    public const string ElementDuplicate = "orbital.keplerian.element.duplicate";
    public const string ElementDimensionMismatch = "orbital.keplerian.element.dimension-mismatch";
    public const string ElementValueOutOfRange = "orbital.keplerian.element.value-out-of-range";
    public const string ParameterMissing = "orbital.parameter.missing";
    public const string ParameterDuplicate = "orbital.parameter.duplicate";
    public const string ParameterDimensionMismatch = "orbital.parameter.dimension-mismatch";
    public const string ParameterQualifierInvalid = "orbital.parameter.qualifier.invalid";
    public const string ParameterEpochInvalid = "orbital.parameter.epoch.invalid";
    public const string ParameterNoteBlank = "orbital.parameter.note.blank";
    public const string MeasurementUnitInvalid = "orbital.measurement.unit.invalid";
    public const string MeasurementPrecisionInvalid = "orbital.measurement.precision.invalid";
    public const string MeasurementUncertaintyInvalid = "orbital.measurement.uncertainty.invalid";
}
