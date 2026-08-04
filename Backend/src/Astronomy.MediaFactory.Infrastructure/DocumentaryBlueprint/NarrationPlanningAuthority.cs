using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7NarrationPlanningInputAuthorityEvaluator(
    IPhase7ScenePacketInputAuthorityEvaluator committedEvaluator,
    IPhase7SceneKnowledgePacketValidator packetValidator) : IPhase7NarrationPlanningInputAuthorityEvaluator
{
    public async Task<Phase7NarrationPlanningInputAuthorityEvaluation> EvaluateAsync(
        Phase7NarrationPlanningInputAuthorityRequest request, CancellationToken token = default)
    {
        if (request.PacketValidation is null)
            return Fail("NARRATION_PLANNING_PACKET_VALIDATION_MISSING", "Packet validation is required.", []);
        var evaluated = await committedEvaluator.EvaluateAsync(new(request.ExecutionRoot, request.ExecutionId,
            request.PlanId, request.EventId, request.Language, request.ProfileId, request.ProfileVersion), token);
        if (!evaluated.IsValid || evaluated.Authority is null)
            return new(false, null, "NARRATION_PLANNING_INPUT_AUTHORITY_INVALID", evaluated.Errors, evaluated.Warnings);
        var validation = request.PacketValidation;
        if (!validation.IsValid || validation.ReasonCode != "P7PACKET_VALID" || validation.Gates.Any(g => !g.Passed) ||
            validation.DeterministicChecksum != Phase7Determinism.Hash(validation with { DeterministicChecksum = "" }))
            return Fail("NARRATION_PLANNING_PACKET_VALIDATION_INVALID", "Packet validation is invalid or its checksum does not recompute.", evaluated.Warnings, validation.Errors);

        var source = evaluated.Authority;
        var packets = request.SceneKnowledgePacketCollection;
        var all = packets.Long.Concat(packets.Short).ToArray();
        if (packets.DeterministicChecksum != NarrationPlanningCanonicalizer.PacketCollectionChecksum(packets) ||
            all.Any(p => p.DeterministicChecksum != Phase7SceneKnowledgePacketCanonicalizer.ComputeChecksum(p)))
            return Fail("NARRATION_PLANNING_PACKET_CHECKSUM_INVALID", "A packet or collection checksum is invalid.", evaluated.Warnings);
        if (all.Any(p => p.BlockingIssues.Count != 0))
            return Fail("NARRATION_PLANNING_PACKET_BLOCKED", "A packet contains blocking issues.", evaluated.Warnings);

        var recomputed = packetValidator.Validate(source, packets.Long, packets.Short);
        if (!recomputed.IsValid)
        {
            var failed = recomputed.Gates.Where(g => !g.Passed).Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
            var code = failed.Contains("StoryFrameCoverageGate") ? "NARRATION_PLANNING_PACKET_COVERAGE_INVALID" :
                failed.Overlaps(["StoryFrameChecksumGate", "SourceSceneLineageGate", "ResolutionReportLineageGate"]) ? "NARRATION_PLANNING_PACKET_LINEAGE_INVALID" :
                failed.Overlaps(["ProfileGate", "LanguageGate", "SceneIdentityGate", "VariantCoverageGate"]) ? "NARRATION_PLANNING_PACKET_IDENTITY_MISMATCH" :
                "NARRATION_PLANNING_PACKET_VALIDATION_INVALID";
            return new(false, null, code, recomputed.Errors, evaluated.Warnings.Concat(all.SelectMany(p => p.Warnings)).Distinct().ToArray());
        }
        var authority = new Phase7NarrationPlanningInputAuthority(source.StoryFrames, source.Knowledge, packets,
            validation, source.FamilyProfile, source.ExecutionId, source.PlanId, source.EventId, source.Language,
            source.ProfileId, source.ProfileVersion, source.LineageEvidence, source.RuntimeCompatibilityEvidence);
        return new(true, authority, "NARRATION_PLANNING_INPUT_AUTHORITY_VALID", [],
            evaluated.Warnings.Concat(all.SelectMany(p => p.Warnings)).Distinct().ToArray());
    }
    private static Phase7NarrationPlanningInputAuthorityEvaluation Fail(string code, string error,
        IReadOnlyList<string> warnings, IEnumerable<string>? upstream = null) =>
        new(false, null, code, (upstream ?? []).Append(error).ToArray(), warnings);
}

public sealed class DefaultNarrationPlanningConstraintPolicy : INarrationPlanningConstraintPolicy
{
    public NarrationPlanningConstraints Resolve(NarrationPlanningConstraintRequest r)
    {
        var secondsPerSentence = r.Language.StartsWith("hi", StringComparison.OrdinalIgnoreCase) ? 11m : 10m;
        var claimFloor = Math.Max(1, r.RequiredClaimCount);
        var preferred = Math.Max(claimFloor, (int)Math.Round(r.TargetDurationSeconds / secondsPerSentence));
        var minimum = Math.Max(1, Math.Min(preferred, (int)Math.Floor(r.MinimumDurationSeconds / secondsPerSentence)));
        var maximum = Math.Max(preferred, (int)Math.Ceiling(r.MaximumDurationSeconds / secondsPerSentence));
        return new(minimum, preferred, maximum, Math.Clamp(r.TargetDurationSeconds, r.MinimumDurationSeconds, r.MaximumDurationSeconds),
            "SemanticBeatBoundaries", ["ViewerQuestion", "RequiredClaims", "VisualTargets"],
            "RequiredInPacketOrder;OptionalAfterRequired;DeferredUnavailable", "PacketVisualEvidenceAuthoredOrder");
    }
}

public static class NarrationPlanningCanonicalizer
{
    public static string PacketCollectionChecksum(SceneKnowledgePacketCollection c) => Phase7Determinism.Hash(new { c.Long, c.Short });
    public static string GoalChecksum(NarrationPlanningGoal x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    public static string StrategyChecksum(NarrationPlanningStrategy x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    public static string TransitionChecksum(NarrationPlanningTransition x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    public static string TransitionId(NarrationPlanningTransition x) => $"transition-{x.Variant.ToLowerInvariant()}-{TransitionChecksum(x)[..20]}";
    public static string SceneChecksum(NarrationPlanningScene x) => Phase7Determinism.Hash(CanonicalScene(x with { DeterministicChecksum = "" }));
    public static string PlanningId(NarrationPlanningScene x) => $"planning-{x.Variant.ToLowerInvariant()}-{SceneChecksum(x)[..20]}";
    public static string DiagnosticsChecksum(NarrationPlanningDiagnostics x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    public static string AuthorityChecksum(NarrationPlanningAuthority x) => Phase7Determinism.Hash(CanonicalAuthority(x with { DeterministicChecksum = "" }));
    public static string AuthorityId(NarrationPlanningAuthority x) => $"narration-planning-{AuthorityChecksum(x)[..20]}";
    public static string ValidationChecksum(NarrationPlanningValidation x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    private static NarrationPlanningScene CanonicalScene(NarrationPlanningScene x) => x with {
        RequiredClaims = x.RequiredClaims.Order(StringComparer.Ordinal).ToArray(), OptionalClaims = x.OptionalClaims.Order(StringComparer.Ordinal).ToArray(),
        DeferredClaims = x.DeferredClaims.Order(StringComparer.Ordinal).ToArray(), ForbiddenStatements = x.ForbiddenStatements.Order(StringComparer.Ordinal).ToArray(),
        SafetyRequirements = x.SafetyRequirements.Order(StringComparer.Ordinal).ToArray(), EditorialConstraints = x.EditorialConstraints.Order(StringComparer.Ordinal).ToArray(),
        CulturalQualificationRequirements = x.CulturalQualificationRequirements.Order(StringComparer.Ordinal).ToArray(),
        LocationQualificationRequirements = x.LocationQualificationRequirements.Order(StringComparer.Ordinal).ToArray(),
        TimeQualificationRequirements = x.TimeQualificationRequirements.Order(StringComparer.Ordinal).ToArray(),
        AstrologyQualificationRequirements = x.AstrologyQualificationRequirements.Order(StringComparer.Ordinal).ToArray(),
        HumanReviewRequirements = x.HumanReviewRequirements.Order(StringComparer.Ordinal).ToArray() };
    private static NarrationPlanningAuthority CanonicalAuthority(NarrationPlanningAuthority x)
    {
        static SortedDictionary<string,string> Sorted(IReadOnlyDictionary<string,string> source)
        { var result = new SortedDictionary<string,string>(StringComparer.Ordinal); foreach (var pair in source) result[pair.Key] = pair.Value; return result; }
        return x with { Phase4To7Lineage = Sorted(x.Phase4To7Lineage), RuntimeCompatibilityEvidence = Sorted(x.RuntimeCompatibilityEvidence) };
    }
}

public sealed class NarrationPlanningAuthorityBuilder(INarrationPlanningConstraintPolicy constraintPolicy) : INarrationPlanningAuthorityBuilder
{
    public NarrationPlanningAuthority Build(Phase7NarrationPlanningInputAuthority input)
    {
        var longs = BuildVariant(input, input.SceneKnowledgePacketCollection.Long, "Long");
        var shorts = BuildVariant(input, input.SceneKnowledgePacketCollection.Short, "Short");
        var scenes = longs.Concat(shorts).ToArray();
        var warnings = input.SceneKnowledgePacketCollection.Long.Concat(input.SceneKnowledgePacketCollection.Short).SelectMany(p => p.Warnings).Distinct().Order().ToArray();
        var dd = new NarrationPlanningDiagnostics(scenes.Length, scenes.Length, scenes.Sum(x => x.RequiredClaims.Count),
            scenes.Sum(x => x.OptionalClaims.Count), scenes.Sum(x => x.DeferredClaims.Count), warnings.Length, 0, warnings, [], "");
        var diagnostics = dd with { DeterministicChecksum = NarrationPlanningCanonicalizer.DiagnosticsChecksum(dd) };
        var draft = new NarrationPlanningAuthority(NarrationPlanningContract.Version, "", input.ExecutionId, input.PlanId,
            input.EventId, input.Language, input.ProfileId, input.ProfileVersion,
            input.PublishedStoryFrameAuthority.Authority.SemanticChecksum, input.PublishedPhase7KnowledgeAuthority.KnowledgeAuthority.SemanticChecksum,
            input.SceneKnowledgePacketCollection.DeterministicChecksum, longs, shorts, diagnostics, input.Phase4To7Lineage,
            input.RuntimeCompatibilityEvidence, "");
        draft = draft with { AuthorityId = NarrationPlanningCanonicalizer.AuthorityId(draft) };
        return draft with { DeterministicChecksum = NarrationPlanningCanonicalizer.AuthorityChecksum(draft) };
    }
    private IReadOnlyList<NarrationPlanningScene> BuildVariant(Phase7NarrationPlanningInputAuthority input, IReadOnlyList<SceneKnowledgePacket> packets, string variant)
    {
        var ordered = packets.OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
        return ordered.Select((p, i) => BuildScene(input, p, variant, i == 0 ? null : ordered[i-1], i == ordered.Length-1 ? null : ordered[i+1])).ToArray();
    }
    private NarrationPlanningScene BuildScene(Phase7NarrationPlanningInputAuthority input, SceneKnowledgePacket p, string variant, SceneKnowledgePacket? previous, SceneKnowledgePacket? next)
    {
        var primaryResolutions = p.ReferenceResolutions.Where(x => x.IsPrimary && x.Status == Phase7KnowledgeReferenceStatus.Resolved && x.ResolvedClaimIds.Count > 0 && p.KnowledgeReferenceIds.Contains(x.ReferenceId, StringComparer.Ordinal)).ToArray();
        if (primaryResolutions.Length != 1) throw new InvalidOperationException("NARRATION_PLANNING_PRIMARY_REFERENCE_INVALID");
        var primary = primaryResolutions.Select(x => x.ReferenceId).ToArray();
        var supportingIds = p.ReferenceResolutions.Where(x => !x.IsPrimary && x.Status == Phase7KnowledgeReferenceStatus.Resolved).Select(x => x.ReferenceId).ToHashSet(StringComparer.Ordinal);
        var supporting = p.KnowledgeReferenceIds.Where(supportingIds.Contains).ToArray();
        var constraints = constraintPolicy.Resolve(new(input.Language, variant, p.TargetDurationSeconds, p.MinimumDurationSeconds,
            p.MaximumDurationSeconds, input.FamilyNarrationProfile, p.SceneRole, p.SectionKey, p.RequiredClaims.Count, p.OptionalClaims.Count));
        var goalDraft = new NarrationPlanningGoal(p.SceneRole, p.SectionKey, p.ViewerQuestionId, p.LearningObjectiveId,
            p.RequiredClaims.Select(c => c.ClaimId).ToArray(), input.ProfileId, "CertifiedClaimsAndViewerQuestion", "");
        var goal = goalDraft with { DeterministicChecksum = NarrationPlanningCanonicalizer.GoalChecksum(goalDraft) };
        var strategyDraft = new NarrationPlanningStrategy(p.NarrativeStage, p.SceneRole, p.SectionKey, "VariantOpeningWhenFirst",
            "RequiredThenOptional", "VariantClosingWhenLast", "RequiredInPacketOrder", "OptionalOnlyWhenTimeAllows",
            "UnavailableForFactualDrafting", "PacketLineageOnly", "");
        var strategy = strategyDraft with { DeterministicChecksum = NarrationPlanningCanonicalizer.StrategyChecksum(strategyDraft) };
        var incoming = Transition(input, variant, previous, p, previous, p, next, previous is null ? "VariantOpening" : "StoryFrameSuccessor");
        var outgoing = Transition(input, variant, p, next, previous, p, next, next is null ? "VariantClosing" : "StoryFrameSuccessor");
        string[] Qualified(Func<CertifiedNarrationClaim,bool> predicate, string prefix) => p.RequiredClaims.Concat(p.OptionalClaims).Where(predicate).Select(c => $"{prefix}:{c.ClaimId}").ToArray();
        var draft = new NarrationPlanningScene("", p.SourceSceneId, variant, p.StoryFrameId, p.StoryFrameChecksum,
            p.SourceSceneChecksum, p.PacketId, p.DeterministicChecksum, p.ResolvedViewerQuestionText, p.SceneObjective, goal,
            primary, supporting, p.RequiredClaims.Select(c => c.ClaimId).ToArray(), p.OptionalClaims.Select(c => c.ClaimId).ToArray(),
            p.DeferredClaims.Select(c => c.ClaimId).ToArray(), strategy, new("MandatoryFactualAuthority", "ConditionalFactualAuthority", "UnavailableForFactualDrafting"), constraints,
            p.ProhibitedClaims, p.SafetyRules, p.EditorialConstraints, Qualified(c => c.IsCultural || c.IsMythological, "QualifyCulture"),
            Qualified(c => c.IsLocationDependent, "QualifyLocation"), Qualified(c => c.IsDateTimeDependent, "QualifyDateTime"),
            Qualified(c => c.IsAstrologyRelated, "ClarifyAstrology"), p.RequiredClaims.Concat(p.OptionalClaims).Concat(p.DeferredClaims).Where(c => c.RequiresHumanReview).Select(c => $"HumanReview:{c.ClaimId}").ToArray(),
            p.MinimumDurationSeconds, p.TargetDurationSeconds, p.MaximumDurationSeconds, constraints.PreferredSentenceCount,
            constraints.ReadingTimeTargetSeconds, p.VisualEvidenceIds, incoming, outgoing, "");
        draft = draft with { PlanningId = NarrationPlanningCanonicalizer.PlanningId(draft) };
        return draft with { DeterministicChecksum = NarrationPlanningCanonicalizer.SceneChecksum(draft) };
    }
    private static NarrationPlanningTransition Transition(Phase7NarrationPlanningInputAuthority input, string variant,
        SceneKnowledgePacket? from, SceneKnowledgePacket? to, SceneKnowledgePacket? previous, SceneKnowledgePacket current,
        SceneKnowledgePacket? next, string kind)
    {
        StoryFrameAuthorityFrame? Frame(SceneKnowledgePacket? p) => p is null ? null : input.PublishedStoryFrameAuthority.Authority.Frames.Single(f => f.FrameId == p.StoryFrameId);
        var ff = Frame(from); var tf = Frame(to);
        var draft = new NarrationPlanningTransition("", input.ExecutionId, variant, from?.StoryFrameId, from?.StoryFrameChecksum,
            to?.StoryFrameId, to?.StoryFrameChecksum, kind, ff?.TransitionOut, tf?.TransitionIn, from?.PacketId,
            to?.PacketId ?? from?.PacketId, null, "");
        draft = draft with { TransitionId = NarrationPlanningCanonicalizer.TransitionId(draft) };
        return draft with { DeterministicChecksum = NarrationPlanningCanonicalizer.TransitionChecksum(draft) };
    }
}

public sealed class NarrationPlanningValidator : INarrationPlanningValidator
{
    private static readonly string[] Names = ["ContractGate", "InputAuthorityGate", "ProfileGate", "LanguageGate", "PlanningCoverageGate", "ScenePlanningGate", "PacketLineageGate", "ViewerQuestionGate", "LearningObjectiveGate", "NarrativeGoalGate", "TransitionGate", "ConstraintGate", "RequiredClaimPlanningGate", "OptionalClaimPlanningGate", "DeferredClaimPlanningGate", "SafetyPlanningGate", "CulturalPlanningGate", "LocationTimePlanningGate", "AstrologyPlanningGate", "HumanReviewPlanningGate", "LongShortIndependenceGate", "DiagnosticsGate", "AuthorityChecksumGate", "DeterminismGate"];
    public NarrationPlanningValidation Validate(Phase7NarrationPlanningInputAuthority input, NarrationPlanningAuthority authority)
    {
        var packets = input.SceneKnowledgePacketCollection.Long.Concat(input.SceneKnowledgePacketCollection.Short).ToDictionary(p => p.PacketId);
        var scenes = authority.LongScenes.Concat(authority.ShortScenes).ToArray();
        bool Claims(Func<SceneKnowledgePacket,IEnumerable<CertifiedNarrationClaim>> select, Func<NarrationPlanningScene,IEnumerable<string>> planned) => scenes.All(s => select(packets[s.PacketId]).Select(c => c.ClaimId).SequenceEqual(planned(s)));
        bool Transitions(IReadOnlyList<NarrationPlanningScene> list, string variant) => list.Count > 0 && list.All(s => s.Variant == variant) &&
            list[0].IncomingTransition is { Kind:"VariantOpening", FromStoryFrameId:null } && list[0].IncomingTransition.ToStoryFrameId == list[0].StoryFrameId &&
            list[^1].OutgoingTransition is { Kind:"VariantClosing", ToStoryFrameId:null } && list[^1].OutgoingTransition.FromStoryFrameId == list[^1].StoryFrameId &&
            list.Zip(list.Skip(1)).All(z => z.First.OutgoingTransition.TransitionId == z.Second.IncomingTransition.TransitionId && z.First.OutgoingTransition.ToStoryFrameId == z.Second.StoryFrameId && z.Second.IncomingTransition.FromStoryFrameId == z.First.StoryFrameId) &&
            list.SelectMany(s => new[]{s.IncomingTransition,s.OutgoingTransition}).All(t => t.ExecutionId == input.ExecutionId && t.Variant == variant && t.DeterministicChecksum == NarrationPlanningCanonicalizer.TransitionChecksum(t));
        var checks = new Dictionary<string,bool>(StringComparer.Ordinal) {
            [Names[0]]=authority.ContractVersion==NarrationPlanningContract.Version, [Names[1]]=authority.ExecutionId==input.ExecutionId&&authority.PlanId==input.PlanId&&authority.EventId==input.EventId&&authority.StoryFrameAuthorityChecksum==input.PublishedStoryFrameAuthority.Authority.SemanticChecksum&&authority.KnowledgeAuthorityChecksum==input.PublishedPhase7KnowledgeAuthority.KnowledgeAuthority.SemanticChecksum&&authority.PacketCollectionChecksum==input.SceneKnowledgePacketCollection.DeterministicChecksum,
            [Names[2]]=authority.ProfileId==input.ProfileId&&authority.ProfileVersion==input.ProfileVersion, [Names[3]]=authority.Language==input.Language,
            [Names[4]]=scenes.Length==packets.Count&&packets.Keys.All(id=>scenes.Count(s=>s.PacketId==id)==1), [Names[5]]=scenes.All(s=>s.PlanningId==NarrationPlanningCanonicalizer.PlanningId(s with{PlanningId="",DeterministicChecksum=""})),
            [Names[6]]=scenes.All(s=>packets.TryGetValue(s.PacketId,out var p)&&p.DeterministicChecksum==s.PacketChecksum&&p.StoryFrameId==s.StoryFrameId&&p.StoryFrameChecksum==s.StoryFrameChecksum&&p.SourceSceneChecksum==s.SourceSceneChecksum),
            [Names[7]]=scenes.All(s=>s.ViewerQuestion==packets[s.PacketId].ResolvedViewerQuestionText), [Names[8]]=scenes.All(s=>s.LearningObjective==packets[s.PacketId].SceneObjective),
            [Names[9]]=scenes.All(s=>s.NarrativeGoal.DeterministicChecksum==NarrationPlanningCanonicalizer.GoalChecksum(s.NarrativeGoal)), [Names[10]]=Transitions(authority.LongScenes,"Long")&&Transitions(authority.ShortScenes,"Short"),
            [Names[11]]=scenes.All(s=>s.NarrationConstraints.MinimumSentenceCount<=s.ExpectedSentenceCount&&s.ExpectedSentenceCount<=s.NarrationConstraints.MaximumSentenceCount&&s.EstimatedReadingTime>=s.MinimumDuration&&s.EstimatedReadingTime<=s.MaximumDuration),
            [Names[12]]=Claims(p=>p.RequiredClaims,s=>s.RequiredClaims), [Names[13]]=Claims(p=>p.OptionalClaims,s=>s.OptionalClaims), [Names[14]]=Claims(p=>p.DeferredClaims,s=>s.DeferredClaims),
            [Names[15]]=scenes.All(s=>packets[s.PacketId].SafetyRules.All(s.SafetyRequirements.Contains)&&packets[s.PacketId].EditorialConstraints.All(s.EditorialConstraints.Contains)&&packets[s.PacketId].ProhibitedClaims.All(s.ForbiddenStatements.Contains)),
            [Names[16]]=scenes.All(s=>packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c=>c.IsCultural||c.IsMythological).All(c=>s.CulturalQualificationRequirements.Contains($"QualifyCulture:{c.ClaimId}"))),
            [Names[17]]=scenes.All(s=>packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c=>c.IsLocationDependent).All(c=>s.LocationQualificationRequirements.Contains($"QualifyLocation:{c.ClaimId}"))&&packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c=>c.IsDateTimeDependent).All(c=>s.TimeQualificationRequirements.Contains($"QualifyDateTime:{c.ClaimId}"))),
            [Names[18]]=scenes.All(s=>packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c=>c.IsAstrologyRelated).All(c=>s.AstrologyQualificationRequirements.Contains($"ClarifyAstrology:{c.ClaimId}"))),
            [Names[19]]=scenes.All(s=>!packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Any(c=>c.RequiresHumanReview)&&packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Concat(packets[s.PacketId].DeferredClaims).Where(c=>c.RequiresHumanReview).All(c=>s.HumanReviewRequirements.Contains($"HumanReview:{c.ClaimId}"))),
            [Names[20]]=authority.LongScenes.All(s=>packets[s.PacketId].Variant=="Long")&&authority.ShortScenes.All(s=>packets[s.PacketId].Variant=="Short")&&!authority.LongScenes.Select(s=>s.StoryFrameId).Intersect(authority.ShortScenes.Select(s=>s.StoryFrameId)).Any()&&!authority.LongScenes.Select(s=>s.PlanningId).Intersect(authority.ShortScenes.Select(s=>s.PlanningId)).Any(),
            [Names[21]]=authority.Diagnostics.PacketCount==packets.Count&&authority.Diagnostics.PlanningSceneCount==scenes.Length&&authority.Diagnostics.RequiredClaimCount==scenes.Sum(s=>s.RequiredClaims.Count)&&authority.Diagnostics.OptionalClaimCount==scenes.Sum(s=>s.OptionalClaims.Count)&&authority.Diagnostics.DeferredClaimCount==scenes.Sum(s=>s.DeferredClaims.Count)&&authority.Diagnostics.WarningCount==authority.Diagnostics.Warnings.Count&&authority.Diagnostics.ErrorCount==authority.Diagnostics.Errors.Count&&authority.Diagnostics.DeterministicChecksum==NarrationPlanningCanonicalizer.DiagnosticsChecksum(authority.Diagnostics),
            [Names[22]]=authority.AuthorityId==NarrationPlanningCanonicalizer.AuthorityId(authority with{AuthorityId="",DeterministicChecksum=""})&&authority.DeterministicChecksum==NarrationPlanningCanonicalizer.AuthorityChecksum(authority),
            [Names[23]]=scenes.All(s=>s.DeterministicChecksum==NarrationPlanningCanonicalizer.SceneChecksum(s)) };
        var gates=Names.Select(n=>new NarrationPlanningValidationGate(n,checks[n],checks[n]?[]:[$"{n} failed."])).ToArray();
        var errors=gates.SelectMany(g=>g.Errors).ToArray(); var draft=new NarrationPlanningValidation(errors.Length==0,errors.Length==0?"NARRATION_PLANNING_VALID":"NARRATION_PLANNING_INVALID",gates,errors,"");
        return draft with { DeterministicChecksum=NarrationPlanningCanonicalizer.ValidationChecksum(draft) };
    }
}
