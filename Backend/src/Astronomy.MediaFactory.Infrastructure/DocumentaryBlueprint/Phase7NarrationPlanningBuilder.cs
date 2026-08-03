using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7NarrationPlanningBuilder(double englishWordsPerMinute = 135, double hindiWordsPerMinute = 120)
    : IPhase7NarrationPlanningBuilder
{
    public VariantNarrationPlan Build(Phase7CommittedInputAuthority input, IReadOnlyList<SceneKnowledgePacket> packets, string variant)
    {
        if (packets.Count == 0 || packets.Any(x => x.Variant != variant)) throw new ArgumentException("Packets must be non-empty and variant-pure.", nameof(packets));
        var rate = input.Language.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? hindiWordsPerMinute : englishWordsPerMinute;
        var scenes = packets.Select((packet,index) => new NarrationScenePlan($"narration-plan-scene-{packet.PacketId}", packet.StoryFrameId,
            packet.SectionKey, packet.SceneObjective, index == 0 ? "Open using a required certified claim." : "Continue without restating the prior scene.",
            packet.RequiredClaims.Select(x => x.ClaimId).ToArray(), packet.OptionalClaims.Select(x => x.ClaimId).ToArray(), packet.ProhibitedClaims,
            $"Progress {index + 1} of {packets.Count} through the {variant} arc.", $"Resolve {packet.SectionKey}.",
            index + 1 == packets.Count ? "Resolve the arc." : "Bridge by shared concept, without adding facts.", "Use only an earlier packet claim if explicitly selected.",
            index + 1 == packets.Count ? "Close without introducing a new factual claim." : "Not a closing scene.", packet.TargetDurationSeconds,
            WordRange(packet.TargetDurationSeconds, rate), packet.SafetyRules,
            input.Language.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? ["Use certified Hindi vocabulary directly.","Preserve protected terms."] : ["Use natural documentary English.","Preserve protected terms."],
            packet.HumanReviewRequired)).ToArray();
        var draft = new VariantNarrationPlan($"phase7-{variant.ToLowerInvariant()}-plan-{input.StoryFrameAuthority.Authority.ExecutionId}",
            input.StoryFrameAuthority.Authority.ExecutionId, input.StoryFrameAuthority.Authority.EventId, input.EventFamily, input.Language,
            input.FamilyProfile.ProfileId, input.FamilyProfile.ContractVersion, variant, input.StoryFrameAuthority.Authority.AuthorityId,
            input.StoryFrameAuthority.Authority.SemanticChecksum, scenes.Length, scenes.Sum(x => x.TargetDurationSeconds), scenes, "");
        return draft with { DeterministicChecksum = Phase7Determinism.Hash(draft with { DeterministicChecksum = "" }) };
    }
    private static NarrationWordRange WordRange(int seconds, double preferredRate)
    {
        var preferred = (int)Math.Round(seconds * preferredRate / 60d);
        return new((int)Math.Floor(preferred * .925), preferred, (int)Math.Ceiling(preferred * 1.075));
    }
}
