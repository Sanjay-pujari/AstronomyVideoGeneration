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
        if (!recomputed.IsValid || recomputed.ReasonCode != "P7PACKET_VALID" || recomputed.Gates.Any(g => !g.Passed))
        {
            var failed = recomputed.Gates.Where(g => !g.Passed).Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
            var code = failed.Contains("StoryFrameCoverageGate") ? "NARRATION_PLANNING_PACKET_COVERAGE_INVALID" :
                failed.Overlaps(["StoryFrameChecksumGate", "SourceSceneLineageGate", "ResolutionReportLineageGate"]) ? "NARRATION_PLANNING_PACKET_LINEAGE_INVALID" :
                failed.Overlaps(["ProfileGate", "LanguageGate", "SceneIdentityGate", "VariantCoverageGate"]) ? "NARRATION_PLANNING_PACKET_IDENTITY_MISMATCH" :
                "NARRATION_PLANNING_PACKET_VALIDATION_INVALID";
            return new(false, null, code, recomputed.Errors, evaluated.Warnings.Concat(all.SelectMany(p => p.Warnings)).Distinct().ToArray());
        }
        var suppliedGates = validation.Gates.Select(g => (g.Name, g.Passed)).ToArray();
        var recomputedGates = recomputed.Gates.Select(g => (g.Name, g.Passed)).ToArray();
        if (validation.ReasonCode != recomputed.ReasonCode ||
            !suppliedGates.SequenceEqual(recomputedGates) ||
            validation.DeterministicChecksum != recomputed.DeterministicChecksum)
            return Fail("NARRATION_PLANNING_PACKET_VALIDATION_MISMATCH",
                "Supplied packet validation does not match authoritative recomputation.", evaluated.Warnings);
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

public sealed class DeterministicNarrationPlanningDraftRealizabilityPolicy : INarrationPlanningDraftRealizabilityPolicy
{
    public NarrationPlanningRealizabilityBudget Evaluate(NarrationPlanningDraftRealizabilityRequest r)
    {
        static int CountOwned(NarrationPlanningTransition t, bool incoming)
        {
            if (t.Kind != NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition) return 0;
            var text = incoming ? t.DestinationTransitionIn : t.SourceTransitionOut;
            return string.IsNullOrWhiteSpace(text) ? 0 : 1;
        }
        var required = r.RequiredClaimIds.Distinct(StringComparer.Ordinal).Count();
        var incomingCount = CountOwned(r.IncomingTransition, true);
        var outgoingCount = CountOwned(r.OutgoingTransition, false);
        const int mandatoryQualifications = 0;
        var minimumMandatory = required + mandatoryQualifications + incomingCount + outgoingCount;
        var ok = minimumMandatory <= r.Constraints.MaximumSentenceCount;
        return new(required, mandatoryQualifications, incomingCount, outgoingCount, 0, minimumMandatory,
            r.Constraints.MaximumSentenceCount, ok, ok ? "NARRATION_PLANNING_DRAFT_CAPACITY_VALID" : "NARRATION_PLANNING_DRAFT_CAPACITY_INVALID");
    }
}

public static class NarrationPlanningCanonicalizer
{
    public static string PacketCollectionChecksum(SceneKnowledgePacketCollection c) => Phase7Determinism.Hash(new { c.Long, c.Short });
    public static string GoalChecksum(NarrationPlanningGoal x) => Phase7Determinism.Hash(x with
        { RequiredClaimIds = x.RequiredClaimIds.Order(StringComparer.Ordinal).ToArray(), DeterministicChecksum = "" });
    public static string StrategyChecksum(NarrationPlanningStrategy x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    public static string TransitionChecksum(NarrationPlanningTransition x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    public static string ComputeTransitionId(NarrationPlanningTransition x) => $"transition-{x.Variant.ToLowerInvariant()}-{Phase7Determinism.Hash(new
    {
        ContractVersion = NarrationPlanningContract.Version,
        x.ExecutionId, x.Variant, x.Kind, x.FromStoryFrameId, x.FromStoryFrameChecksum,
        x.ToStoryFrameId, x.ToStoryFrameChecksum, x.SourceTransitionOut, x.DestinationTransitionIn,
        x.PreviousPacketId, x.CurrentPacketId, x.NextPacketId
    })[..20]}";
    public static string TransitionId(NarrationPlanningTransition x) => ComputeTransitionId(x);
    public static string SceneChecksum(NarrationPlanningScene x) => Phase7Determinism.Hash(CanonicalScene(x with { DeterministicChecksum = "" }));
    public static string PlanningId(NarrationPlanningScene x) => $"planning-{x.Variant.ToLowerInvariant()}-{SceneChecksum(x)[..20]}";
    public static string DiagnosticsChecksum(NarrationPlanningDiagnostics x) => Phase7Determinism.Hash(x with
        { Warnings = x.Warnings.Order(StringComparer.Ordinal).ToArray(), Errors = x.Errors.Order(StringComparer.Ordinal).ToArray(), DeterministicChecksum = "" });
    public static string AuthorityChecksum(NarrationPlanningAuthority x) => Phase7Determinism.Hash(CanonicalAuthority(x with { DeterministicChecksum = "" }));
    public static string AuthorityId(NarrationPlanningAuthority x) => $"narration-planning-{AuthorityChecksum(x)[..20]}";
    public static string ValidationChecksum(NarrationPlanningValidation x) => Phase7Determinism.Hash(x with { DeterministicChecksum = "" });
    // Authored-order collections (scenes, governed references, visual targets, and profile emphasis)
    // are intentionally left untouched. Semantic-set collections are ordinally normalized below.
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

public static class NarrationPlanningReferenceGovernance
{
    /// <summary>
    /// Establishes packet/reference variant ownership from the validated P7.1B-A boundary. Reference text is
    /// deliberately not interpreted as variant metadata.
    /// </summary>
    public static bool HasValidatedVariantOwnership(Phase7NarrationPlanningInputAuthority input, SceneKnowledgePacket packet)
    {
        if (input.PacketValidation is null || !input.PacketValidation.IsValid ||
            input.PacketValidation.ReasonCode != "P7PACKET_VALID" || input.PacketValidation.Gates.Any(g => !g.Passed))
            return false;

        var inLong = input.SceneKnowledgePacketCollection.Long.Count(p =>
            string.Equals(p.PacketId, packet.PacketId, StringComparison.Ordinal));
        var inShort = input.SceneKnowledgePacketCollection.Short.Count(p =>
            string.Equals(p.PacketId, packet.PacketId, StringComparison.Ordinal));
        var ownedByLong = inLong == 1 && inShort == 0 && packet.Variant == "Long";
        var ownedByShort = inShort == 1 && inLong == 0 && packet.Variant == "Short";
        return (ownedByLong || ownedByShort) &&
            packet.ReferenceResolutions.Where(r => r.IsPrimary).All(r =>
                packet.KnowledgeReferenceIds.Contains(r.ReferenceId, StringComparer.Ordinal)) &&
            packet.ReferenceResolutions.All(r => r.Status != Phase7KnowledgeReferenceStatus.CrossVariantInvalid);
    }

    public static bool IsGovernedResolvedPrimary(Phase7NarrationPlanningInputAuthority input,
        SceneKnowledgePacket packet, Phase7PacketReferenceResolution resolution)
    {
        var required = packet.RequiredClaims.Select(c => c.ClaimId).ToHashSet(StringComparer.Ordinal);
        var allClaims = required.Concat(packet.OptionalClaims.Select(c => c.ClaimId))
            .Concat(packet.DeferredClaims.Select(c => c.ClaimId)).ToHashSet(StringComparer.Ordinal);
        return HasValidatedVariantOwnership(input, packet) && resolution.IsPrimary &&
            resolution.Status == Phase7KnowledgeReferenceStatus.Resolved &&
            resolution.ResolvedClaimIds.Count > 0 &&
            packet.KnowledgeReferenceIds.Contains(resolution.ReferenceId, StringComparer.Ordinal) &&
            resolution.ResolvedClaimIds.All(allClaims.Contains) &&
            (!resolution.IsRequired || resolution.ResolvedClaimIds.Any(required.Contains));
    }

    public static IReadOnlyList<string> GovernedPrimaryReferences(Phase7NarrationPlanningInputAuthority input,
        SceneKnowledgePacket packet)
    {
        var governed = packet.ReferenceResolutions.Where(r => IsGovernedResolvedPrimary(input, packet, r))
            .Select(r => r.ReferenceId).ToHashSet(StringComparer.Ordinal);
        return packet.KnowledgeReferenceIds.Where(governed.Contains).ToArray();
    }

    public static IReadOnlyList<string> GovernedSupportingReferences(SceneKnowledgePacket packet)
    {
        var governed = packet.ReferenceResolutions.Where(r => !r.IsPrimary &&
                r.Status == Phase7KnowledgeReferenceStatus.Resolved && r.ResolvedClaimIds.Count > 0 &&
                packet.KnowledgeReferenceIds.Contains(r.ReferenceId, StringComparer.Ordinal))
            .Select(r => r.ReferenceId).ToHashSet(StringComparer.Ordinal);
        return packet.KnowledgeReferenceIds.Where(governed.Contains).ToArray();
    }
}

public sealed class NarrationPlanningAuthorityBuilder(INarrationPlanningConstraintPolicy constraintPolicy, INarrationPlanningDraftRealizabilityPolicy realizabilityPolicy) : INarrationPlanningAuthorityBuilder
{
    public NarrationPlanningAuthorityBuildResult Build(Phase7NarrationPlanningInputAuthority input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.SceneKnowledgePacketCollection is null || input.PublishedStoryFrameAuthority is null ||
            input.PublishedPhase7KnowledgeAuthority is null || input.FamilyNarrationProfile is null)
            return Failure("NARRATION_PLANNING_INPUT_INVALID", "The typed planning input is incomplete.");

        var packets = input.SceneKnowledgePacketCollection.Long.Concat(input.SceneKnowledgePacketCollection.Short).ToArray();
        if (packets.Any(p => !NarrationPlanningReferenceGovernance.HasValidatedVariantOwnership(input, p)))
            return Failure("NARRATION_PLANNING_PACKET_VARIANT_OWNERSHIP_INVALID",
                "Packet variant ownership is not validated by the P7PACKET_VALID boundary.", packets);
        if (packets.Any(p => string.IsNullOrWhiteSpace(p.PacketId) || p.DeterministicChecksum != Phase7SceneKnowledgePacketCanonicalizer.ComputeChecksum(p)))
            return Failure("NARRATION_PLANNING_PACKET_NOT_FOUND", "A packet has invalid identity or checksum.", packets);
        var frames = input.PublishedStoryFrameAuthority.Authority.Frames;
        if (packets.Any(p => frames.Count(f => f.FrameId == p.StoryFrameId) != 1))
            return Failure("NARRATION_PLANNING_STORY_FRAME_NOT_FOUND", "A packet does not resolve to exactly one Story Frame.", packets);
        if (packets.Any(p => p.ReferenceResolutions.Count(x => NarrationPlanningReferenceGovernance.IsGovernedResolvedPrimary(input, p, x)) != 1))
            return Failure("NARRATION_PLANNING_PRIMARY_REFERENCE_INVALID", "A packet must contain exactly one governed resolved Primary.", packets);
        if (packets.Any(p => !Partitions(p)))
            return Failure("NARRATION_PLANNING_CLAIM_PARTITION_INVALID", "A packet claim occurs in an invalid partition.", packets);

        var longResult = BuildVariant(input, input.SceneKnowledgePacketCollection.Long, "Long");
        if (!longResult.IsValid) return longResult.Failure!;
        var shortResult = BuildVariant(input, input.SceneKnowledgePacketCollection.Short, "Short");
        if (!shortResult.IsValid) return shortResult.Failure!;
        var longs = longResult.Scenes!; var shorts = shortResult.Scenes!;
        var scenes = longs.Concat(shorts).ToArray();
        var realizabilityDiagnostics = RealizabilityDiagnostics(longs, shorts);
        var warnings = packets.SelectMany(p => p.Warnings).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var references = packets.SelectMany(p => p.ReferenceResolutions).ToArray();
        var dd = new NarrationPlanningDiagnostics(packets.Length, scenes.Length, longs.Count, shorts.Count,
            packets.Sum(p => p.ReferenceResolutions.Count(r => NarrationPlanningReferenceGovernance.IsGovernedResolvedPrimary(input, p, r))), scenes.Sum(x => x.SupportingKnowledgeReferences.Count),
            references.Count(x => x.IsRequired), references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Resolved),
            references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Deferred),
            references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Missing),
            references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Ambiguous),
            references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.CrossVariantInvalid),
            references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Unsupported),
            references.Count(x => x.Status is Phase7KnowledgeReferenceStatus.Missing or Phase7KnowledgeReferenceStatus.Ambiguous or Phase7KnowledgeReferenceStatus.CrossVariantInvalid or Phase7KnowledgeReferenceStatus.Unsupported), scenes.Length * 2,
            packets.Sum(x => x.BlockingIssues.Count), 0, scenes.Sum(x => x.RequiredClaims.Count),
            scenes.Sum(x => x.OptionalClaims.Count), scenes.Sum(x => x.DeferredClaims.Count), warnings.Length, 0, warnings, [], realizabilityDiagnostics, "");
        var diagnostics = dd with { DeterministicChecksum = NarrationPlanningCanonicalizer.DiagnosticsChecksum(dd) };
        var draft = new NarrationPlanningAuthority(NarrationPlanningContract.Version, "", input.ExecutionId, input.PlanId,
            input.EventId, input.Language, input.ProfileId, input.ProfileVersion,
            input.PublishedStoryFrameAuthority.Authority.SemanticChecksum, input.PublishedPhase7KnowledgeAuthority.KnowledgeAuthority.SemanticChecksum,
            input.SceneKnowledgePacketCollection.DeterministicChecksum, longs, shorts, diagnostics, input.Phase4To7Lineage,
            input.RuntimeCompatibilityEvidence, "");
        draft = draft with { AuthorityId = NarrationPlanningCanonicalizer.AuthorityId(draft) };
        var authority = draft with { DeterministicChecksum = NarrationPlanningCanonicalizer.AuthorityChecksum(draft) };
        return new(true, authority, "NARRATION_PLANNING_AUTHORITY_VALID", [], warnings, []);
    }

    private (bool IsValid, IReadOnlyList<NarrationPlanningScene>? Scenes, NarrationPlanningAuthorityBuildResult? Failure) BuildVariant(
        Phase7NarrationPlanningInputAuthority input, IReadOnlyList<SceneKnowledgePacket> packets, string variant)
    {
        var ordered = packets.OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
        var scenes = new List<NarrationPlanningScene>(ordered.Length);
        for (var i = 0; i < ordered.Length; i++)
        {
            var result = BuildScene(input, ordered[i], variant, i == 0 ? null : ordered[i - 1], i == ordered.Length - 1 ? null : ordered[i + 1]);
            if (!result.IsValid) return (false, null, result.Failure);
            scenes.Add(result.Scene!);
        }
        return (true, scenes, null);
    }

    private IReadOnlyList<NarrationPlanningSceneRealizabilityDiagnostic> RealizabilityDiagnostics(
        IReadOnlyList<NarrationPlanningScene> longs, IReadOnlyList<NarrationPlanningScene> shorts) =>
        longs.Select((s, i) => Diagnostic(s, i + 1)).Concat(shorts.Select((s, i) => Diagnostic(s, i + 1)))
            .OrderBy(x => x.Variant, StringComparer.Ordinal).ThenBy(x => x.SceneNumber).ToArray();

    private NarrationPlanningSceneRealizabilityDiagnostic Diagnostic(NarrationPlanningScene s, int sceneNumber)
    {
        var b = realizabilityPolicy.Evaluate(new(s.Variant, s.NarrativeGoal.SectionKey, s.RequiredClaims,
            s.IncomingTransition, s.OutgoingTransition, s.NarrationConstraints, s.LocationQualificationRequirements,
            s.TimeQualificationRequirements, s.CulturalQualificationRequirements, s.AstrologyQualificationRequirements));
        return new(s.PlanningId, s.Variant, sceneNumber, s.SceneId, s.StoryFrameId, s.NarrativeGoal.SectionKey,
            s.RequiredClaims, b.RequiredClaimSentenceCount, b.MandatoryIncomingTransitionSentenceCount,
            b.MandatoryOutgoingTransitionSentenceCount, b.MandatoryQualificationSentenceCount, b.MinimumMandatorySentenceCount,
            s.NarrationConstraints.MinimumSentenceCount, s.NarrationConstraints.PreferredSentenceCount,
            s.NarrationConstraints.MaximumSentenceCount, b.IsRealizable, b.IsRealizable ? [] : [b.ReasonCode, "NARRATION_PLANNING_SCENE_REQUIRED_CONTENT_EXCEEDS_CAPACITY"]);
    }

    private (bool IsValid, NarrationPlanningScene? Scene, NarrationPlanningAuthorityBuildResult? Failure) BuildScene(
        Phase7NarrationPlanningInputAuthority input, SceneKnowledgePacket p, string variant, SceneKnowledgePacket? previous, SceneKnowledgePacket? next)
    {
        var primary = NarrationPlanningReferenceGovernance.GovernedPrimaryReferences(input, p);
        var supporting = NarrationPlanningReferenceGovernance.GovernedSupportingReferences(p);
        NarrationPlanningConstraints constraints;
        try { constraints = constraintPolicy.Resolve(new(input.Language, variant, p.TargetDurationSeconds, p.MinimumDurationSeconds,
            p.MaximumDurationSeconds, input.FamilyNarrationProfile, p.SceneRole, p.SectionKey, p.RequiredClaims.Count, p.OptionalClaims.Count));
        } catch (ArgumentException ex) { return (false, null, Failure("NARRATION_PLANNING_CONSTRAINT_POLICY_INVALID", ex.Message, [p])); }
        if (constraints is null || constraints.MinimumSentenceCount < 1 || constraints.MinimumSentenceCount > constraints.PreferredSentenceCount ||
            constraints.PreferredSentenceCount > constraints.MaximumSentenceCount || constraints.ReadingTimeTargetSeconds < p.MinimumDurationSeconds ||
            constraints.ReadingTimeTargetSeconds > p.MaximumDurationSeconds)
            return (false, null, Failure("NARRATION_PLANNING_CONSTRAINT_POLICY_INVALID", "Constraint policy returned incoherent constraints.", [p]));
        var goalDraft = new NarrationPlanningGoal(p.SceneRole, p.SectionKey, p.ViewerQuestionId, p.LearningObjectiveId,
            p.RequiredClaims.Select(c => c.ClaimId).ToArray(), input.ProfileId, NarrationPlanningPolicyCatalog.GoalPolicy, "");
        var goal = goalDraft with { DeterministicChecksum = NarrationPlanningCanonicalizer.GoalChecksum(goalDraft) };
        var strategyDraft = new NarrationPlanningStrategy(p.NarrativeStage, p.SceneRole, p.SectionKey, NarrationPlanningPolicyCatalog.OpeningMode,
            NarrationPlanningPolicyCatalog.DevelopmentMode, NarrationPlanningPolicyCatalog.ClosingMode, NarrationPlanningPolicyCatalog.ClaimIntroductionPolicy,
            NarrationPlanningPolicyCatalog.OptionalClaimUsagePolicy, NarrationPlanningPolicyCatalog.DeferredClaimPolicy, NarrationPlanningPolicyCatalog.CallbackPolicy, "");
        var strategy = strategyDraft with { DeterministicChecksum = NarrationPlanningCanonicalizer.StrategyChecksum(strategyDraft) };
        var incoming = BuildIncomingTransition(input, variant, previous, p, next);
        var outgoing = BuildOutgoingTransition(input, variant, previous, p, next);
        string[] Qualified(Func<CertifiedNarrationClaim, bool> predicate, string prefix) => p.RequiredClaims.Concat(p.OptionalClaims)
            .Where(predicate).Select(c => $"{prefix}:{c.ClaimId}").ToArray();
        var draft = new NarrationPlanningScene("", p.SourceSceneId, variant, p.StoryFrameId, p.StoryFrameChecksum,
            p.SourceSceneChecksum, p.PacketId, p.DeterministicChecksum, p.ResolvedViewerQuestionText, p.SceneObjective, goal,
            primary, supporting, p.RequiredClaims.Select(c => c.ClaimId).ToArray(), p.OptionalClaims.Select(c => c.ClaimId).ToArray(),
            p.DeferredClaims.Select(c => c.ClaimId).ToArray(), strategy, new(NarrationPlanningPolicyCatalog.RequiredClaimUsage, NarrationPlanningPolicyCatalog.OptionalClaimUsage, NarrationPlanningPolicyCatalog.DeferredClaimUsage), constraints,
            p.ProhibitedClaims, p.SafetyRules, p.EditorialConstraints, Qualified(c => c.IsCultural || c.IsMythological, NarrationPlanningPolicyCatalog.CulturalQualificationPrefix),
            Qualified(c => c.IsLocationDependent, NarrationPlanningPolicyCatalog.LocationQualificationPrefix), Qualified(c => c.IsDateTimeDependent, NarrationPlanningPolicyCatalog.TimeQualificationPrefix),
            Qualified(c => c.IsAstrologyRelated, NarrationPlanningPolicyCatalog.AstrologyQualificationPrefix), p.RequiredClaims.Concat(p.OptionalClaims).Concat(p.DeferredClaims)
                .Where(c => c.RequiresHumanReview).Select(c => $"{NarrationPlanningPolicyCatalog.HumanReviewPrefix}:{c.ClaimId}").ToArray(),
            p.MinimumDurationSeconds, p.TargetDurationSeconds, p.MaximumDurationSeconds, constraints.PreferredSentenceCount,
            constraints.ReadingTimeTargetSeconds, p.VisualEvidenceIds, incoming, outgoing, "");
        draft = draft with { PlanningId = NarrationPlanningCanonicalizer.PlanningId(draft) };
        var budget = realizabilityPolicy.Evaluate(new(draft.Variant, draft.NarrativeGoal.SectionKey, draft.RequiredClaims,
            draft.IncomingTransition, draft.OutgoingTransition, draft.NarrationConstraints, draft.LocationQualificationRequirements,
            draft.TimeQualificationRequirements, draft.CulturalQualificationRequirements, draft.AstrologyQualificationRequirements));
        if (!budget.IsRealizable)
            return (false, null, Failure("NARRATION_PLANNING_SCENE_REQUIRED_CONTENT_EXCEEDS_CAPACITY",
                $"Required claims and owned transitions exceed planning capacity: planningId={draft.PlanningId};variant={draft.Variant};sectionKey={draft.NarrativeGoal.SectionKey};requiredClaims={budget.RequiredClaimSentenceCount};incomingTransitions={budget.MandatoryIncomingTransitionSentenceCount};outgoingTransitions={budget.MandatoryOutgoingTransitionSentenceCount};mandatoryQualifications={budget.MandatoryQualificationSentenceCount};minimumMandatorySentences={budget.MinimumMandatorySentenceCount};maximumSentences={budget.MaximumSentenceCount}.", [p]));
        return (true, draft with { DeterministicChecksum = NarrationPlanningCanonicalizer.SceneChecksum(draft) }, null);
    }

    private static NarrationPlanningTransition BuildIncomingTransition(Phase7NarrationPlanningInputAuthority input,
        string variant, SceneKnowledgePacket? previous, SceneKnowledgePacket current, SceneKnowledgePacket? next) =>
        Transition(input, variant, previous, current, previous, current, next, previous is null ? NarrationPlanningPolicyCatalog.VariantOpeningTransition : NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition);

    private static NarrationPlanningTransition BuildOutgoingTransition(Phase7NarrationPlanningInputAuthority input,
        string variant, SceneKnowledgePacket? previous, SceneKnowledgePacket current, SceneKnowledgePacket? next) =>
        Transition(input, variant, current, next, previous, current, next, next is null ? NarrationPlanningPolicyCatalog.VariantClosingTransition : NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition);

    private static NarrationPlanningTransition Transition(Phase7NarrationPlanningInputAuthority input, string variant,
        SceneKnowledgePacket? from, SceneKnowledgePacket? to, SceneKnowledgePacket? previous, SceneKnowledgePacket current,
        SceneKnowledgePacket? next, string kind)
    {
        StoryFrameAuthorityFrame? Frame(SceneKnowledgePacket? p) => p is null ? null :
            input.PublishedStoryFrameAuthority.Authority.Frames.Single(f => f.FrameId == p.StoryFrameId);
        var ff = Frame(from); var tf = Frame(to);
        var draft = new NarrationPlanningTransition("", input.ExecutionId, variant, from?.StoryFrameId, from?.StoryFrameChecksum,
            to?.StoryFrameId, to?.StoryFrameChecksum, kind, ff?.TransitionOut, tf?.TransitionIn, previous?.PacketId,
            current.PacketId, next?.PacketId, "");
        draft = draft with { TransitionId = NarrationPlanningCanonicalizer.ComputeTransitionId(draft) };
        return draft with { DeterministicChecksum = NarrationPlanningCanonicalizer.TransitionChecksum(draft) };
    }

    private static bool Partitions(SceneKnowledgePacket p)
    {
        var required = p.RequiredClaims.Select(x => x.ClaimId).ToArray();
        var optional = p.OptionalClaims.Select(x => x.ClaimId).ToArray();
        var deferred = p.DeferredClaims.Select(x => x.ClaimId).ToArray();
        return !p.RequiredClaims.Concat(p.OptionalClaims).Any(x => x.RequiresHumanReview) &&
            !required.Intersect(optional, StringComparer.Ordinal).Concat(required.Intersect(deferred, StringComparer.Ordinal))
            .Concat(optional.Intersect(deferred, StringComparer.Ordinal)).Any() &&
            p.RequiredClaims.All(x => x.Disposition == Phase7ClaimDisposition.Required) &&
            p.OptionalClaims.All(x => x.Disposition == Phase7ClaimDisposition.Optional) &&
            p.DeferredClaims.All(x => x.Disposition == Phase7ClaimDisposition.Deferred);
    }

    private static NarrationPlanningAuthorityBuildResult Failure(string code, string error,
        IEnumerable<SceneKnowledgePacket>? packets = null)
    {
        var warnings = packets?.SelectMany(p => p.Warnings).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray() ?? [];
        var upstreamBlockers = packets?.SelectMany(p => p.BlockingIssues).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray() ?? [];
        var blockers = upstreamBlockers.Append(code).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        return new(false, null, code, [error], warnings, blockers);
    }
}

public sealed class NarrationPlanningValidator(INarrationPlanningConstraintPolicy constraintPolicy, INarrationPlanningDraftRealizabilityPolicy realizabilityPolicy) : INarrationPlanningValidator
{
    private static readonly string[] Names = ["ContractGate", "InputAuthorityGate", "ProfileGate", "LanguageGate", "PlanningCoverageGate", "ScenePlanningGate", "SceneAuthorityGate", "ViewerQuestionGate", "LearningObjectiveGate", "NarrativeGoalGate", "StrategyGate", "TransitionGate", "ConstraintPolicyGate", "ClaimUsagePolicyGate", "RequiredClaimPlanningGate", "OptionalClaimPlanningGate", "DeferredClaimPlanningGate", "SafetyPlanningGate", "CulturalPlanningGate", "LocationTimePlanningGate", "AstrologyPlanningGate", "HumanReviewPlanningGate", "LongShortIndependenceGate", "DraftRealizabilityGate", "DiagnosticsGate", "AuthorityChecksumGate", "DeterminismGate"];

    public NarrationPlanningValidation Validate(Phase7NarrationPlanningInputAuthority input, NarrationPlanningAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(authority);
        var allPackets = input.SceneKnowledgePacketCollection.Long.Concat(input.SceneKnowledgePacketCollection.Short).ToArray();
        var packets = allPackets.GroupBy(p => p.PacketId, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var scenes = authority.LongScenes.Concat(authority.ShortScenes).ToArray();
        bool HasPacket(NarrationPlanningScene scene) => packets.ContainsKey(scene.PacketId);
        static bool SetEqual(IEnumerable<string> left, IEnumerable<string> right) =>
            left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        bool Claims(Func<SceneKnowledgePacket, IEnumerable<CertifiedNarrationClaim>> select,
            Func<NarrationPlanningScene, IEnumerable<string>> planned) => scenes.All(s => HasPacket(s) &&
                SetEqual(select(packets[s.PacketId]).Select(c => c.ClaimId), planned(s)));
        static bool ExactQualifications(IEnumerable<string> actual, IEnumerable<string> claims, string prefix) =>
            SetEqual(actual, claims.Select(id => $"{prefix}:{id}"));
        bool SceneAuthority(NarrationPlanningScene s)
        {
            if (!HasPacket(s)) return false;
            var p = packets[s.PacketId];
            return s.SceneId == p.SourceSceneId && s.Variant == p.Variant && s.StoryFrameId == p.StoryFrameId &&
                s.StoryFrameChecksum == p.StoryFrameChecksum && s.SourceSceneChecksum == p.SourceSceneChecksum &&
                s.PacketId == p.PacketId && s.PacketChecksum == p.DeterministicChecksum &&
                s.ViewerQuestion == p.ResolvedViewerQuestionText && s.LearningObjective == p.SceneObjective &&
                NarrationPlanningReferenceGovernance.HasValidatedVariantOwnership(input, p) &&
                s.PrimaryKnowledgeReferences.SequenceEqual(NarrationPlanningReferenceGovernance.GovernedPrimaryReferences(input, p), StringComparer.Ordinal) &&
                s.SupportingKnowledgeReferences.SequenceEqual(NarrationPlanningReferenceGovernance.GovernedSupportingReferences(p), StringComparer.Ordinal) &&
                SetEqual(s.RequiredClaims, p.RequiredClaims.Select(c => c.ClaimId)) &&
                SetEqual(s.OptionalClaims, p.OptionalClaims.Select(c => c.ClaimId)) && SetEqual(s.DeferredClaims, p.DeferredClaims.Select(c => c.ClaimId)) &&
                SetEqual(s.ForbiddenStatements, p.ProhibitedClaims) && SetEqual(s.SafetyRequirements, p.SafetyRules) &&
                SetEqual(s.EditorialConstraints, p.EditorialConstraints) &&
                s.VisualSynchronizationTargets.SequenceEqual(p.VisualEvidenceIds, StringComparer.Ordinal) &&
                s.MinimumDuration == p.MinimumDurationSeconds && s.ExpectedDuration == p.TargetDurationSeconds && s.MaximumDuration == p.MaximumDurationSeconds;
        }
        bool Goal(NarrationPlanningScene s)
        {
            if (!HasPacket(s)) return false; var p = packets[s.PacketId]; var g = s.NarrativeGoal;
            return g.SceneRole == p.SceneRole && g.SectionKey == p.SectionKey && g.ViewerQuestionId == p.ViewerQuestionId &&
                g.LearningObjectiveId == p.LearningObjectiveId && SetEqual(g.RequiredClaimIds, p.RequiredClaims.Select(c => c.ClaimId)) &&
                g.ProfileId == input.ProfileId && g.GoalPolicy == NarrationPlanningPolicyCatalog.GoalPolicy &&
                g.DeterministicChecksum == NarrationPlanningCanonicalizer.GoalChecksum(g);
        }
        bool Strategy(NarrationPlanningScene s)
        {
            if (!HasPacket(s)) return false; var p = packets[s.PacketId]; var x = s.Strategy;
            return x.NarrativeStage == p.NarrativeStage && x.SceneRole == p.SceneRole && x.SectionKey == p.SectionKey &&
                x.OpeningMode == NarrationPlanningPolicyCatalog.OpeningMode && x.DevelopmentMode == NarrationPlanningPolicyCatalog.DevelopmentMode &&
                x.ClosingMode == NarrationPlanningPolicyCatalog.ClosingMode && x.ClaimIntroductionPolicy == NarrationPlanningPolicyCatalog.ClaimIntroductionPolicy &&
                x.OptionalClaimUsagePolicy == NarrationPlanningPolicyCatalog.OptionalClaimUsagePolicy && x.DeferredClaimPolicy == NarrationPlanningPolicyCatalog.DeferredClaimPolicy &&
                x.CallbackPolicy == NarrationPlanningPolicyCatalog.CallbackPolicy && x.DeterministicChecksum == NarrationPlanningCanonicalizer.StrategyChecksum(x);
        }
        bool Constraints(NarrationPlanningScene s)
        {
            if (!HasPacket(s)) return false; var p = packets[s.PacketId];
            try
            {
                var expected = constraintPolicy.Resolve(new(input.Language, s.Variant, p.TargetDurationSeconds, p.MinimumDurationSeconds,
                    p.MaximumDurationSeconds, input.FamilyNarrationProfile, p.SceneRole, p.SectionKey, p.RequiredClaims.Count, p.OptionalClaims.Count));
                return Phase7Determinism.Hash(expected) == Phase7Determinism.Hash(s.NarrationConstraints) && s.MinimumDuration == p.MinimumDurationSeconds &&
                    s.ExpectedDuration == p.TargetDurationSeconds && s.MaximumDuration == p.MaximumDurationSeconds &&
                    s.ExpectedSentenceCount == expected.PreferredSentenceCount && s.EstimatedReadingTime == expected.ReadingTimeTargetSeconds;
            }
            catch (ArgumentException) { return false; }
        }

        bool Transitions(IReadOnlyList<NarrationPlanningScene> list, IReadOnlyList<SceneKnowledgePacket> source, string variant)
        {
            if (list.Count == 0 || list.Count != source.Count || list.Any(s => s.Variant != variant)) return false;
            var orderedPackets = source.OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
            if (!list.Select(s => s.PacketId).SequenceEqual(orderedPackets.Select(p => p.PacketId), StringComparer.Ordinal)) return false;
            for (var i = 0; i < list.Count; i++)
            {
                var scene = list[i]; var current = orderedPackets[i];
                var previous = i == 0 ? null : orderedPackets[i - 1];
                var next = i == orderedPackets.Length - 1 ? null : orderedPackets[i + 1];
                if (!TransitionValid(scene.IncomingTransition, previous, current, next, previous, current,
                        previous is null ? NarrationPlanningPolicyCatalog.VariantOpeningTransition : NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, variant) ||
                    !TransitionValid(scene.OutgoingTransition, previous, current, next, current, next,
                        next is null ? NarrationPlanningPolicyCatalog.VariantClosingTransition : NarrationPlanningPolicyCatalog.StoryFrameSuccessorTransition, variant)) return false;
                if (i + 1 < list.Count)
                {
                    var incoming = list[i + 1].IncomingTransition;
                    var outgoing = scene.OutgoingTransition;
                    if (outgoing.Kind != incoming.Kind || outgoing.FromStoryFrameId != incoming.FromStoryFrameId ||
                        outgoing.FromStoryFrameChecksum != incoming.FromStoryFrameChecksum ||
                        outgoing.ToStoryFrameId != incoming.ToStoryFrameId || outgoing.ToStoryFrameChecksum != incoming.ToStoryFrameChecksum ||
                        outgoing.SourceTransitionOut != incoming.SourceTransitionOut ||
                        outgoing.DestinationTransitionIn != incoming.DestinationTransitionIn) return false;
                }
            }
            return true;
        }
        bool TransitionValid(NarrationPlanningTransition transition, SceneKnowledgePacket? previous,
            SceneKnowledgePacket current, SceneKnowledgePacket? next, SceneKnowledgePacket? from,
            SceneKnowledgePacket? to, string kind, string variant)
        {
            StoryFrameAuthorityFrame? Frame(SceneKnowledgePacket? packet) => packet is null ? null :
                input.PublishedStoryFrameAuthority.Authority.Frames.SingleOrDefault(f => f.FrameId == packet.StoryFrameId);
            var fromFrame = Frame(from); var toFrame = Frame(to);
            return transition.ExecutionId == input.ExecutionId && transition.Variant == variant && transition.Kind == kind &&
                transition.FromStoryFrameId == from?.StoryFrameId && transition.FromStoryFrameChecksum == from?.StoryFrameChecksum &&
                transition.ToStoryFrameId == to?.StoryFrameId && transition.ToStoryFrameChecksum == to?.StoryFrameChecksum &&
                transition.SourceTransitionOut == fromFrame?.TransitionOut && transition.DestinationTransitionIn == toFrame?.TransitionIn &&
                transition.PreviousPacketId == previous?.PacketId && transition.CurrentPacketId == current.PacketId &&
                transition.NextPacketId == next?.PacketId &&
                transition.TransitionId == NarrationPlanningCanonicalizer.ComputeTransitionId(transition with { TransitionId = "", DeterministicChecksum = "" }) &&
                transition.DeterministicChecksum == NarrationPlanningCanonicalizer.TransitionChecksum(transition);
        }

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [Names[0]] = authority.ContractVersion == NarrationPlanningContract.Version,
            [Names[1]] = authority.ExecutionId == input.ExecutionId && authority.PlanId == input.PlanId && authority.EventId == input.EventId && authority.StoryFrameAuthorityChecksum == input.PublishedStoryFrameAuthority.Authority.SemanticChecksum && authority.KnowledgeAuthorityChecksum == input.PublishedPhase7KnowledgeAuthority.KnowledgeAuthority.SemanticChecksum && authority.PacketCollectionChecksum == input.SceneKnowledgePacketCollection.DeterministicChecksum,
            [Names[2]] = authority.ProfileId == input.ProfileId && authority.ProfileVersion == input.ProfileVersion,
            [Names[3]] = authority.Language == input.Language,
            [Names[4]] = allPackets.Length == packets.Count && scenes.Length == packets.Count && packets.Keys.All(id => scenes.Count(s => s.PacketId == id) == 1),
            [Names[5]] = scenes.All(s => s.PlanningId == NarrationPlanningCanonicalizer.PlanningId(s with { PlanningId = "", DeterministicChecksum = "" })),
            [Names[6]] = scenes.All(SceneAuthority), [Names[7]] = scenes.All(s => HasPacket(s) && s.ViewerQuestion == packets[s.PacketId].ResolvedViewerQuestionText),
            [Names[8]] = scenes.All(s => HasPacket(s) && s.LearningObjective == packets[s.PacketId].SceneObjective), [Names[9]] = scenes.All(Goal),
            [Names[10]] = scenes.All(Strategy), [Names[11]] = Transitions(authority.LongScenes, input.SceneKnowledgePacketCollection.Long, "Long") && Transitions(authority.ShortScenes, input.SceneKnowledgePacketCollection.Short, "Short"),
            [Names[12]] = scenes.All(Constraints), [Names[13]] = scenes.All(s => s.ClaimUsagePolicy == new NarrationClaimUsagePolicy(NarrationPlanningPolicyCatalog.RequiredClaimUsage, NarrationPlanningPolicyCatalog.OptionalClaimUsage, NarrationPlanningPolicyCatalog.DeferredClaimUsage)),
            [Names[14]] = Claims(p => p.RequiredClaims, s => s.RequiredClaims), [Names[15]] = Claims(p => p.OptionalClaims, s => s.OptionalClaims), [Names[16]] = Claims(p => p.DeferredClaims, s => s.DeferredClaims),
            [Names[17]] = scenes.All(s => HasPacket(s) && SetEqual(packets[s.PacketId].SafetyRules, s.SafetyRequirements) && SetEqual(packets[s.PacketId].EditorialConstraints, s.EditorialConstraints) && SetEqual(packets[s.PacketId].ProhibitedClaims, s.ForbiddenStatements)),
            [Names[18]] = scenes.All(s => HasPacket(s) && ExactQualifications(s.CulturalQualificationRequirements, packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c => c.IsCultural || c.IsMythological).Select(c => c.ClaimId), NarrationPlanningPolicyCatalog.CulturalQualificationPrefix)),
            [Names[19]] = scenes.All(s => HasPacket(s) && ExactQualifications(s.LocationQualificationRequirements, packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c => c.IsLocationDependent).Select(c => c.ClaimId), NarrationPlanningPolicyCatalog.LocationQualificationPrefix) && ExactQualifications(s.TimeQualificationRequirements, packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c => c.IsDateTimeDependent).Select(c => c.ClaimId), NarrationPlanningPolicyCatalog.TimeQualificationPrefix)),
            [Names[20]] = scenes.All(s => HasPacket(s) && ExactQualifications(s.AstrologyQualificationRequirements, packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Where(c => c.IsAstrologyRelated).Select(c => c.ClaimId), NarrationPlanningPolicyCatalog.AstrologyQualificationPrefix)),
            [Names[21]] = scenes.All(s => HasPacket(s) && !packets[s.PacketId].RequiredClaims.Concat(packets[s.PacketId].OptionalClaims).Any(c => c.RequiresHumanReview) && ExactQualifications(s.HumanReviewRequirements, packets[s.PacketId].DeferredClaims.Where(c => c.RequiresHumanReview).Select(c => c.ClaimId), NarrationPlanningPolicyCatalog.HumanReviewPrefix)),
            [Names[22]] = authority.LongScenes.All(s => input.SceneKnowledgePacketCollection.Long.Any(p => p.PacketId == s.PacketId) && HasPacket(s) && packets[s.PacketId].Variant == "Long" && NarrationPlanningReferenceGovernance.HasValidatedVariantOwnership(input, packets[s.PacketId])) && authority.ShortScenes.All(s => input.SceneKnowledgePacketCollection.Short.Any(p => p.PacketId == s.PacketId) && HasPacket(s) && packets[s.PacketId].Variant == "Short" && NarrationPlanningReferenceGovernance.HasValidatedVariantOwnership(input, packets[s.PacketId])) && !input.SceneKnowledgePacketCollection.Long.Select(s => s.PacketId).Intersect(input.SceneKnowledgePacketCollection.Short.Select(s => s.PacketId), StringComparer.Ordinal).Any() && !authority.LongScenes.Select(s => s.PacketId).Intersect(authority.ShortScenes.Select(s => s.PacketId), StringComparer.Ordinal).Any() && !authority.LongScenes.Select(s => s.PlanningId).Intersect(authority.ShortScenes.Select(s => s.PlanningId), StringComparer.Ordinal).Any(),
            [Names[23]] = scenes.All(s => realizabilityPolicy.Evaluate(new(s.Variant, s.NarrativeGoal.SectionKey, s.RequiredClaims, s.IncomingTransition, s.OutgoingTransition, s.NarrationConstraints, s.LocationQualificationRequirements, s.TimeQualificationRequirements, s.CulturalQualificationRequirements, s.AstrologyQualificationRequirements)).IsRealizable), [Names[24]] = DiagnosticsValid(input, authority.Diagnostics, scenes, packets.Values), [Names[25]] = authority.AuthorityId == NarrationPlanningCanonicalizer.AuthorityId(authority with { AuthorityId = "", DeterministicChecksum = "" }) && authority.DeterministicChecksum == NarrationPlanningCanonicalizer.AuthorityChecksum(authority),
            [Names[26]] = scenes.All(s => s.DeterministicChecksum == NarrationPlanningCanonicalizer.SceneChecksum(s))
        };
        var gates = Names.Select(n => new NarrationPlanningValidationGate(n, checks[n], checks[n] ? [] : [$"{n} failed."])).ToArray();
        var errors = gates.SelectMany(g => g.Errors).ToArray();
        var draftRealizabilityFailed = !checks["DraftRealizabilityGate"];
        if (draftRealizabilityFailed)
        {
            var details = scenes.Select(s => (Scene: s, Budget: realizabilityPolicy.Evaluate(new(s.Variant, s.NarrativeGoal.SectionKey, s.RequiredClaims, s.IncomingTransition, s.OutgoingTransition, s.NarrationConstraints, s.LocationQualificationRequirements, s.TimeQualificationRequirements, s.CulturalQualificationRequirements, s.AstrologyQualificationRequirements))))
                .Where(x => !x.Budget.IsRealizable)
                .Select(x => $"NARRATION_PLANNING_SCENE_REQUIRED_CONTENT_EXCEEDS_CAPACITY: planningId={x.Scene.PlanningId};variant={x.Scene.Variant};sectionKey={x.Scene.NarrativeGoal.SectionKey};requiredClaims={x.Budget.RequiredClaimSentenceCount};incomingTransitions={x.Budget.MandatoryIncomingTransitionSentenceCount};outgoingTransitions={x.Budget.MandatoryOutgoingTransitionSentenceCount};mandatoryQualifications={x.Budget.MandatoryQualificationSentenceCount};minimumMandatorySentences={x.Budget.MinimumMandatorySentenceCount};maximumSentences={x.Budget.MaximumSentenceCount}.")
                .ToArray();
            errors = errors.Concat(details).ToArray();
        }
        var draft = new NarrationPlanningValidation(errors.Length == 0, errors.Length == 0 ? "NARRATION_PLANNING_VALID" : draftRealizabilityFailed ? "NARRATION_PLANNING_DRAFT_CAPACITY_INVALID" : "NARRATION_PLANNING_INVALID", gates, errors, "");
        return draft with { DeterministicChecksum = NarrationPlanningCanonicalizer.ValidationChecksum(draft) };
    }

    private static bool DiagnosticsValid(Phase7NarrationPlanningInputAuthority input, NarrationPlanningDiagnostics d, IReadOnlyList<NarrationPlanningScene> scenes,
        IEnumerable<SceneKnowledgePacket> packetSource)
    {
        var packets = packetSource.ToArray(); var references = packets.SelectMany(p => p.ReferenceResolutions).ToArray();
        return d.PacketCount == packets.Length && d.PlanningSceneCount == scenes.Count &&
            d.LongPlanningSceneCount == scenes.Count(s => s.Variant == "Long") && d.ShortPlanningSceneCount == scenes.Count(s => s.Variant == "Short") &&
            d.PrimaryReferenceCount == packets.Sum(p => p.ReferenceResolutions.Count(r => NarrationPlanningReferenceGovernance.IsGovernedResolvedPrimary(input, p, r))) && d.SupportingReferenceCount == scenes.Sum(s => s.SupportingKnowledgeReferences.Count) &&
            d.RequiredReferenceCount == references.Count(x => x.IsRequired) && d.ResolvedReferenceCount == references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Resolved) &&
            d.DeferredReferenceCount == references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Deferred) &&
            d.MissingReferenceCount == references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Missing) && d.AmbiguousReferenceCount == references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Ambiguous) &&
            d.CrossVariantReferenceCount == references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.CrossVariantInvalid) && d.UnsupportedReferenceCount == references.Count(x => x.Status == Phase7KnowledgeReferenceStatus.Unsupported) &&
            d.UnresolvedReferenceCount == d.MissingReferenceCount + d.AmbiguousReferenceCount + d.CrossVariantReferenceCount + d.UnsupportedReferenceCount && d.TransitionCount == scenes.Count * 2 &&
            d.BlockingIssueCount == packets.Sum(p => p.BlockingIssues.Count) && d.FailedGateCount == 0 &&
            d.RequiredClaimCount == scenes.Sum(s => s.RequiredClaims.Count) && d.OptionalClaimCount == scenes.Sum(s => s.OptionalClaims.Count) &&
            d.DeferredClaimCount == scenes.Sum(s => s.DeferredClaims.Count) && d.WarningCount == d.Warnings.Count && d.ErrorCount == d.Errors.Count && d.RealizabilityDiagnostics.Count == scenes.Count &&
            d.RealizabilityDiagnostics.All(x => x.IsDraftRealizable && x.MinimumMandatorySentenceCount <= x.MaximumSentenceCount) &&
            d.DeterministicChecksum == NarrationPlanningCanonicalizer.DiagnosticsChecksum(d);
    }
}
