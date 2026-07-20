namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Events;
public static class AstronomyEventValidationCodes
{
    public const string EventKindInvalid="A2.KNOWLEDGE.EVENT.Aggregate.KindInvalid", SignificanceInvalid="A2.KNOWLEDGE.EVENT.Aggregate.SignificanceInvalid", TextIncomplete="A2.KNOWLEDGE.EVENT.Aggregate.TextIncomplete";
    public const string TemporalKindMismatch="A2.KNOWLEDGE.EVENT.Temporal.KindMismatch", TemporalUtcInvalid="A2.KNOWLEDGE.EVENT.Temporal.UtcInvalid", TemporalOrderingInvalid="A2.KNOWLEDGE.EVENT.Temporal.OrderingInvalid";
    public const string ReferenceContextIncompatible="A2.KNOWLEDGE.EVENT.ReferenceContext.Incompatible";
    public const string ParticipantDuplicate="A2.KNOWLEDGE.EVENT.Participants.Duplicate", ParticipantRoleInvalid="A2.KNOWLEDGE.EVENT.Participants.RoleInvalid", ParticipantPrimaryInvalid="A2.KNOWLEDGE.EVENT.Participants.PrimaryInvalid", ParticipantNoteBlank="A2.KNOWLEDGE.EVENT.Participants.NoteBlank";
    public const string PhaseMarkerDuplicate="A2.KNOWLEDGE.EVENT.PhaseMarkers.Duplicate", PhaseMarkerOrderingInvalid="A2.KNOWLEDGE.EVENT.PhaseMarkers.OrderingInvalid", PhaseMarkerUtcInvalid="A2.KNOWLEDGE.EVENT.PhaseMarkers.UtcInvalid", PhaseMarkerOutsideExtent="A2.KNOWLEDGE.EVENT.PhaseMarkers.OutsideExtent";
    public const string GeometryDuplicate="A2.KNOWLEDGE.EVENT.Geometry.Duplicate", GeometryDimensionMismatch="A2.KNOWLEDGE.EVENT.Geometry.DimensionMismatch", GeometryEpochInvalid="A2.KNOWLEDGE.EVENT.Geometry.EpochInvalid", GeometryCategoryInvalid="A2.KNOWLEDGE.EVENT.Geometry.CategoryInvalid";
    public const string CircumstanceDuplicate="A2.KNOWLEDGE.EVENT.Circumstances.Duplicate", CircumstanceValueBlank="A2.KNOWLEDGE.EVENT.Circumstances.ValueBlank", CircumstanceNoteBlank="A2.KNOWLEDGE.EVENT.Circumstances.NoteBlank";
    public const string MeasurementUnitInvalid="A2.KNOWLEDGE.EVENT.Measurement.UnitInvalid", MeasurementPrecisionInvalid="A2.KNOWLEDGE.EVENT.Measurement.PrecisionInvalid", MeasurementUncertaintyInvalid="A2.KNOWLEDGE.EVENT.Measurement.UncertaintyInvalid";
}
