using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7SceneKnowledgePacketBuilder : IPhase7SceneKnowledgePacketBuilder
{
    private static readonly Regex NeutralPlaceholder = new(@"^(?:tbd|todo|placeholder|unknown|not[- ]?provided)(?:\W|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IPhase7KnowledgeReferenceResolver resolver;
    public Phase7SceneKnowledgePacketBuilder() : this(new Phase7KnowledgeReferenceResolver()) { }
    public Phase7SceneKnowledgePacketBuilder(IPhase7KnowledgeReferenceResolver resolver) => this.resolver = resolver;

    [Obsolete("P7.1B packets require the complete typed committed authority, including its resolution report.")]
    public IReadOnlyList<SceneKnowledgePacket> Build(Phase7CommittedInputAuthority authority, string variant) =>
        throw new NotSupportedException("Use Build(Phase7ScenePacketInputAuthority, string); lossy legacy conversion is prohibited.");

    public IReadOnlyList<SceneKnowledgePacket> Build(Phase7ScenePacketInputAuthority authority, string variant)
    {
        if (variant is not ("Long" or "Short")) throw new ArgumentException("Variant must be Long or Short.", nameof(variant));
        if (authority.Knowledge.ResolvedNarrationKnowledge is null)
            throw new InvalidOperationException("The physically validated committed resolution report is required.");
        var frames = variant == "Long" ? authority.LongStoryFrames : authority.ShortStoryFrames;
        return frames.OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber)
            .Select(frame => BuildOne(authority, frame, variant)).ToArray();
    }

    private SceneKnowledgePacket BuildOne(Phase7ScenePacketInputAuthority input, StoryFrameAuthorityFrame frame, string variant)
    {
        var resolutionReport = input.Knowledge.ResolvedNarrationKnowledge!;
        var sourceRows = variant == "Long" ? input.LongSourceScenes : input.ShortSourceScenes;
        var matches = sourceRows.Where(x => x.Variant == variant && x.SceneId == frame.SceneId && x.SceneNumber == frame.SceneNumber).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"Story Frame '{frame.FrameId}' must have exactly one source-scene lineage row in {variant}.");
        var source = matches[0];
        var sectionAuthority = new Phase7SceneSectionAuthorityResolver().Resolve(frame, source);
        if (!sectionAuthority.IsValid) throw new InvalidOperationException($"{sectionAuthority.ReasonCode}:{frame.FrameId}");
        var section = sectionAuthority.SectionKey;
        if (!input.ReferenceRequirements.TryGetValue(frame.FrameId, out var requirements))
            throw new InvalidOperationException($"P7PACKET_REFERENCE_REQUIREMENTS_UNRESOLVED:{frame.FrameId}");
        var otherIds = (variant == "Long" ? input.ShortStoryFrames : input.LongStoryFrames)
            .SelectMany(x => x.KnowledgeReferenceIds).Distinct(StringComparer.Ordinal).ToArray();
        var resolved = requirements.Select(r => (Requirement:r, Resolution:resolver.Resolve(
            new Phase7KnowledgeReferenceRequest(r.ReferenceId, r.Variant, !r.IsRequired, otherIds), input))).ToArray();
        var exactRequired = resolved.Where(x => x.Requirement.IsRequired && x.Resolution.Status == Phase7KnowledgeReferenceStatus.Resolved)
            .SelectMany(x => x.Resolution.Claims).DistinctBy(x => x.ClaimId, StringComparer.Ordinal);
        var exactOptional = resolved.Where(x => !x.Requirement.IsRequired && x.Resolution.Status == Phase7KnowledgeReferenceStatus.Resolved)
            .SelectMany(x => x.Resolution.Claims).DistinctBy(x => x.ClaimId, StringComparer.Ordinal);
        var allowedDomains = DomainTerms(section, source.SceneRole, source.NarrativeStage, frame.LearningObjectiveIds);
        bool Relevant(CertifiedNarrationClaim c) => allowedDomains.Contains(Normalize(c.Domain)) ||
            resolved.SelectMany(x => x.Resolution.Claims).Any(x => x.ClaimId == c.ClaimId);
        var authorityClaims = input.Knowledge.KnowledgeAuthority.Claims;
        var required = exactRequired.Concat(authorityClaims.Where(c => c.Disposition == Phase7ClaimDisposition.Required && Relevant(c)))
            .Where(c => c.Disposition == Phase7ClaimDisposition.Required && !c.RequiresHumanReview && HasRequiredEvidence(c, input))
            .DistinctBy(c => c.ClaimId, StringComparer.Ordinal).OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
        var optional = exactOptional.Concat(authorityClaims.Where(c => c.Disposition == Phase7ClaimDisposition.Optional && Relevant(c)))
            .Where(c => c.Disposition == Phase7ClaimDisposition.Optional && !c.RequiresHumanReview && HasOptionalEvidence(c, input))
            .DistinctBy(c => c.ClaimId, StringComparer.Ordinal).OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
        var deferred = authorityClaims.Where(c => c.Disposition == Phase7ClaimDisposition.Deferred && Relevant(c))
            .OrderBy(c => c.ClaimId, StringComparer.Ordinal).ToArray();
        var review = authorityClaims.Where(c => Relevant(c) && (c.RequiresHumanReview || c.Disposition == Phase7ClaimDisposition.HumanReview)).ToArray();
        var blocking = resolved.Where(x => x.Requirement.IsRequired && x.Resolution.Status != Phase7KnowledgeReferenceStatus.Resolved)
            .Select(x => $"{x.Resolution.ReasonCode}:{x.Requirement.ReferenceId}").ToList();
        if (!requirements.Any(x => x.IsPrimary)) blocking.Add($"P7PACKET_PRIMARY_REFERENCE_MISSING:{frame.FrameId}");
        if (required.Length == 0) blocking.Add($"P7PACKET_REQUIRED_CLAIM_MISSING:{frame.FrameId}:{section}");
        var warnings = resolved.Where(x => !x.Requirement.IsRequired && x.Resolution.Status != Phase7KnowledgeReferenceStatus.Resolved)
            .Select(x => $"{x.Resolution.ReasonCode}:{x.Requirement.ReferenceId}").ToList();
        warnings.AddRange(review.Select(x => $"Human review context excluded: {x.ClaimId}"));
        warnings.AddRange(new[] { frame.NarrativeIntent, frame.VisualIntent, frame.Subject }.Where(IsPlaceholder)
            .Select(_ => "A governed neutral upstream placeholder requires editorial completion."));
        var question = "What should the viewer understand from the certified evidence in this scene?";
        var questionReason = string.IsNullOrWhiteSpace(frame.ViewerQuestionIds.FirstOrDefault())
            ? "GovernedFamilySectionSceneRoleFallback" : "SourceIdPreservedEditorialTextFallback";
        var storyChecksum = Phase7Determinism.Hash(frame);
        var sourceChecksum = Phase7Determinism.Hash(source);
        var primaryIds = resolved.Where(x => x.Requirement.IsPrimary && x.Resolution.Status == Phase7KnowledgeReferenceStatus.Resolved)
            .Select(x => x.Requirement.ReferenceId).Order(StringComparer.Ordinal).ToArray();
        var identity = new { contract=Phase7ScenePacketContract.Version, input.ExecutionId, variant, frame.FrameId, storyChecksum,
            source.SceneId, sourceChecksum, section, primaryIds, required=required.Select(x=>x.ClaimId).Order(StringComparer.Ordinal),
            optional=optional.Select(x=>x.ClaimId).Order(StringComparer.Ordinal) };
        var packetId = $"packet-{variant.ToLowerInvariant()}-{Phase7Determinism.Hash(identity)[..20]}";
        var target = Math.Max(1, (int)Math.Round(frame.EstimatedDuration));
        var draft = new SceneKnowledgePacket(packetId,input.ExecutionId,input.PlanId,input.EventId,input.EventFamily,input.Language,
            input.ProfileId,input.ProfileVersion,variant,frame.FrameId,storyChecksum,source.SceneId,sourceChecksum,frame.SceneNumber,
            frame.FrameNumber,source.NarrativeStage,source.SceneRole,section,frame.ViewerQuestionIds.FirstOrDefault()??"",question,
            frame.LearningObjectiveIds.FirstOrDefault()??"",$"Explain the certified evidence for the {source.SceneRole} scene.",
            required,optional,deferred,required.Concat(optional).Where(x=>x.IsCultural&&x.RequiresQualification).Select(x=>x.Text).ToArray(),
            input.FamilyProfile.SafetyRules,["Use only packet claims as factual authority.","Qualify location and date/time dependent claims."],
            ["Unsupported factual claims","Unqualified universal viewing claims"],resolutionReport.LocalizedVocabulary,
            resolutionReport.ProtectedTerms,resolutionReport.PronunciationHints,
            required.Concat(optional).SelectMany(x=>x.KnowledgeReferenceIds).Where(id=>input.Knowledge.KnowledgeAuthority.KnowledgeEntities.Any(e=>e.KnowledgeId==id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            requirements.Select(x=>x.ReferenceId).ToArray(),required.Concat(optional).SelectMany(x=>x.SourceIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            target,Math.Max(1,(int)Math.Floor(target*.8)),Math.Max(target,(int)Math.Ceiling(target*1.2)),
            required.Concat(optional).Any(x=>x.IsLocationDependent),required.Concat(optional).Any(x=>x.IsDateTimeDependent),required.Concat(optional).Where(x=>x.IsApproximate).Select(x=>$"Claim {x.ClaimId} is approximate.").ToArray(),
            review.Length>0,warnings,blocking,new SortedDictionary<string,string>(StringComparer.Ordinal){{"sourceNarrativeIntent",frame.NarrativeIntent},{"sourceVisualIntent",frame.VisualIntent},{"sourceSceneChecksum",sourceChecksum}},"")
        { SourceViewerQuestionId=frame.ViewerQuestionIds.FirstOrDefault()??"",ResolvedViewerQuestionText=question,
          ViewerQuestionResolutionReason=questionReason,VisualPlanningLineage=frame.ImageRequirements.Concat(frame.BrollRequirements).Distinct(StringComparer.Ordinal).ToArray(),
          SectionAuthority=sectionAuthority,
          ReferenceResolutions=resolved.Select(x=>new Phase7PacketReferenceResolution(x.Requirement.ReferenceId,x.Requirement.IsPrimary,x.Requirement.IsRequired,x.Resolution.Status,x.Resolution.ReasonCode,x.Resolution.Claims.Select(c=>c.ClaimId).Order(StringComparer.Ordinal).ToArray())).OrderBy(x=>x.ReferenceId,StringComparer.Ordinal).ToArray() };
        draft=draft with { ViewerQuestionResolutionChecksum=Phase7Determinism.Hash(new { draft.SourceViewerQuestionId,question,questionReason,section,variant,claimIds=required.Select(x=>x.ClaimId).Order(StringComparer.Ordinal) }) };
        return draft with { DeterministicChecksum=Phase7Determinism.Hash(draft with { DeterministicChecksum="" }) };
    }

    private static bool HasRequiredEvidence(CertifiedNarrationClaim claim, Phase7ScenePacketInputAuthority input) =>
        input.Knowledge.KnowledgeAuthority.ClaimSupportEvidence.Any(e => e.ClaimId == claim.ClaimId &&
            e.SemanticIdentity == claim.SemanticIdentity && claim.SourceIds.Contains(e.SourceId, StringComparer.Ordinal) &&
            e.SourceEligibility == Phase7SourceEligibility.EligibleForRequiredClaim && !e.RequiresHumanReview &&
            e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField);
    internal static bool HasOptionalEvidence(CertifiedNarrationClaim claim, Phase7ScenePacketInputAuthority input) =>
        input.Knowledge.KnowledgeAuthority.ClaimSupportEvidence.Any(e => e.ClaimId == claim.ClaimId &&
            e.SemanticIdentity == claim.SemanticIdentity && claim.SourceIds.Contains(e.SourceId, StringComparer.Ordinal) &&
            e.SourceEligibility is Phase7SourceEligibility.EligibleForRequiredClaim or Phase7SourceEligibility.EligibleForOptionalClaim &&
            !e.RequiresHumanReview && e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField);
    internal static HashSet<string> DomainTerms(string section,string role,string narrativeStage,IReadOnlyList<string> objectives)
    {
        var map = new Dictionary<string,string[]>(StringComparer.Ordinal) {
            ["hook"]=["identity","recognition","interestingfacts"], ["opening"]=["identity","recognition","interestingfacts"], ["recognition"]=["identity","recognition"],
            ["identity"]=["identity"], ["appearance"]=["appearance","physicalcharacteristics"], ["geometry"]=["geometry","physicalcharacteristics"],
            ["science"]=["scientificstructure","physicalcharacteristics","formation","evolution"], ["structure"]=["scientificstructure","physicalcharacteristics"], ["formation"]=["formation"], ["evolution"]=["evolution"],
            ["objects"]=["objects","stars","deepsky"], ["stars"]=["stars"], ["deepsky"]=["deepsky"],
            ["observation"]=["observation","visibility","timing","locationdependence"], ["viewing"]=["observation","visibility"], ["visibility"]=["visibility"], ["timing"]=["timing"],
            ["equipment"]=["equipment"], ["astrophotography"]=["astrophotography"], ["culture"]=["cultureandmythology","regionaltraditions"], ["mythology"]=["cultureandmythology"], ["tradition"]=["regionaltraditions"],
            ["astrology"]=["astrologyclarification"], ["safety"]=["safety"], ["history"]=["history"], ["closing"]=["summary","interestingfacts"], ["summary"]=["summary","interestingfacts"], ["interestingfacts"]=["interestingfacts"] };
        return new[]{section,role,narrativeStage}.Concat(objectives).SelectMany(Tokenize)
            .Where(map.ContainsKey).SelectMany(x=>map[x]).ToHashSet(StringComparer.Ordinal);
    }
    internal static IEnumerable<string> Tokenize(string value)=>value.Split([' ','_','-','/','.'],StringSplitOptions.RemoveEmptyEntries).Select(Normalize);
    private static string Normalize(string value)=>string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static bool IsPlaceholder(string? value)=>string.IsNullOrWhiteSpace(value)||NeutralPlaceholder.IsMatch(value.Trim());
}
