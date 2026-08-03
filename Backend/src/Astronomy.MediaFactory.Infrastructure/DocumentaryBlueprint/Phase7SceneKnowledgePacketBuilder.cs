using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7SceneKnowledgePacketBuilder : IPhase7SceneKnowledgePacketBuilder
{
    private static readonly Regex Placeholder = new(@"OrionGoldVisual\d+|Orion Gold scene \d+ outcome|generic (curiosity beat|emotional beat|visual intent|question text|objective text)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnsafeTime = new(@"Global's sky|\b\d{1,2}:\d{2}\s*(AM|PM)?\s*India Standard Time\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public IReadOnlyList<SceneKnowledgePacket> Build(Phase7CommittedInputAuthority input, string variant)
    {
        if (variant is not ("Long" or "Short")) throw new ArgumentException("Variant must be Long or Short.", nameof(variant));
        var frames = variant == "Long" ? input.LongStoryFrames : input.ShortStoryFrames;
        var order = variant == "Long" ? input.FamilyProfile.LongProfile.PreferredNarrativeOrder : input.FamilyProfile.ShortProfile.BeatKeys;
        return frames.OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).Select((frame, index) => BuildOne(input, frame, variant, order[Math.Min(index, order.Count - 1)], index)).ToArray();
    }

    private static SceneKnowledgePacket BuildOne(Phase7CommittedInputAuthority input, StoryFrameAuthorityFrame frame, string variant, string section, int ordinal)
    {
        var candidateDomains = DomainTerms(section);
        var claims = input.Knowledge.Domains.Where(x => x.Status == KnowledgeDomainStatus.Available)
            .OrderByDescending(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term)))
            .SelectMany(x => x.Claims).DistinctBy(x => x.ClaimId).ToArray();
        var referenced = claims.Where(x => frame.KnowledgeReferenceIds.Contains(x.ClaimId, StringComparer.OrdinalIgnoreCase)
            || x.KnowledgeReferenceIds.Any(k => frame.KnowledgeReferenceIds.Contains(k, StringComparer.OrdinalIgnoreCase))).ToArray();
        var selected = (referenced.Length > 0 ? referenced : claims.Where(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term))).Take(3).ToArray());
        if (selected.Length == 0) selected = claims.Take(1).ToArray();
        var optional = claims.Except(selected).Take(2).ToArray();
        var sourceValues = new[] { frame.NarrativeIntent, frame.VisualIntent, frame.Subject }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var placeholders = sourceValues.Where(x => Placeholder.IsMatch(x)).ToArray();
        var unsafeClaims = selected.Concat(optional).Where(x => UnsafeTime.IsMatch(x.Text)).ToArray();
        var blocking = new List<string>();
        if (selected.Length == 0) blocking.Add($"No certified claim resolves section '{section}'.");
        if (unsafeClaims.Length > 0) blocking.Add("Unsafe universal location/time claim detected.");
        var objective = $"Establish the certified {section.Replace('-', ' ')} knowledge domain.";
        var target = Math.Max(1, (int)Math.Round(frame.EstimatedDuration));
        var packetId = $"packet-{variant.ToLowerInvariant()}-{Phase7Determinism.Hash(new { input.StoryFrameAuthority.Authority.ExecutionId, frame.FrameId })[..20]}";
        var lineage = new SortedDictionary<string,string>(StringComparer.Ordinal)
        {
            ["sourceNarrativeIntent"] = frame.NarrativeIntent, ["sourceVisualIntent"] = frame.VisualIntent,
            ["phase7Enrichment"] = placeholders.Length > 0 ? "generic-upstream-semantic-resolved" : "not-required"
        };
        var draft = new SceneKnowledgePacket(packetId, input.StoryFrameAuthority.Authority.ExecutionId,
            input.StoryFrameAuthority.Authority.PlanId, input.StoryFrameAuthority.Authority.EventId, input.EventFamily,
            input.Language, input.FamilyProfile.ProfileId, input.FamilyProfile.ContractVersion, variant, frame.FrameId,
            Phase7Determinism.Hash(frame), frame.SceneId, Phase7Determinism.Hash(new { frame.SceneId, frame.SceneNumber, frame.SceneRole }),
            frame.SceneNumber, frame.FrameNumber, frame.NarrativeStage, frame.SceneRole, section,
            frame.ViewerQuestionIds.FirstOrDefault() ?? "", null, frame.LearningObjectiveIds.FirstOrDefault() ?? "", objective,
            selected, optional, [], selected.Where(x => x.IsCultural).Select(x => x.Text).ToArray(), input.FamilyProfile.SafetyRules,
            ["Use only packet claims as factual authority.","Do not generate an unqualified location or exact-time assertion."],
            ["Unsupported factual claims", "Universal exact viewing time without location and date"], input.Knowledge.LocalizedVocabulary,
            input.Knowledge.ProtectedTerms, input.Knowledge.PronunciationHints,
            frame.ImageRequirements.Concat(frame.BrollRequirements).Distinct(StringComparer.Ordinal).ToArray(), frame.KnowledgeReferenceIds,
            selected.Concat(optional).SelectMany(x => x.SourceIds).Distinct(StringComparer.Ordinal).ToArray(), target,
            Math.Max(1,(int)Math.Floor(target * .8)), Math.Max(target,(int)Math.Ceiling(target * 1.2)),
            selected.Any(x => x.IsLocationDependent), selected.Any(x => x.IsDateTimeDependent),
            selected.Where(x => x.IsApproximate).Select(x => $"Claim {x.ClaimId} is approximate.").ToArray(),
            selected.Any(x => x.RequiresHumanReview), placeholders.Select(x => $"Resolved upstream placeholder: {x}").ToArray(), blocking,
            lineage, "");
        return draft with { DeterministicChecksum = Phase7Determinism.Hash(draft with { DeterministicChecksum = "" }) };
    }
    private static string[] DomainTerms(string section) => Normalize(section).Split('-', StringSplitOptions.RemoveEmptyEntries)
        .SelectMany(x => x switch { "opening" or "hook" or "closing" or "close" => ["identity","recognition","interestingfacts"], "stars" => ["keyobjects","objects"], "formation" => ["scientificstructure","scientificsignificance"], "photography" => ["astrophotography"], "culture" or "mythology" => ["cultureandmythology","indianorregionaltraditions"], "astrology" => ["astrologyclarification"], _ => new[] { x } }).Distinct().ToArray();
    private static string Normalize(string value) => string.Join('-', value.ToLowerInvariant().Split([' ','_','-'], StringSplitOptions.RemoveEmptyEntries));
}
