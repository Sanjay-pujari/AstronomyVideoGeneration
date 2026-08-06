namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class Phase7NarrationRuntimeAuthorityReasonCodes
{
    public const string Missing = "P7_RUNTIME_AUTHORITY_MISSING";
    public const string PacketCollectionMissing = "P7_RUNTIME_PACKET_COLLECTION_MISSING";
    public const string PacketChecksumMismatch = "P7_RUNTIME_PACKET_CHECKSUM_MISMATCH";
    public const string PacketPlanningMismatch = "P7_RUNTIME_PACKET_PLANNING_MISMATCH";
    public const string RequiredClaimMissing = "P7_RUNTIME_REQUIRED_CLAIM_MISSING";
    public const string VariantOwnershipInvalid = "P7_RUNTIME_VARIANT_OWNERSHIP_INVALID";
    public const string Valid = "P7_RUNTIME_AUTHORITY_VALID";
}

public sealed record Phase7NarrationRuntimeAuthority(
    PublishedPhase7KnowledgeAuthority KnowledgeAuthority,
    PublishedNarrationPlanningAuthority PlanningAuthority,
    IReadOnlyList<SceneKnowledgePacket> LongPackets,
    IReadOnlyList<SceneKnowledgePacket> ShortPackets,
    string PacketArtifactPath);

public sealed record Phase7NarrationRuntimeAuthorityRequest(string ExecutionRoot, string ExecutionId,
    string PlanId, string EventId, string Language, string ProfileId, string ProfileVersion);

public sealed record Phase7NarrationRuntimeAuthorityLoadResult(bool IsValid,
    Phase7NarrationRuntimeAuthority? Authority, string ReasonCode, IReadOnlyList<string> Errors);

public interface IPhase7NarrationRuntimeAuthorityLoader
{
    Task<Phase7NarrationRuntimeAuthorityLoadResult> LoadAsync(
        Phase7NarrationRuntimeAuthorityRequest request, CancellationToken token = default);
}
