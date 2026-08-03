using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7SceneKnowledgePacketBuilder : IPhase7SceneKnowledgePacketBuilder
{
    private readonly IPhase7KnowledgeReferenceResolver referenceResolver;
    public Phase7SceneKnowledgePacketBuilder() : this(new Phase7KnowledgeReferenceResolver()) { }
    public Phase7SceneKnowledgePacketBuilder(IPhase7KnowledgeReferenceResolver referenceResolver) => this.referenceResolver = referenceResolver;
    private static readonly Regex Placeholder = new(@"OrionGoldVisual\d+|Orion Gold scene \d+ outcome|generic (curiosity beat|emotional beat|visual intent|question text|objective text)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnsafeTime = new(@"Global's sky|\b\d{1,2}:\d{2}\s*(AM|PM)?\s*India Standard Time\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public IReadOnlyList<SceneKnowledgePacket> Build(Phase7CommittedInputAuthority input, string variant)
    {
        if (variant is not ("Long" or "Short")) throw new ArgumentException("Variant must be Long or Short.", nameof(variant));
        var frames = variant == "Long" ? input.LongStoryFrames : input.ShortStoryFrames;
        var order = variant == "Long" ? input.FamilyProfile.LongProfile.PreferredNarrativeOrder : input.FamilyProfile.ShortProfile.BeatKeys;
        return frames.OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).Select((frame, index) => BuildOne(input, frame, variant, order[Math.Min(index, order.Count - 1)], index)).ToArray();
    }

    private SceneKnowledgePacket BuildOne(Phase7CommittedInputAuthority input, StoryFrameAuthorityFrame frame, string variant, string section, int ordinal)
    {
        var candidateDomains = DomainTerms(section);
        var claims = input.Knowledge.Domains.Where(x => x.Status == KnowledgeDomainStatus.Available)
            .OrderByDescending(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term)))
            .SelectMany(x => x.Claims).DistinctBy(x => x.ClaimId).ToArray();
        var resolutions = referenceResolver.Resolve(frame.KnowledgeReferenceIds, input.Knowledge);
        var referenced = resolutions.Where(x=>x.Status==Phase7KnowledgeReferenceStatus.Resolved).SelectMany(x=>x.Claims)
            .DistinctBy(x=>x.ClaimId).Select(x=>x with { SelectionReason="ExactStoryFrameReference" }).ToArray();
        var selected = (referenced.Length > 0 ? referenced : claims.Where(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term))).Take(3).Select(x=>x with { SelectionReason="FamilySceneSectionRequirement" }).ToArray());
        if (selected.Length == 0) selected = claims.Take(1).ToArray();
        var optional = claims.Except(selected).Take(2).ToArray();
        var sourceValues = new[] { frame.NarrativeIntent, frame.VisualIntent, frame.Subject }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var placeholders = sourceValues.Where(x => Placeholder.IsMatch(x)).ToArray();
        var unsafeClaims = selected.Concat(optional).Where(x => UnsafeTime.IsMatch(x.Text)).ToArray();
        var blocking = new List<string>();
        blocking.AddRange(resolutions.Where(x=>x.Status is Phase7KnowledgeReferenceStatus.Missing or Phase7KnowledgeReferenceStatus.Ambiguous or Phase7KnowledgeReferenceStatus.CrossVariantInvalid or Phase7KnowledgeReferenceStatus.Unsupported).Select(x=>$"{x.ReasonCode}:{x.ReferenceId}"));
        if (selected.Length == 0) blocking.Add($"No certified claim resolves section '{section}'.");
        if (unsafeClaims.Length > 0) blocking.Add("Unsafe universal location/time claim detected.");
        var subject = selected.FirstOrDefault()?.Text ?? frame.Subject;
        var objective = Objective(section, subject);
        var question = ViewerQuestion(section, subject);
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
            frame.ViewerQuestionIds.FirstOrDefault() ?? "", question, frame.LearningObjectiveIds.FirstOrDefault() ?? "", objective,
            selected, optional, [], selected.Where(x => x.IsCultural).Select(x => x.Text).ToArray(), input.FamilyProfile.SafetyRules,
            ["Use only packet claims as factual authority.","Do not generate an unqualified location or exact-time assertion."],
            ["Unsupported factual claims", "Universal exact viewing time without location and date"], input.Knowledge.LocalizedVocabulary,
            input.Knowledge.ProtectedTerms, input.Knowledge.PronunciationHints,
            selected.Concat(optional).SelectMany(x=>x.KnowledgeReferenceIds).Where(IsVisualIdentity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), frame.KnowledgeReferenceIds,
            selected.Concat(optional).SelectMany(x => x.SourceIds).Distinct(StringComparer.Ordinal).ToArray(), target,
            Math.Max(1,(int)Math.Floor(target * .8)), Math.Max(target,(int)Math.Ceiling(target * 1.2)),
            selected.Any(x => x.IsLocationDependent), selected.Any(x => x.IsDateTimeDependent),
            selected.Where(x => x.IsApproximate).Select(x => $"Claim {x.ClaimId} is approximate.").ToArray(),
            selected.Any(x => x.RequiresHumanReview), placeholders.Select(x => $"Resolved upstream placeholder: {x}").ToArray(), blocking,
            lineage, "") { ResolvedViewerQuestionText=question, VisualPlanningLineage=frame.ImageRequirements.Concat(frame.BrollRequirements).Distinct(StringComparer.Ordinal).ToArray() };
        draft = draft with { ViewerQuestionResolutionChecksum=Phase7Determinism.Hash(new { draft.SourceViewerQuestionId, question, section, claimIds=selected.Select(x=>x.ClaimId) }) };
        return draft with { DeterministicChecksum = Phase7Determinism.Hash(draft with { DeterministicChecksum = "" }) };
    }
    private static string[] DomainTerms(string section) => Normalize(section).Split('-', StringSplitOptions.RemoveEmptyEntries)
        .SelectMany(x => x switch { "opening" or "hook" or "closing" or "close" => ["identity","recognition","interestingfacts"], "stars" => ["keyobjects","objects"], "formation" => ["scientificstructure","scientificsignificance"], "photography" => ["astrophotography"], "culture" or "mythology" => ["cultureandmythology","indianorregionaltraditions"], "astrology" => ["astrologyclarification"], _ => new[] { x } }).Distinct().ToArray();
    private static string Normalize(string value) => string.Join('-', value.ToLowerInvariant().Split([' ','_','-'], StringSplitOptions.RemoveEmptyEntries));
    private static bool IsVisualIdentity(string value) => Regex.IsMatch(value, @"^(constellation|star|object|messier|planet|moon|comet|satellite)\.[a-z0-9.-]+$", RegexOptions.IgnoreCase);
    private static string Objective(string section,string subject) => section switch
    {
        var x when x.Contains("recognition",StringComparison.OrdinalIgnoreCase)||x is "hook" or "opening-recognition" => $"Help the viewer recognize the subject using this certified evidence: {subject}",
        var x when x.Contains("observation",StringComparison.OrdinalIgnoreCase)||x.Contains("viewing",StringComparison.OrdinalIgnoreCase) => $"Give qualified, location-aware observing guidance grounded in: {subject}",
        var x when x.Contains("culture",StringComparison.OrdinalIgnoreCase)||x.Contains("astrology",StringComparison.OrdinalIgnoreCase) => $"Separate tradition-specific context from scientific identity using: {subject}",
        var x when x.Contains("close",StringComparison.OrdinalIgnoreCase) => $"Reconnect recognition, science, and safe observation without adding a new claim: {subject}",
        _ => $"Explain the scene's certified {section.Replace('-',' ')} evidence through: {subject}"
    };
    private static string ViewerQuestion(string section,string subject) => section switch
    {
        var x when x.Contains("recognition",StringComparison.OrdinalIgnoreCase)||x is "hook" => "What makes this subject easy to recognize?",
        var x when x.Contains("observation",StringComparison.OrdinalIgnoreCase)||x.Contains("viewing",StringComparison.OrdinalIgnoreCase) => "How can you find it safely from your location and season?",
        var x when x.Contains("geometry",StringComparison.OrdinalIgnoreCase)||x.Contains("distance",StringComparison.OrdinalIgnoreCase) => "What does its apparent geometry reveal about real distance?",
        var x when x.Contains("culture",StringComparison.OrdinalIgnoreCase)||x.Contains("astrology",StringComparison.OrdinalIgnoreCase) => "How do cultural traditions differ from modern astronomical classification?",
        _ => $"What certified evidence explains this {section.Replace('-',' ')} scene?"
    };
}
