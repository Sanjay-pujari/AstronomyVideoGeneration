using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7NarrationRuntimeAuthorityLoader(
    IPhase7KnowledgeCommittedStateEvaluator knowledgeEvaluator,
    IPhase7NarrationPlanningCommittedStateEvaluator planningEvaluator)
    : IPhase7NarrationRuntimeAuthorityLoader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Phase7NarrationRuntimeAuthorityLoadResult> LoadAsync(
        Phase7NarrationRuntimeAuthorityRequest request, CancellationToken token = default)
    {
        var packetPath = Path.Combine(request.ExecutionRoot,
            NarrationPlanningArtifactPaths.PacketCollection.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(packetPath)) return Fail(Phase7NarrationRuntimeAuthorityReasonCodes.PacketCollectionMissing,
            $"Committed packet collection was not found at {NarrationPlanningArtifactPaths.PacketCollection}.");

        SceneKnowledgePacketCollection packets;
        try
        {
            await using var stream = File.OpenRead(packetPath);
            packets = await JsonSerializer.DeserializeAsync<SceneKnowledgePacketCollection>(stream, Json, token)
                ?? throw new JsonException("Packet collection is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        { return Fail(Phase7NarrationRuntimeAuthorityReasonCodes.PacketCollectionMissing, ex.Message); }

        if (packets.DeterministicChecksum != NarrationPlanningCanonicalizer.PacketCollectionChecksum(packets))
            return Fail(Phase7NarrationRuntimeAuthorityReasonCodes.PacketChecksumMismatch,
                "The committed packet collection deterministic checksum does not recompute.");

        var knowledge = await knowledgeEvaluator.EvaluateAsync(new(request.ExecutionRoot, request.ExecutionId,
            request.PlanId, request.EventId, request.Language), token);
        if (!knowledge.IsValid || knowledge.Authority is null)
            return Fail(Phase7NarrationRuntimeAuthorityReasonCodes.Missing, knowledge.Errors.ToArray());

        var planningInput = new Phase7NarrationPlanningInputAuthorityRequest(request.ExecutionRoot, request.ExecutionId,
            request.PlanId, request.EventId, request.Language, request.ProfileId, request.ProfileVersion, packets);
        var planning = await planningEvaluator.EvaluateAsync(planningInput, token);
        if (!planning.IsValid || planning.Authority is null)
            return Fail(Phase7NarrationRuntimeAuthorityReasonCodes.Missing, planning.Errors.ToArray());
        var authority = planning.Authority.Authority;
        if (authority.PacketCollectionChecksum != packets.DeterministicChecksum)
            return Fail(Phase7NarrationRuntimeAuthorityReasonCodes.PacketChecksumMismatch,
                "Planning authority packet checksum differs from the committed packet artifact.");

        var errors = ValidateVariant("Long", authority.LongScenes, packets.Long)
            .Concat(ValidateVariant("Short", authority.ShortScenes, packets.Short)).ToArray();
        if (errors.Length > 0)
            return Fail(errors.Any(x => x.Contains("variant", StringComparison.OrdinalIgnoreCase))
                ? Phase7NarrationRuntimeAuthorityReasonCodes.VariantOwnershipInvalid
                : Phase7NarrationRuntimeAuthorityReasonCodes.PacketPlanningMismatch, errors);
        if (packets.Long.Select(x => x.PacketId).Intersect(packets.Short.Select(x => x.PacketId), StringComparer.Ordinal).Any())
            return Fail(Phase7NarrationRuntimeAuthorityReasonCodes.VariantOwnershipInvalid,
                "Long and Short packet ownership overlaps.");

        return new(true, new(knowledge.Authority, planning.Authority, packets.Long, packets.Short,
            NarrationPlanningArtifactPaths.PacketCollection), Phase7NarrationRuntimeAuthorityReasonCodes.Valid, []);
    }

    private static IEnumerable<string> ValidateVariant(string variant,
        IReadOnlyList<NarrationPlanningScene> scenes, IReadOnlyList<SceneKnowledgePacket> packets)
    {
        if (scenes.Count != packets.Count) yield return $"{variant} planning/packet count mismatch.";
        foreach (var scene in scenes)
        {
            var matches = packets.Where(p => p.PacketId == scene.PacketId).ToArray();
            if (matches.Length != 1) { yield return $"Planning scene {scene.PlanningId} has {matches.Length} packets."; continue; }
            var packet = matches[0];
            if (!packet.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase) ||
                !scene.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase))
                yield return $"Planning scene {scene.PlanningId} has invalid variant ownership.";
            if (packet.StoryFrameId != scene.StoryFrameId || packet.SourceSceneId != scene.SceneId ||
                packet.DeterministicChecksum != Phase7SceneKnowledgePacketCanonicalizer.ComputeChecksum(packet) ||
                packet.DeterministicChecksum != scene.PacketChecksum || packet.BlockingIssues.Count > 0)
                yield return $"Packet {packet.PacketId} identity/checksum/blocking state does not match planning.";
            var required = packet.RequiredClaims.Where(Eligible).Select(c => c.ClaimId).ToHashSet(StringComparer.Ordinal);
            var optional = packet.OptionalClaims.Where(Eligible).Select(c => c.ClaimId).ToHashSet(StringComparer.Ordinal);
            var deferred = packet.DeferredClaims.Select(c => c.ClaimId).ToHashSet(StringComparer.Ordinal);
            if (!scene.RequiredClaims.All(required.Contains)) yield return $"Planning scene {scene.PlanningId} has a missing required claim.";
            if (!scene.OptionalClaims.All(optional.Contains) || !scene.DeferredClaims.All(deferred.Contains))
                yield return $"Planning scene {scene.PlanningId} claim disposition differs from its packet.";
        }
    }

    private static bool Eligible(CertifiedNarrationClaim claim) => !claim.RequiresHumanReview &&
        claim.Disposition is not Phase7ClaimDisposition.Deferred and not Phase7ClaimDisposition.HumanReview;
    private static Phase7NarrationRuntimeAuthorityLoadResult Fail(string code, params string[] errors) => new(false, null, code, errors);
}
