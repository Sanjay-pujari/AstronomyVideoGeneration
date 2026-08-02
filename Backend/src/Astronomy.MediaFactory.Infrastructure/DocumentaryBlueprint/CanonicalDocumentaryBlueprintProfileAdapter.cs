using Astronomy.MediaFactory.Core.Certification;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Thin planning projection of the canonical certification-family owner; it cannot resolve an unowned family.</summary>
public sealed class CanonicalDocumentaryBlueprintProfileAdapter(IFamilyCertificationProfileRegistry families)
{
    public const string OrionGoldProfileId = "orion-gold";

    public DocumentaryBlueprintProfile ProjectOrionGold()
    {
        var owner = families.Resolve("CONSTELLATION");
        if (!string.Equals(owner.FamilyId, "CONSTELLATION", StringComparison.Ordinal))
            throw new InvalidOperationException("The canonical CONSTELLATION owner did not resolve.");
        return new(OrionGoldProfileId, "1.0", owner.FamilyId, "Gold", Variant("Long", 12, 600),
            Variant("Short", 4, 120), "CertifiedQuestionAllocation", "CertifiedKnowledgeOnly", "MustNotClaimEnforced");
    }

    private static DocumentaryVariantProfile Variant(string name, int count, int budget)
    {
        var stages = new[] { "Wonder", "Recognition", "Discovery", "Science", "History", "Culture", "ModernAstronomy", "Clarification", "Observation", "Astrophotography", "Inspiration", "Inspiration" };
        var roles = new[] { "OpeningHook", "Orientation", "RecognitionGuide", "ScientificExplanation", "HistoricalContext", "CulturalContext", "CoreDiscovery", "MisconceptionCorrection", "PracticalObservation", "AstrophotographyGuide", "CoreDiscovery", "ReflectiveClosing" };
        var slots = Enumerable.Range(1, count).Select(i => new DocumentaryNarrativeSlot($"{name.ToLowerInvariant()}-{i:00}", i,
            i == count ? "Inspiration" : stages[i - 1], i == count ? "ReflectiveClosing" : roles[i - 1], $"OrionGold{i:00}", [], [], [], false, $"Objective{i:00}",
            $"Outcome{i:00}", i == count ? "Close" : $"Advance{i:00}", 1, true, true, i == count ? "Terminal" : "Continue")
        {
            VisualOpportunityIntent = $"OrionGoldVisual{i:00}", EditorialOutcome = $"Orion Gold scene {i:00} outcome",
            EditorialPriority = i is 1 or 4 ? EditorialPriority.High : EditorialPriority.Medium,
            ObjectiveCuriosityGoal = $"Advance Orion Gold curiosity beat {i:00}",
            ObjectiveEmotionalGoal = $"Deliver Orion Gold emotional beat {i:00}",
            TransitionNextQuestionSeed = i == count ? "Conclude without a new question." : $"Seed Orion Gold question {i + 1:00}",
            TransitionEditorialDirection = i == count ? "Close the documentary." : "Advance to the next certified beat."
        }).ToArray();
        return new(name, true, count, count, count, budget, name == "Long" ? 30 : 20,
            name == "Long" ? 90 : 40, slots, [], [], "ProfileSlotTransitions");
    }
}
