using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7FoundationValidator : IPhase7FoundationValidator
{
    private static readonly string[] CompleteSet = ["07-narration/narration-input-authority.json","07-narration/family-narration-profile.json","07-narration/knowledge-resolution-report.json","07-narration/phase7-foundation-diagnostics.json","07-narration/long/scene-knowledge-packets.json","07-narration/long/narration-planning.json","07-narration/short/scene-knowledge-packets.json","07-narration/short/narration-planning.json","validation/phase-07-foundation-validation.json"];
    private static readonly Regex Unsafe = new(@"Global's sky|\b\d{1,2}:\d{2}\s*(AM|PM)?\s*India Standard Time\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public Phase7FoundationValidation Validate(Phase7CommittedInputAuthority input, IReadOnlyList<SceneKnowledgePacket> longPackets,
        IReadOnlyList<SceneKnowledgePacket> shortPackets, VariantNarrationPlan longPlan, VariantNarrationPlan shortPlan, IReadOnlyList<string> paths)
    {
        var gates = new List<Phase7FoundationValidationGate>();
        Add("CommittedInputGate", input.StoryFrameAuthority is not null, "Committed Phase 6 authority is absent.");
        Add("FamilyProfileGate", !string.IsNullOrWhiteSpace(input.FamilyProfile.ProfileId), "Family profile is absent.");
        Add("KnowledgeAggregateGate", !string.IsNullOrWhiteSpace(input.Knowledge.PayloadChecksum) && input.Knowledge.BlockingIssues.Count == 0, "Certified knowledge aggregate is invalid.");
        Add("EvergreenPayloadGate", !string.IsNullOrWhiteSpace(input.EvergreenPayloadId) && !string.IsNullOrWhiteSpace(input.EvergreenPayloadChecksum), "Certified evergreen payload is absent.");
        Add("SourceRegistryGate", !string.IsNullOrWhiteSpace(input.Knowledge.SourceRegistryChecksum) && input.Knowledge.SourceIds.Count > 0, "Source registry is invalid.");
        Add("VariantGate", longPackets.All(x=>x.Variant=="Long") && shortPackets.All(x=>x.Variant=="Short"), "Packet variants are invalid.");
        Add("ScenePacketCoverageGate", Coverage(input.LongStoryFrames,longPackets) && Coverage(input.ShortStoryFrames,shortPackets), "Story Frame packet coverage is not exactly one-to-one.");
        Add("ScenePacketOrderGate", Ordered(longPackets) && Ordered(shortPackets), "Packet order is not canonical.");
        var claims = longPackets.Concat(shortPackets).SelectMany(x=>x.RequiredClaims).ToArray();
        Add("SourceProvenanceGate", claims.All(x=>x.ProvenancePrecision=="Exact"), "A required claim lacks exact source provenance.");
        Add("CanonicalDomainGate", input.Knowledge.Domains.All(x=>NarrationKnowledgeDomains.TryParse(x.Domain,out _)), "A non-canonical knowledge domain exists.");
        Add("MandatoryDomainGate", input.FamilyProfile.MandatoryKnowledgeDomains.All(m=>input.Knowledge.Domains.Any(d=>d.Domain==m&&d.Status==KnowledgeDomainStatus.Available)), "A mandatory family domain is missing.");
        Add("ClaimGroundingGate", claims.All(x=>!string.IsNullOrWhiteSpace(x.ClaimId)) && longPackets.Concat(shortPackets).All(p=>p.RequiredClaims.Select(c=>c.ClaimId).Distinct(StringComparer.Ordinal).Count()==p.RequiredClaims.Count), "Claim IDs are blank or duplicated.");
        Add("ClaimIdentityGate", claims.Select(x=>x.SemanticIdentity).Distinct(StringComparer.Ordinal).Count()==claims.DistinctBy(x=>x.ClaimId).Count(), "Duplicate semantic claim identity exists.");
        Add("ConfidenceGate", claims.All(x=>x.Confidence is >= 0m and <= 1m && (x.Confidence>=.8m||x.RequiresHumanReview)), "Claim confidence is invalid or unqualified.");
        Add("ClaimSourceGate", claims.All(x=>x.SourceIds.Count>0 && x.SourceIds.All(s=>!string.IsNullOrWhiteSpace(s))), "A required factual claim has no source.");
        Add("PlaceholderResolutionGate", longPackets.Concat(shortPackets).All(x=>!x.BlockingIssues.Any(i=>i.Contains("placeholder",StringComparison.OrdinalIgnoreCase))), "A blocking placeholder remains unresolved.");
        Add("KnowledgeReferenceResolutionGate", longPackets.Concat(shortPackets).All(x=>!x.BlockingIssues.Any(i=>i.StartsWith("P7REF_",StringComparison.Ordinal))), "A primary knowledge reference is unresolved.");
        Add("SceneSemanticEnrichmentGate", longPackets.Concat(shortPackets).All(x=>!x.SceneObjective.StartsWith("Establish the certified",StringComparison.OrdinalIgnoreCase)), "A generic scene objective remains.");
        Add("ViewerQuestionResolutionGate", longPackets.Concat(shortPackets).All(x=>!string.IsNullOrWhiteSpace(x.ResolvedViewerQuestionText)&&!string.IsNullOrWhiteSpace(x.ViewerQuestionResolutionChecksum)), "A resolved viewer question is missing.");
        Add("VisualEvidenceGate", longPackets.Concat(shortPackets).SelectMany(x=>x.VisualEvidenceIds).All(x=>Regex.IsMatch(x,@"^[a-z0-9]+\.[a-z0-9.-]+$",RegexOptions.IgnoreCase)), "Visual evidence contains free-form planning text.");
        Add("LocationTimeSafetyGate", longPackets.Concat(shortPackets).SelectMany(x=>x.RequiredClaims.Concat(x.OptionalClaims)).All(x=>!Unsafe.IsMatch(x.Text)), "Unsafe universal location/time claim exists.");
        Add("CulturalSafetyGate", claims.Where(x=>x.IsCultural).All(x=>x.RequiresQualification&&x.RequiresHumanReview), "A cultural claim is unqualified.");
        Add("AstrologySeparationGate", claims.Where(x=>x.IsAstrologyRelated).All(x=>x.RequiresQualification), "An astrology relationship is presented without qualification.");
        Add("LocalizationGate", input.Language is "en" or "hi" && input.FamilyProfile.SupportedLanguages.Contains(input.Language), "Localization is invalid.");
        Add("PlanningCoverageGate", PlanCovers(longPlan,longPackets,"Long") && PlanCovers(shortPlan,shortPackets,"Short"), "Narration planning coverage is invalid.");
        Add("LongShortIndependenceGate", !ReferenceEquals(longPlan.Scenes,shortPlan.Scenes) && !longPackets.Select(x=>x.PacketId).Intersect(shortPackets.Select(x=>x.PacketId)).Any(), "Long and Short plans are dependent.");
        Add("ChecksumGate", ValidChecksums(longPackets,shortPackets,longPlan,shortPlan), "A deterministic checksum is invalid.");
        Add("ArtifactCompleteSetGate", paths.Order(StringComparer.Ordinal).SequenceEqual(CompleteSet.Order(StringComparer.Ordinal)), "The P7.1 artifact complete set is incomplete or contains extras.");
        Add("PhysicalReadbackGate", paths.Count==CompleteSet.Length, "Physical complete-set readback was not evidenced.");
        Add("ArtifactPathGate", paths.All(Safe), "An artifact path is unsafe.");
        var errors = gates.SelectMany(x=>x.Errors).ToArray();
        var code = errors.Length == 0 ? "P7FOUNDATION_VALID" : Reason(gates.First(x=>!x.Passed).Name);
        var draft = new Phase7FoundationValidation(errors.Length==0,code,gates,errors,"");
        return draft with { DeterministicChecksum=Phase7Determinism.Hash(draft with { DeterministicChecksum="" }) };

        void Add(string name,bool passed,string error)=>gates.Add(new(name,passed,passed?[]:[error]));
    }
    private static bool Coverage(IReadOnlyList<StoryFrameAuthorityFrame> frames,IReadOnlyList<SceneKnowledgePacket> packets)
        => frames.Count==packets.Count && frames.Select(x=>x.FrameId).Order().SequenceEqual(packets.Select(x=>x.StoryFrameId).Order());
    private static bool Ordered(IReadOnlyList<SceneKnowledgePacket> packets)=>packets.SequenceEqual(packets.OrderBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber));
    private static bool PlanCovers(VariantNarrationPlan plan,IReadOnlyList<SceneKnowledgePacket> packets,string variant)
        => plan.Variant==variant && plan.ScenePlanCount==packets.Count && plan.Scenes.Select(x=>x.StoryFrameId).SequenceEqual(packets.Select(x=>x.StoryFrameId));
    private static bool ValidChecksums(IEnumerable<SceneKnowledgePacket> l,IEnumerable<SceneKnowledgePacket> s,VariantNarrationPlan lp,VariantNarrationPlan sp)
        => l.Concat(s).All(x=>x.DeterministicChecksum==Phase7Determinism.Hash(x with { DeterministicChecksum="" }))
           && lp.DeterministicChecksum==Phase7Determinism.Hash(lp with { DeterministicChecksum="" }) && sp.DeterministicChecksum==Phase7Determinism.Hash(sp with { DeterministicChecksum="" });
    private static bool Safe(string path)=>!string.IsNullOrWhiteSpace(path)&&!Path.IsPathRooted(path)&&!path.Contains('\\')&&!path.Split('/').Any(x=>x is "" or "." or "..")&&!path.Contains("staging",StringComparison.OrdinalIgnoreCase)&&!path.Contains("backup",StringComparison.OrdinalIgnoreCase);
    private static string Reason(string gate)=>gate switch { "FamilyProfileGate"=>"P7FOUNDATION_PROFILE_INVALID","KnowledgePayloadGate"=>"P7FOUNDATION_KNOWLEDGE_INVALID","SourceRegistryGate"=>"P7FOUNDATION_SOURCE_INVALID","ScenePacketCoverageGate" or "ScenePacketOrderGate" or "VariantGate"=>"P7FOUNDATION_PACKET_COVERAGE_INVALID","ClaimGroundingGate" or "ClaimSourceGate"=>"P7FOUNDATION_CLAIM_GROUNDING_INVALID","PlaceholderResolutionGate"=>"P7FOUNDATION_PLACEHOLDER_UNRESOLVED","LocationTimeSafetyGate"=>"P7FOUNDATION_LOCATION_TIME_UNSAFE","LocalizationGate"=>"P7FOUNDATION_LOCALIZATION_INVALID","PlanningCoverageGate" or "LongShortIndependenceGate"=>"P7FOUNDATION_PLANNING_INVALID","ChecksumGate"=>"P7FOUNDATION_CHECKSUM_INVALID",_=>"P7FOUNDATION_PHASE6_INVALID" };
}
