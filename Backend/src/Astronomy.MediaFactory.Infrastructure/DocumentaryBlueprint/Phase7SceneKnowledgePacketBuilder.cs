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

    public IReadOnlyList<SceneKnowledgePacket> Build(Phase7ScenePacketInputAuthority authority, string variant)
    {
        var k = authority.Knowledge.KnowledgeAuthority;
        var domains = k.Claims.GroupBy(x => x.Domain, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new NarrationKnowledgeDomain(x.Key, KnowledgeDomainStatus.Available,
                x.OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray(), [])).ToArray();
        var resolved = new ResolvedNarrationKnowledge(k.EventKnowledgePayloadId, k.EventKnowledgeChecksum,
            k.SourceRegistryId, k.SourceRegistryChecksum, k.Language, domains, new Dictionary<string,string>(), [],
            new Dictionary<string,string>(), k.Sources.Select(x => x.SourceId).Order(StringComparer.Ordinal).ToArray(),
            k.Warnings, k.BlockingIssues, k.SemanticChecksum) { KnowledgeEntities = k.KnowledgeEntities,
                ClaimSupportEvidence = k.ClaimSupportEvidence };
        var legacy = new Phase7CommittedInputAuthority(authority.StoryFrames, authority.EventFamily, authority.EventType,
            authority.Language, authority.ProfileId, authority.ProfileVersion, authority.EventId,
            k.EventKnowledgePayloadId, k.EventKnowledgeChecksum, k.SourceRegistryId, k.SourceRegistryChecksum,
            k.EvergreenPayloadId, k.EvergreenChecksum, authority.FamilyProfile, authority.LongStoryFrames,
            authority.ShortStoryFrames, authority.LongSourceScenes, authority.ShortSourceScenes,
            authority.LineageEvidence.Select(x => $"{x.Key}:{x.Value}").ToArray(),
            authority.Knowledge.ArtifactPaths.Concat(authority.StoryFrames.ArtifactPaths).Distinct().ToArray(),
            authority.RuntimeCompatibilityEvidence, resolved);
        return Build(legacy, variant);
    }

    private SceneKnowledgePacket BuildOne(Phase7CommittedInputAuthority input, StoryFrameAuthorityFrame frame, string variant, string section, int ordinal)
    {
        var sourceScenes = variant == "Long" ? input.LongSourceScenes : input.ShortSourceScenes;
        if (sourceScenes.Count(x => x.SceneId == frame.SceneId && x.SceneNumber == frame.SceneNumber) != 1)
            throw new InvalidOperationException($"Story Frame '{frame.FrameId}' must have exactly one source-scene lineage row.");
        var candidateDomains = DomainTerms(section);
        var claims = input.Knowledge.Domains.Where(x => x.Status == KnowledgeDomainStatus.Available)
            .OrderByDescending(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term)))
            .SelectMany(x => x.Claims).DistinctBy(x => x.ClaimId)
            .OrderBy(x => x.ClaimId, StringComparer.Ordinal).ToArray();
        var resolutions = referenceResolver.Resolve(frame.KnowledgeReferenceIds, input.Knowledge);
        var referenced = resolutions.Where(x=>x.Status==Phase7KnowledgeReferenceStatus.Resolved).SelectMany(x=>x.Claims)
            .DistinctBy(x=>x.ClaimId).Select(x=>x with { SelectionReason="ExactStoryFrameReference" }).ToArray();
        var selected = (referenced.Length > 0 ? referenced : claims.Where(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term))).Take(3).Select(x=>x with { SelectionReason="FamilySceneSectionRequirement" }).ToArray())
            .Where(x => x.Disposition == Phase7ClaimDisposition.Required && !x.RequiresHumanReview).ToArray();
        if (selected.Length == 0) selected = claims.Where(x => x.Disposition == Phase7ClaimDisposition.Required && !x.RequiresHumanReview).Take(1).ToArray();
        var optional = claims.Where(x => x.Disposition == Phase7ClaimDisposition.Optional && !x.RequiresHumanReview)
            .Where(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term))).Take(2).ToArray();
        var deferred = claims.Where(x => x.Disposition == Phase7ClaimDisposition.Deferred).ToArray();
        var review = claims.Where(x => x.Disposition == Phase7ClaimDisposition.HumanReview || x.RequiresHumanReview)
            .Where(x => candidateDomains.Any(term => Normalize(x.Domain).Contains(term))).ToArray();
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
        var storyChecksum = Phase7Determinism.Hash(frame);
        var packetId = $"packet-{variant.ToLowerInvariant()}-{Phase7Determinism.Hash(new { input.StoryFrameAuthority.Authority.ExecutionId, variant, frame.FrameId, storyChecksum, required=selected.Select(x=>x.ClaimId).Order(), optional=optional.Select(x=>x.ClaimId).Order(), contract=Phase7ScenePacketContract.Version })[..20]}";
        var lineage = new SortedDictionary<string,string>(StringComparer.Ordinal)
        {
            ["sourceNarrativeIntent"] = frame.NarrativeIntent, ["sourceVisualIntent"] = frame.VisualIntent,
            ["phase7Enrichment"] = placeholders.Length > 0 ? "generic-upstream-semantic-resolved" : "not-required"
        };
        var draft = new SceneKnowledgePacket(packetId, input.StoryFrameAuthority.Authority.ExecutionId,
            input.StoryFrameAuthority.Authority.PlanId, input.StoryFrameAuthority.Authority.EventId, input.EventFamily,
            input.Language, input.FamilyProfile.ProfileId, input.FamilyProfile.ContractVersion, variant, frame.FrameId,
            storyChecksum, frame.SceneId, Phase7Determinism.Hash(new { frame.SceneId, frame.SceneNumber, frame.SceneRole }),
            frame.SceneNumber, frame.FrameNumber, frame.NarrativeStage, frame.SceneRole, section,
            frame.ViewerQuestionIds.FirstOrDefault() ?? "", question, frame.LearningObjectiveIds.FirstOrDefault() ?? "", objective,
            selected, optional, deferred, selected.Concat(optional).Where(x => x.IsCultural && x.RequiresQualification).Select(x => x.Text).ToArray(), input.FamilyProfile.SafetyRules,
            ["Use only packet claims as factual authority.","Do not generate an unqualified location or exact-time assertion."],
            ["Unsupported factual claims", "Universal exact viewing time without location and date"], input.Knowledge.LocalizedVocabulary,
            input.Knowledge.ProtectedTerms, input.Knowledge.PronunciationHints,
            selected.Concat(optional).SelectMany(x=>x.KnowledgeReferenceIds).Where(IsVisualIdentity)
                .Where(x=>input.Knowledge.KnowledgeEntities.Any(e=>e.KnowledgeId==x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), frame.KnowledgeReferenceIds,
            selected.Concat(optional).SelectMany(x => x.SourceIds).Distinct(StringComparer.Ordinal).ToArray(), target,
            Math.Max(1,(int)Math.Floor(target * .8)), Math.Max(target,(int)Math.Ceiling(target * 1.2)),
            selected.Any(x => x.IsLocationDependent), selected.Any(x => x.IsDateTimeDependent),
            selected.Where(x => x.IsApproximate).Select(x => $"Claim {x.ClaimId} is approximate.").ToArray(),
            review.Length > 0, placeholders.Select(x => $"Resolved upstream placeholder: {x}").Concat(review.Select(x => $"Human review context excluded: {x.ClaimId}")).ToArray(), blocking,
            lineage, "") { ResolvedViewerQuestionText=question,
                ViewerQuestionResolutionReason=frame.ViewerQuestionIds.Count>0?"CertifiedPhase6ViewerQuestionIdentity":"GovernedFamilySceneRoleFallback",
                VisualPlanningLineage=frame.ImageRequirements.Concat(frame.BrollRequirements).Distinct(StringComparer.Ordinal).ToArray() };
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
