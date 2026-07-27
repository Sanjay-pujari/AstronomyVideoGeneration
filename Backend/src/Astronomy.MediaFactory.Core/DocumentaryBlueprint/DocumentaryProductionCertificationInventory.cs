namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryProductionCertificationStatus { Certified, Rejected }
public enum DocumentaryProductionCertificationRejectionReason
{
    KnowledgeFoundationNotCertified, ExportMaterializationNotComplete, MediaProjectionNotComplete,
    PipelineExecutionNotComplete, IdentityChainMismatch, CorrelationChainMismatch, ProvenanceChainMismatch,
    TopicChainMismatch, VariantInventoryMismatch, VariantOutputMissing, VariantOutputNotVerified,
    KnowledgeTraceabilityMismatch, NarrationTraceabilityMismatch, SubtitleTraceabilityMismatch,
    VisualTraceabilityMismatch, SceneAssetTraceabilityMismatch, OutputManifestMismatch,
    DeterminismCertificationFailed, NonMutationCertificationFailed, SerializationCertificationFailed,
    ArchitectureBoundaryViolation, DocumentationCertificationFailed
}

public static class DocumentaryProductionCertificationInventory
{
    public const string SchemaVersion = "1.0";
    public static IReadOnlyList<DocumentaryMediaVariantType> VariantTypes { get; } =
        Array.AsReadOnly(new[] { DocumentaryMediaVariantType.LongEnglish, DocumentaryMediaVariantType.LongHindi,
            DocumentaryMediaVariantType.ShortEnglish, DocumentaryMediaVariantType.ShortHindi });
    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) => Array.AsReadOnly((values ?? throw new ArgumentNullException(nameof(values))).ToArray());
}
