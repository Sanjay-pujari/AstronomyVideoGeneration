using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7SceneKnowledgePacketValidator : IPhase7SceneKnowledgePacketValidator
{
    private static readonly string[] GateNames = ["InputAuthorityGate", "VariantCoverageGate", "StoryFrameCoverageGate",
        "SceneOrderGate", "SceneIdentityGate", "StoryFrameChecksumGate", "SourceSceneLineageGate", "ProfileGate",
        "LanguageGate", "SectionAuthorityGate", "PrimaryReferenceGate", "RequiredReferenceResolutionGate", "PacketBlockingIssueGate", "ClaimPartitionGate",
        "RequiredClaimEvidenceGate", "OptionalClaimEvidenceGate", "RequiredClaimChecksumGate", "PacketClaimAuthorityIdentityGate", "NoContradictionGate", "HumanReviewIsolationGate",
        "SafetyRuleGate", "CulturalQualificationGate", "AstrologySeparationGate", "LocationTimeSafetyGate",
        "DurationGate", "VisualEvidenceGate", "ViewerQuestionResolutionGate",
        "ResolutionReportLineageGate", "LongShortIndependenceGate", "DeterminismGate"];

    public Phase7SceneKnowledgePacketValidation Validate(Phase7ScenePacketInputAuthority input,
        IReadOnlyList<SceneKnowledgePacket> longPackets, IReadOnlyList<SceneKnowledgePacket> shortPackets)
    {
        var failures = new Dictionary<string,List<string>>(StringComparer.Ordinal);
        var packetFailures = new List<Phase7ScenePacketFailureSummary>();
        void Check(string gate, bool valid, string error) { if (!valid) (failures.TryGetValue(gate, out var e) ? e : failures[gate] = []).Add(error); }
        void PacketFail(SceneKnowledgePacket p, string gate, string code, string? referenceId = null, string? claimId = null, string? blocking = null, IEnumerable<string>? extraClaims = null)
        {
            var parts = new SortedDictionary<string,string>(StringComparer.Ordinal)
            {
                ["reasonCode"] = code, ["gate"] = gate, ["variant"] = p.Variant, ["packetId"] = p.PacketId, ["storyFrameId"] = p.StoryFrameId,
                ["sourceSceneId"] = p.SourceSceneId, ["sceneNumber"] = p.SceneNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["frameNumber"] = p.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), ["sectionKey"] = p.SectionKey
            };
            if (!string.IsNullOrWhiteSpace(referenceId)) parts["referenceId"] = referenceId!;
            if (!string.IsNullOrWhiteSpace(claimId)) parts["claimId"] = claimId!;
            if (extraClaims is not null) parts["candidateClaimIds"] = string.Join(",", extraClaims.Order(StringComparer.Ordinal));
            var message = code + ":" + string.Join(";", parts.Select(x => x.Key + "=" + x.Value));
            (failures.TryGetValue(gate, out var e) ? e : failures[gate] = []).Add(message);
            packetFailures.Add(new(p.Variant, p.PacketId, p.StoryFrameId, p.SourceSceneId, p.SceneNumber, p.FrameNumber, p.SectionKey,
                [gate], [code], string.IsNullOrWhiteSpace(referenceId) ? [] : [referenceId!],
                string.IsNullOrWhiteSpace(claimId) ? (extraClaims?.Order(StringComparer.Ordinal).ToArray() ?? []) : [claimId!],
                string.IsNullOrWhiteSpace(blocking) ? [] : [blocking!]));
        }
        var all = longPackets.Concat(shortPackets).ToArray();
        var expected = input.LongStoryFrames.Concat(input.ShortStoryFrames).ToArray();
        var authorityClaims = input.Knowledge.KnowledgeAuthority.Claims.ToDictionary(x => x.ClaimId, StringComparer.Ordinal);
        Check("InputAuthorityGate", input.Knowledge.CommittedStateValidationPassed, "P7.1A committed state is invalid.");
        Check("VariantCoverageGate", longPackets.All(x => x.Variant == "Long") && shortPackets.All(x => x.Variant == "Short"), "Packet variant is wrong.");
        Check("StoryFrameCoverageGate", all.Length == expected.Length && all.Select(x=>x.StoryFrameId).Order(StringComparer.Ordinal).SequenceEqual(expected.Select(x=>x.FrameId).Order(StringComparer.Ordinal)), "Packet coverage is not exactly one per frame.");
        Check("SceneOrderGate", Ordered(longPackets) && Ordered(shortPackets), "Authored scene order drifted.");
        Check("SceneIdentityGate", all.All(p => expected.Count(f => f.FrameId == p.StoryFrameId && f.SceneId == p.SourceSceneId && f.SceneNumber == p.SceneNumber && f.FrameNumber == p.FrameNumber) == 1), "Scene/frame identity mismatch.");
        Check("StoryFrameChecksumGate", all.All(p => expected.Any(f => f.FrameId == p.StoryFrameId && p.StoryFrameChecksum == Phase7Determinism.Hash(f))), "Story Frame checksum mismatch.");
        Check("SourceSceneLineageGate", all.All(p => (p.Variant == "Long" ? input.LongSourceScenes : input.ShortSourceScenes).Count(s => s.Variant == p.Variant && s.SceneId == p.SourceSceneId && s.SceneNumber == p.SceneNumber && p.SourceSceneChecksum == Phase7Determinism.Hash(s)) == 1), "Source scene row/checksum is absent, ambiguous, or not authoritative.");
        Check("ProfileGate", all.All(p => p.ProfileId == input.ProfileId && p.ProfileVersion == input.ProfileVersion), "Profile identity mismatch.");
        Check("LanguageGate", all.All(p => string.Equals(p.Language, input.Language, StringComparison.OrdinalIgnoreCase)), "Language mismatch.");
        Check("SectionAuthorityGate", all.All(p=>p.SectionAuthority is { IsValid:true } a && a.SectionKey==p.SectionKey &&
            (p.Variant=="Long"?input.LongSourceScenes:input.ShortSourceScenes).Any(s=>s.SceneId==p.SourceSceneId&&s.NarrativeStage==a.NarrativeStage&&s.SceneRole==a.SceneRole)), "Packet section is not resolver-derived source-scene authority.");
        foreach (var p in all.OrderBy(x=>x.Variant,StringComparer.Ordinal).ThenBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber))
        {
            if (!input.ReferenceRequirements.TryGetValue(p.StoryFrameId,out var req)) { PacketFail(p,"PrimaryReferenceGate","P7PACKET_PRIMARY_REFERENCE_COUNT_INVALID"); continue; }
            var prim=req.Where(r=>r.IsPrimary).OrderBy(r=>r.ReferenceId,StringComparer.Ordinal).ToArray();
            if (prim.Length!=1) { PacketFail(p,"PrimaryReferenceGate", prim.Length>1?"P7PACKET_PRIMARY_REFERENCE_AMBIGUOUS":"P7PACKET_PRIMARY_REFERENCE_COUNT_INVALID"); continue; }
            var r=prim[0]; var res=p.ReferenceResolutions.FirstOrDefault(x=>x.ReferenceId==r.ReferenceId);
            if (r.Variant!=p.Variant) PacketFail(p,"PrimaryReferenceGate","P7PACKET_PRIMARY_REFERENCE_VARIANT_MISMATCH",r.ReferenceId);
            else if (!p.KnowledgeReferenceIds.Contains(r.ReferenceId,StringComparer.Ordinal)) PacketFail(p,"PrimaryReferenceGate","P7PACKET_PRIMARY_REFERENCE_NOT_IN_PACKET",r.ReferenceId);
            else if (res is null || res.Status!=Phase7KnowledgeReferenceStatus.Resolved) PacketFail(p,"PrimaryReferenceGate","P7PACKET_PRIMARY_REFERENCE_UNRESOLVED",r.ReferenceId);
            else if (res.ResolvedClaimIds.Count==0) PacketFail(p,"PrimaryReferenceGate","P7PACKET_PRIMARY_REFERENCE_NO_CLAIMS",r.ReferenceId);
        }
        foreach (var p in all.OrderBy(x=>x.Variant,StringComparer.Ordinal).ThenBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber))
        if (input.ReferenceRequirements.TryGetValue(p.StoryFrameId, out var requirements))
        foreach (var requirement in requirements.Where(r=>r.IsRequired).OrderBy(r=>r.ReferenceId,StringComparer.Ordinal))
        {
            var resolution=p.ReferenceResolutions.FirstOrDefault(r=>r.ReferenceId==requirement.ReferenceId);
            if (resolution is null || resolution.Status != Phase7KnowledgeReferenceStatus.Resolved)
                PacketFail(p,"RequiredReferenceResolutionGate","P7PACKET_REQUIRED_REFERENCE_UNRESOLVED",requirement.ReferenceId);
            else if (!resolution.ResolvedClaimIds.Any(id => p.RequiredClaims.Any(c=>c.ClaimId==id && !c.RequiresHumanReview && c.Disposition==Phase7ClaimDisposition.Required && HasRequiredEvidence(c,input))))
                PacketFail(p,"RequiredReferenceResolutionGate","P7PACKET_REQUIRED_REFERENCE_NO_ELIGIBLE_REQUIRED_CLAIM",requirement.ReferenceId, extraClaims: resolution.ResolvedClaimIds);
        }
        foreach (var p in all.OrderBy(x=>x.Variant,StringComparer.Ordinal).ThenBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber)) foreach (var b in p.BlockingIssues.Order(StringComparer.Ordinal)) PacketFail(p,"PacketBlockingIssueGate", b.Split(':')[0] is { Length: >0 } c ? c : "P7PACKET_BLOCKING_ISSUE", blocking: b);
        Check("ClaimPartitionGate", all.All(Partitions), "A claim occurs in multiple partitions or has the wrong disposition.");
        Check("RequiredClaimEvidenceGate", all.SelectMany(p=>p.RequiredClaims).All(c => authorityClaims.TryGetValue(c.ClaimId,out var a) && a.SemanticIdentity==c.SemanticIdentity && input.Knowledge.KnowledgeAuthority.ClaimSupportEvidence.Any(e=>e.ClaimId==c.ClaimId&&e.SemanticIdentity==c.SemanticIdentity&&c.SourceIds.Contains(e.SourceId,StringComparer.Ordinal)&&e.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim&&!e.RequiresHumanReview&&e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField)), "A required claim lacks exact Required-eligible evidence.");
        Check("OptionalClaimEvidenceGate", all.SelectMany(p=>p.OptionalClaims).All(c => authorityClaims.TryGetValue(c.ClaimId,out var a) && a.SemanticIdentity==c.SemanticIdentity && Phase7SceneKnowledgePacketBuilder.HasOptionalEvidence(c,input)), "An optional claim lacks exact eligible evidence.");
        Check("RequiredClaimChecksumGate", all.SelectMany(p=>p.RequiredClaims).All(c => authorityClaims.TryGetValue(c.ClaimId, out var a) && ClaimIdentical(c,a) && c.Checksum == a.Checksum && c.Checksum == Phase7Determinism.Hash(c with { Checksum="" })), "A required claim checksum or frozen authority identity is invalid.");
        var identityMismatches = all.SelectMany(p => p.RequiredClaims.Concat(p.OptionalClaims).Concat(p.DeferredClaims))
            .Where(c => !authorityClaims.TryGetValue(c.ClaimId, out var a) || !ClaimIdentical(c, a))
            .Select(c => $"P7PACKET_CLAIM_AUTHORITY_IDENTITY_MISMATCH:{c.ClaimId}").Distinct(StringComparer.Ordinal).ToArray();
        foreach (var mismatch in identityMismatches) Check("PacketClaimAuthorityIdentityGate", false, mismatch);
        Check("PacketClaimAuthorityIdentityGate", identityMismatches.Length == 0, "Packet claims are authority-identical.");
        Check("NoContradictionGate", input.Knowledge.KnowledgeAuthority.MergeDecisions.All(x => x.Classification != Phase7KnowledgeMergeClassification.Contradictory || !x.SelectedClaimIds.Any(id => all.SelectMany(p=>p.RequiredClaims).Any(c=>c.ClaimId==id))), "An unresolved contradiction became required.");
        Check("HumanReviewIsolationGate", all.SelectMany(p=>p.RequiredClaims.Concat(p.OptionalClaims)).All(c => !c.RequiresHumanReview && c.Disposition != Phase7ClaimDisposition.HumanReview), "Human-review material is authoritative.");
        Check("SafetyRuleGate", all.All(p => input.FamilyProfile.SafetyRules.All(p.SafetyRules.Contains)), "An applicable safety rule is absent.");
        Check("CulturalQualificationGate", all.SelectMany(p=>p.RequiredClaims.Concat(p.OptionalClaims)).Where(c=>c.IsCultural).All(c=>c.RequiresQualification), "Cultural context is unqualified.");
        Check("AstrologySeparationGate", all.SelectMany(p=>p.RequiredClaims.Concat(p.OptionalClaims)).Where(c=>c.IsAstrologyRelated).All(c=>c.RequiresQualification), "Astrology is not separated from astronomy.");
        foreach (var p in all.OrderBy(x=>x.Variant,StringComparer.Ordinal).ThenBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber)) foreach (var c in p.RequiredClaims.Concat(p.OptionalClaims).Where(c=>c.IsLocationDependent||c.IsDateTimeDependent).OrderBy(c=>c.ClaimId,StringComparer.Ordinal)) if (!LocationTimeSafe(c,input)) PacketFail(p,"LocationTimeSafetyGate","P7PACKET_LOCATION_TIME_QUALIFICATION_MISSING", claimId: c.ClaimId);
        Check("DurationGate", all.All(p=>p.MinimumDurationSeconds>0&&p.MinimumDurationSeconds<=p.TargetDurationSeconds&&p.TargetDurationSeconds<=p.MaximumDurationSeconds), "Duration bounds are invalid.");
        var entityIds=input.Knowledge.KnowledgeAuthority.KnowledgeEntities.Select(x=>x.KnowledgeId).ToHashSet(StringComparer.Ordinal);
        Check("VisualEvidenceGate", all.SelectMany(p=>p.VisualEvidenceIds).All(entityIds.Contains), "Visual evidence is not a certified object identity.");
        Check("ViewerQuestionResolutionGate", all.All(p=>!string.IsNullOrWhiteSpace(p.ResolvedViewerQuestionText)&&p.ViewerQuestionResolutionChecksum==Phase7Determinism.Hash(new{p.SourceViewerQuestionId,question=p.ResolvedViewerQuestionText,questionReason=p.ViewerQuestionResolutionReason,section=p.SectionKey,variant=p.Variant,claimIds=p.RequiredClaims.Select(x=>x.ClaimId).Order(StringComparer.Ordinal)})), "Viewer-question resolution lineage is invalid.");
        Check("ResolutionReportLineageGate", input.Knowledge.ResolvedNarrationKnowledge is { } r&&r.DeterministicChecksum==Phase7Determinism.Hash(r with{DeterministicChecksum=""}), "Committed resolution report is absent or invalid.");
        var longFrameIds=input.LongStoryFrames.Select(x=>x.FrameId).ToHashSet(StringComparer.Ordinal);var shortFrameIds=input.ShortStoryFrames.Select(x=>x.FrameId).ToHashSet(StringComparer.Ordinal);
        bool OwnsRequirements(SceneKnowledgePacket p, string variant) => input.ReferenceRequirements.TryGetValue(p.StoryFrameId,out var r) && r.All(x=>x.Variant==variant);
        Check("LongShortIndependenceGate", !ReferenceEquals(longPackets,shortPackets)&&!longPackets.Select(x=>x.PacketId).Intersect(shortPackets.Select(x=>x.PacketId),StringComparer.Ordinal).Any()&&longPackets.All(x=>longFrameIds.Contains(x.StoryFrameId)&&!shortFrameIds.Contains(x.StoryFrameId)&&OwnsRequirements(x,"Long"))&&shortPackets.All(x=>shortFrameIds.Contains(x.StoryFrameId)&&!longFrameIds.Contains(x.StoryFrameId)&&OwnsRequirements(x,"Short")), "Long and Short packet identity/authority crossed variants.");
        Check("DeterminismGate", all.All(p=>p.DeterministicChecksum==Phase7SceneKnowledgePacketCanonicalizer.ComputeChecksum(p)), "Packet checksum mismatch.");
        var gates=GateNames.Select(n=>new Phase7SceneKnowledgePacketValidationGate(n,!failures.ContainsKey(n),failures.GetValueOrDefault(n)??[])).ToArray();
        var errors=gates.SelectMany(x=>x.Errors).Order(StringComparer.Ordinal).ToArray();
        var draft=new Phase7SceneKnowledgePacketValidation(errors.Length==0,errors.Length==0?"P7PACKET_VALID":"P7PACKET_INVALID",gates,errors,"");
        return draft with{TotalGateCount=gates.Length, FailureSummaries=packetFailures.OrderBy(x=>x.Variant,StringComparer.Ordinal).ThenBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber).ThenBy(x=>string.Join(",",x.ReasonCodes),StringComparer.Ordinal).ToArray(), DeterministicChecksum=Phase7Determinism.Hash(draft with{DeterministicChecksum=""})};
    }
    private static bool Ordered(IEnumerable<SceneKnowledgePacket> p)=>p.SequenceEqual(p.OrderBy(x=>x.SceneNumber).ThenBy(x=>x.FrameNumber));
    private static bool ClaimIdentical(CertifiedNarrationClaim left, CertifiedNarrationClaim right) =>
        Phase7Determinism.Hash(left) == Phase7Determinism.Hash(right);
    private static bool LocationTimeSafe(CertifiedNarrationClaim claim, Phase7ScenePacketInputAuthority input) =>
        !(claim.IsLocationDependent || claim.IsDateTimeDependent) ||
        Phase7KnowledgePolicyFacts.Scoped(input.Knowledge.KnowledgeAuthority, claim) ||
        Phase7KnowledgePolicyFacts.Qualified(input.Knowledge.KnowledgeAuthority, claim);
    private static bool HasRequiredEvidence(CertifiedNarrationClaim claim, Phase7ScenePacketInputAuthority input) =>
        input.Knowledge.KnowledgeAuthority.ClaimSupportEvidence.Any(e => e.ClaimId==claim.ClaimId &&
            e.SemanticIdentity==claim.SemanticIdentity && claim.SourceIds.Contains(e.SourceId,StringComparer.Ordinal) &&
            e.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim && !e.RequiresHumanReview &&
            e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField);
    private static bool Partitions(SceneKnowledgePacket p)
    { var r=p.RequiredClaims.Select(x=>x.ClaimId).ToArray();var o=p.OptionalClaims.Select(x=>x.ClaimId).ToArray();var d=p.DeferredClaims.Select(x=>x.ClaimId).ToArray();return !r.Intersect(o).Concat(r.Intersect(d)).Concat(o.Intersect(d)).Any()&&p.RequiredClaims.All(x=>x.Disposition==Phase7ClaimDisposition.Required)&&p.OptionalClaims.All(x=>x.Disposition==Phase7ClaimDisposition.Optional)&&p.DeferredClaims.All(x=>x.Disposition==Phase7ClaimDisposition.Deferred); }
}
