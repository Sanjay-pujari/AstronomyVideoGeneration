using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7SceneKnowledgePacketValidator : IPhase7SceneKnowledgePacketValidator
{
    private static readonly string[] GateNames = ["InputAuthorityGate", "VariantCoverageGate", "StoryFrameCoverageGate",
        "SceneOrderGate", "SceneIdentityGate", "StoryFrameChecksumGate", "SourceSceneLineageGate", "ProfileGate",
        "LanguageGate", "PrimaryReferenceGate", "RequiredReferenceResolutionGate", "ClaimPartitionGate",
        "RequiredClaimEvidenceGate", "RequiredClaimChecksumGate", "NoContradictionGate", "HumanReviewIsolationGate",
        "SafetyRuleGate", "CulturalQualificationGate", "AstrologySeparationGate", "LocationTimeSafetyGate",
        "DurationGate", "VisualEvidenceGate", "LongShortIndependenceGate", "DeterminismGate"];

    public Phase7SceneKnowledgePacketValidation Validate(Phase7ScenePacketInputAuthority input,
        IReadOnlyList<SceneKnowledgePacket> longPackets, IReadOnlyList<SceneKnowledgePacket> shortPackets)
    {
        var failures = new Dictionary<string,List<string>>(StringComparer.Ordinal);
        void Check(string gate, bool valid, string error) { if (!valid) (failures.TryGetValue(gate, out var e) ? e : failures[gate] = []).Add(error); }
        var all = longPackets.Concat(shortPackets).ToArray();
        var expected = input.LongStoryFrames.Concat(input.ShortStoryFrames).ToArray();
        var authorityClaims = input.Knowledge.KnowledgeAuthority.Claims.ToDictionary(x => x.ClaimId, StringComparer.Ordinal);
        Check("InputAuthorityGate", input.Knowledge.CommittedStateValidationPassed, "P7.1A committed state is invalid.");
        Check("VariantCoverageGate", longPackets.All(x => x.Variant == "Long") && shortPackets.All(x => x.Variant == "Short"), "Packet variant is wrong.");
        Check("StoryFrameCoverageGate", all.Length == expected.Length && all.Select(x=>x.StoryFrameId).Order().SequenceEqual(expected.Select(x=>x.FrameId).Order()), "Packet coverage is not exactly one per frame.");
        Check("SceneOrderGate", Ordered(longPackets) && Ordered(shortPackets), "Authored scene order drifted.");
        Check("SceneIdentityGate", all.All(p => expected.Count(f => f.FrameId == p.StoryFrameId && f.SceneId == p.SourceSceneId && f.SceneNumber == p.SceneNumber && f.FrameNumber == p.FrameNumber) == 1), "Scene/frame identity mismatch.");
        Check("StoryFrameChecksumGate", all.All(p => expected.Any(f => f.FrameId == p.StoryFrameId && p.StoryFrameChecksum == Phase7Determinism.Hash(f))), "Story Frame checksum mismatch.");
        Check("SourceSceneLineageGate", all.All(p => (p.Variant == "Long" ? input.LongSourceScenes : input.ShortSourceScenes).Count(s => s.SceneId == p.SourceSceneId && s.SceneNumber == p.SceneNumber) == 1), "Source scene row is absent or ambiguous.");
        Check("ProfileGate", all.All(p => p.ProfileId == input.ProfileId && p.ProfileVersion == input.ProfileVersion), "Profile identity mismatch.");
        Check("LanguageGate", all.All(p => string.Equals(p.Language, input.Language, StringComparison.OrdinalIgnoreCase)), "Language mismatch.");
        Check("PrimaryReferenceGate", all.All(p => p.KnowledgeReferenceIds.Count > 0), "A primary reference is missing.");
        Check("RequiredReferenceResolutionGate", all.All(p => !p.BlockingIssues.Any(x => x.StartsWith("P7REF_", StringComparison.Ordinal))), "A required reference did not resolve.");
        Check("ClaimPartitionGate", all.All(Partitions), "A claim occurs in multiple partitions or has the wrong disposition.");
        Check("RequiredClaimEvidenceGate", all.SelectMany(p=>p.RequiredClaims).All(c => c.SourceIds.Count > 0 && authorityClaims.ContainsKey(c.ClaimId)), "A required claim lacks exact eligible evidence.");
        Check("RequiredClaimChecksumGate", all.SelectMany(p=>p.RequiredClaims).All(c => c.Checksum == Phase7Determinism.Hash(c with { Checksum="" })), "A required claim checksum is invalid.");
        Check("NoContradictionGate", input.Knowledge.KnowledgeAuthority.MergeDecisions.All(x => x.Classification != Phase7KnowledgeMergeClassification.Contradictory || !x.SelectedClaimIds.Any(id => all.SelectMany(p=>p.RequiredClaims).Any(c=>c.ClaimId==id))), "An unresolved contradiction became required.");
        Check("HumanReviewIsolationGate", all.SelectMany(p=>p.RequiredClaims.Concat(p.OptionalClaims)).All(c => !c.RequiresHumanReview && c.Disposition != Phase7ClaimDisposition.HumanReview), "Human-review material is authoritative.");
        Check("SafetyRuleGate", all.All(p => input.FamilyProfile.SafetyRules.All(p.SafetyRules.Contains)), "An applicable safety rule is absent.");
        Check("CulturalQualificationGate", all.SelectMany(p=>p.RequiredClaims.Concat(p.OptionalClaims)).Where(c=>c.IsCultural).All(c=>c.RequiresQualification), "Cultural context is unqualified.");
        Check("AstrologySeparationGate", all.SelectMany(p=>p.RequiredClaims.Concat(p.OptionalClaims)).Where(c=>c.IsAstrologyRelated).All(c=>c.RequiresQualification), "Astrology is not separated from astronomy.");
        Check("LocationTimeSafetyGate", all.SelectMany(p=>p.RequiredClaims.Concat(p.OptionalClaims)).Where(c=>c.IsLocationDependent||c.IsDateTimeDependent).All(c=>c.RequiresQualification), "Location/time claim is unqualified.");
        Check("DurationGate", all.All(p=>p.MinimumDurationSeconds>0&&p.MinimumDurationSeconds<=p.TargetDurationSeconds&&p.TargetDurationSeconds<=p.MaximumDurationSeconds), "Duration bounds are invalid.");
        var entityIds=input.Knowledge.KnowledgeAuthority.KnowledgeEntities.Select(x=>x.KnowledgeId).ToHashSet(StringComparer.Ordinal);
        Check("VisualEvidenceGate", all.SelectMany(p=>p.VisualEvidenceIds).All(entityIds.Contains), "Visual evidence is not a certified object identity.");
        Check("LongShortIndependenceGate", !longPackets.Select(x=>x.PacketId).Intersect(shortPackets.Select(x=>x.PacketId),StringComparer.Ordinal).Any() && !ReferenceEquals(longPackets,shortPackets), "Long and Short packets are dependent.");
        Check("DeterminismGate", all.All(p=>p.DeterministicChecksum==Phase7Determinism.Hash(p with{DeterministicChecksum=""})), "Packet checksum mismatch.");
        var gates=GateNames.Select(n=>new Phase7SceneKnowledgePacketValidationGate(n,!failures.ContainsKey(n),failures.GetValueOrDefault(n)??[])).ToArray();
        var errors=gates.SelectMany(x=>x.Errors).ToArray();
        var draft=new Phase7SceneKnowledgePacketValidation(errors.Length==0,errors.Length==0?"P7PACKET_VALID":"P7PACKET_INVALID",gates,errors,"");
        return draft with{DeterministicChecksum=Phase7Determinism.Hash(draft with{DeterministicChecksum=""})};
    }
    private static bool Ordered(IEnumerable<SceneKnowledgePacket> p)=>p.SequenceEqual(p.OrderBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber));
    private static bool Partitions(SceneKnowledgePacket p)
    { var r=p.RequiredClaims.Select(x=>x.ClaimId).ToArray();var o=p.OptionalClaims.Select(x=>x.ClaimId).ToArray();var d=p.DeferredClaims.Select(x=>x.ClaimId).ToArray();return !r.Intersect(o).Concat(r.Intersect(d)).Concat(o.Intersect(d)).Any()&&p.RequiredClaims.All(x=>x.Disposition==Phase7ClaimDisposition.Required)&&p.OptionalClaims.All(x=>x.Disposition==Phase7ClaimDisposition.Optional)&&p.DeferredClaims.All(x=>x.Disposition==Phase7ClaimDisposition.Deferred); }
}
