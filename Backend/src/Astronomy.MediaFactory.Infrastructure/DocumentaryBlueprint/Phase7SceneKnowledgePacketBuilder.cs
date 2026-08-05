using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7SceneKnowledgePacketBuilder : IPhase7SceneKnowledgePacketBuilder
{
    private static readonly Regex NeutralPlaceholder = new(@"^(?:tbd|todo|placeholder|unknown|not[- ]?provided)(?:\W|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IPhase7KnowledgeReferenceResolver resolver;
    private readonly IPhase7SceneSectionAuthorityResolver sectionAuthorityResolver;
    public Phase7SceneKnowledgePacketBuilder(IPhase7KnowledgeReferenceResolver resolver,
        IPhase7SceneSectionAuthorityResolver sectionAuthorityResolver)
    {
        this.resolver = resolver;
        this.sectionAuthorityResolver = sectionAuthorityResolver;
    }

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
        var sectionAuthority = sectionAuthorityResolver.Resolve(frame, source);
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
        foreach (var item in resolved.Where(x => x.Requirement.IsRequired &&
            !x.Resolution.Claims.Any(c => required.Any(r => r.ClaimId == c.ClaimId))))
            blocking.Add($"P7PACKET_REQUIRED_REFERENCE_NO_ELIGIBLE_REQUIRED_CLAIM:{item.Requirement.ReferenceId}");
        var primaries = resolved.Where(x => x.Requirement.IsPrimary).ToArray();
        if (primaries.Length != 1 || primaries[0].Requirement.Variant != variant ||
            primaries[0].Resolution.Status != Phase7KnowledgeReferenceStatus.Resolved ||
            primaries[0].Resolution.Claims.Count == 0)
            blocking.Add($"P7PACKET_PRIMARY_REFERENCE_UNRESOLVED:{frame.FrameId}");
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
        var target = Math.Max(1, (int)Math.Round(frame.EstimatedDuration));
        var draft = new SceneKnowledgePacket("",input.ExecutionId,input.PlanId,input.EventId,input.EventFamily,input.Language,
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
          ReferenceResolutions=resolved.Select(x=>new Phase7PacketReferenceResolution(x.Requirement.ReferenceId,x.Requirement.IsPrimary,x.Requirement.IsRequired,x.Resolution.Status,x.Resolution.ReasonCode,x.Resolution.Claims.Select(c=>c.ClaimId).Order(StringComparer.Ordinal).ToArray())).ToArray() };
        draft=draft with { ViewerQuestionResolutionChecksum=Phase7Determinism.Hash(new { draft.SourceViewerQuestionId,question,questionReason,section,variant,claimIds=required.Select(x=>x.ClaimId).Order(StringComparer.Ordinal) }) };
        draft = Phase7SceneKnowledgePacketCanonicalizer.Canonicalize(draft);
        draft = draft with { PacketId=Phase7SceneKnowledgePacketCanonicalizer.ComputePacketId(draft) };
        return draft with { DeterministicChecksum=Phase7SceneKnowledgePacketCanonicalizer.ComputeChecksum(draft) };
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
        string[] D(params NarrationKnowledgeDomainKey[] keys) => keys.Select(Domain).ToArray();
        var map = new Dictionary<string,string[]>(StringComparer.Ordinal) {
            ["hook"]=D(NarrationKnowledgeDomainKey.Identity,NarrationKnowledgeDomainKey.Recognition,NarrationKnowledgeDomainKey.InterestingFacts), ["opening"]=D(NarrationKnowledgeDomainKey.Identity,NarrationKnowledgeDomainKey.Recognition,NarrationKnowledgeDomainKey.InterestingFacts), ["recognition"]=D(NarrationKnowledgeDomainKey.Identity,NarrationKnowledgeDomainKey.Recognition,NarrationKnowledgeDomainKey.RecognitionGeometry),
            ["identity"]=D(NarrationKnowledgeDomainKey.Identity), ["appearance"]=D(NarrationKnowledgeDomainKey.Appearance,NarrationKnowledgeDomainKey.PhysicalCharacteristics), ["geometry"]=D(NarrationKnowledgeDomainKey.Geometry,NarrationKnowledgeDomainKey.RecognitionGeometry,NarrationKnowledgeDomainKey.PhysicalCharacteristics),
            ["science"]=D(NarrationKnowledgeDomainKey.ScientificStructure,NarrationKnowledgeDomainKey.PhysicalCharacteristics,NarrationKnowledgeDomainKey.Formation,NarrationKnowledgeDomainKey.Evolution,NarrationKnowledgeDomainKey.ScientificSignificance), ["structure"]=D(NarrationKnowledgeDomainKey.ScientificStructure,NarrationKnowledgeDomainKey.PhysicalCharacteristics), ["formation"]=D(NarrationKnowledgeDomainKey.Formation), ["evolution"]=D(NarrationKnowledgeDomainKey.Evolution),
            ["objects"]=D(NarrationKnowledgeDomainKey.KeyObjects,NarrationKnowledgeDomainKey.ScientificStructure,NarrationKnowledgeDomainKey.Multiplicity,NarrationKnowledgeDomainKey.Variability), ["stars"]=D(NarrationKnowledgeDomainKey.KeyObjects,NarrationKnowledgeDomainKey.ScientificStructure,NarrationKnowledgeDomainKey.Multiplicity,NarrationKnowledgeDomainKey.Variability), ["deepsky"]=D(NarrationKnowledgeDomainKey.DeepSkyObjects),
            ["observation"]=D(NarrationKnowledgeDomainKey.Observation,NarrationKnowledgeDomainKey.Visibility,NarrationKnowledgeDomainKey.Timing,NarrationKnowledgeDomainKey.LocationDependence), ["viewing"]=D(NarrationKnowledgeDomainKey.Observation,NarrationKnowledgeDomainKey.Visibility,NarrationKnowledgeDomainKey.Timing,NarrationKnowledgeDomainKey.LocationDependence), ["visibility"]=D(NarrationKnowledgeDomainKey.Visibility), ["timing"]=D(NarrationKnowledgeDomainKey.Timing),
            ["equipment"]=D(NarrationKnowledgeDomainKey.Equipment), ["astrophotography"]=D(NarrationKnowledgeDomainKey.Astrophotography,NarrationKnowledgeDomainKey.ImagingAppearance), ["culture"]=D(NarrationKnowledgeDomainKey.CultureAndMythology,NarrationKnowledgeDomainKey.RegionalTraditions), ["mythology"]=D(NarrationKnowledgeDomainKey.CultureAndMythology,NarrationKnowledgeDomainKey.RegionalTraditions), ["tradition"]=D(NarrationKnowledgeDomainKey.RegionalTraditions),
            ["astrology"]=D(NarrationKnowledgeDomainKey.AstrologyClarification), ["safety"]=D(NarrationKnowledgeDomainKey.Safety), ["history"]=D(NarrationKnowledgeDomainKey.History), ["closing"]=D(NarrationKnowledgeDomainKey.InterestingFacts,NarrationKnowledgeDomainKey.ScientificSignificance), ["summary"]=D(NarrationKnowledgeDomainKey.InterestingFacts,NarrationKnowledgeDomainKey.ScientificSignificance), ["interestingfacts"]=D(NarrationKnowledgeDomainKey.InterestingFacts) };
        return new[]{section,role,narrativeStage}.Concat(objectives).SelectMany(Tokenize)
            .Where(map.ContainsKey).SelectMany(x=>map[x]).ToHashSet(StringComparer.Ordinal);
    }
    private static string Domain(NarrationKnowledgeDomainKey key) => Normalize(NarrationKnowledgeDomains.Id(key));
    internal static IEnumerable<string> Tokenize(string value)=>value.Split([' ','_','-','/','.'],StringSplitOptions.RemoveEmptyEntries).Select(Normalize);
    private static string Normalize(string value)=>string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static bool IsPlaceholder(string? value)=>string.IsNullOrWhiteSpace(value)||NeutralPlaceholder.IsMatch(value.Trim());
}
