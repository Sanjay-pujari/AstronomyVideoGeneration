using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7NarrationPlanningInputAuthorityEvaluator(
    IPhase7ScenePacketInputAuthorityEvaluator committedEvaluator)
    : IPhase7NarrationPlanningInputAuthorityEvaluator
{
    public async Task<Phase7NarrationPlanningInputAuthorityEvaluation> EvaluateAsync(
        Phase7NarrationPlanningInputAuthorityRequest request, CancellationToken token = default)
    {
        var evaluated = await committedEvaluator.EvaluateAsync(new(request.ExecutionRoot, request.ExecutionId,
            request.PlanId, request.EventId, request.Language, request.ProfileId, request.ProfileVersion), token);
        if (!evaluated.IsValid || evaluated.Authority is null)
            return new(false, null, "NARRATION_PLANNING_INPUT_AUTHORITY_INVALID", evaluated.Errors, evaluated.Warnings);
        var source = evaluated.Authority;
        var packets = request.SceneKnowledgePacketCollection;
        var all = packets.Long.Concat(packets.Short).ToArray();
        var checksum = Phase7Determinism.Hash(new { Long = packets.Long, Short = packets.Short });
        if (packets.DeterministicChecksum != checksum || all.Length == 0 ||
            packets.Long.Any(x => x.Variant != "Long") || packets.Short.Any(x => x.Variant != "Short") ||
            all.Any(x => x.ExecutionId != request.ExecutionId || x.PlanId != request.PlanId || x.EventId != request.EventId))
            return new(false, null, "NARRATION_PLANNING_PACKET_COLLECTION_INVALID",
                ["The packet collection checksum, variant purity, or execution identity is invalid."], evaluated.Warnings);
        var authority = new Phase7NarrationPlanningInputAuthority(source.StoryFrames, source.Knowledge, packets,
            source.FamilyProfile, source.ExecutionId, source.PlanId, source.EventId, source.Language,
            source.ProfileId, source.ProfileVersion, source.LineageEvidence, source.RuntimeCompatibilityEvidence);
        return new(true, authority, "NARRATION_PLANNING_INPUT_AUTHORITY_VALID", [], evaluated.Warnings);
    }
}

public sealed class NarrationPlanningAuthorityBuilder : INarrationPlanningAuthorityBuilder
{
    public NarrationPlanningAuthority Build(Phase7NarrationPlanningInputAuthority input)
    {
        var longs = BuildVariant(input, input.SceneKnowledgePacketCollection.Long, "Long");
        var shorts = BuildVariant(input, input.SceneKnowledgePacketCollection.Short, "Short");
        var diagnosticsDraft = new NarrationPlanningDiagnostics(longs.Count + shorts.Count,
            longs.Count + shorts.Count, longs.Concat(shorts).Sum(x => x.RequiredClaims.Count), [], [], "");
        var diagnostics = diagnosticsDraft with { DeterministicChecksum = Phase7Determinism.Hash(diagnosticsDraft) };
        var draft = new NarrationPlanningAuthority(NarrationPlanningContract.Version,
            $"narration-planning-{input.ExecutionId}", input.ExecutionId, input.PlanId, input.EventId,
            input.Language, input.ProfileId, input.ProfileVersion, longs, shorts, diagnostics,
            input.Phase4To7Lineage, input.RuntimeCompatibilityEvidence, "");
        return draft with { DeterministicChecksum = Phase7Determinism.Hash(draft) };
    }

    private static IReadOnlyList<NarrationPlanningScene> BuildVariant(Phase7NarrationPlanningInputAuthority input,
        IReadOnlyList<SceneKnowledgePacket> packets, string variant)
    {
        var ordered = packets.OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
        return ordered.Select((packet, index) => BuildScene(input, packet, variant, index,
            index == 0 ? null : ordered[index - 1], index + 1 == ordered.Length ? null : ordered[index + 1])).ToArray();
    }

    private static NarrationPlanningScene BuildScene(Phase7NarrationPlanningInputAuthority input,
        SceneKnowledgePacket packet, string variant, int index, SceneKnowledgePacket? previous, SceneKnowledgePacket? next)
    {
        var duration = packet.TargetDurationSeconds;
        var maxSentences = Math.Max(1, (int)Math.Ceiling(duration / 8d));
        var minSentences = Math.Max(1, (int)Math.Floor(duration / 14d));
        var expected = Math.Clamp((int)Math.Round(duration / 10d), minSentences, maxSentences);
        var constraints = new NarrationPlanningConstraints(maxSentences, minSentences, duration,
            "SemanticBeatBoundaries", ["ViewerQuestion", "RequiredClaims", "VisualTargets"],
            "RequiredClaimsInPacketOrderThenOptional", "SynchronizeOnlyToPacketVisualEvidence");
        var incoming = Transition(input, variant, previous, packet, index == 0 ? "VariantOpening" : "StoryFrameSuccessor");
        var outgoing = Transition(input, variant, packet, next, next is null ? "VariantClosing" : "StoryFrameSuccessor");
        var primary = packet.ReferenceResolutions.Where(x => x.IsPrimary).Select(x => x.ReferenceId).ToArray();
        if (primary.Length == 0) primary = packet.KnowledgeReferenceIds.Take(1).ToArray();
        var supporting = packet.KnowledgeReferenceIds.Except(primary, StringComparer.Ordinal).ToArray();
        var cultural = packet.RequiredClaims.Concat(packet.OptionalClaims).Where(x => x.IsCultural || x.IsMythological)
            .Select(x => $"Qualify:{x.ClaimId}").Concat(packet.CulturalContext).Distinct().ToArray();
        var location = packet.LocationDependence ? new[] { "StateLocationScopeForDependentClaims" } : Array.Empty<string>();
        var time = packet.DateTimeDependence ? new[] { "StateTimeScopeForDependentClaims" } : Array.Empty<string>();
        var goal = $"{packet.SceneRole}|{packet.SectionKey}|{packet.ViewerQuestionId}|{packet.LearningObjectiveId}|{string.Join(',', packet.RequiredClaims.Select(x => x.ClaimId))}|{input.ProfileId}";
        var planningId = $"planning-{variant.ToLowerInvariant()}-{packet.PacketId}";
        var draft = new NarrationPlanningScene(planningId, packet.SourceSceneId, variant, packet.StoryFrameId,
            packet.PacketId, packet.DeterministicChecksum, packet.ResolvedViewerQuestionText,
            packet.SceneObjective, goal, primary, supporting, packet.RequiredClaims.Select(x => x.ClaimId).ToArray(),
            packet.OptionalClaims.Select(x => x.ClaimId).ToArray(), packet.DeferredClaims.Select(x => x.ClaimId).ToArray(),
            $"{packet.NarrativeStage}|{packet.SceneRole}|{packet.SectionKey}", constraints, packet.ProhibitedClaims,
            packet.SafetyRules, cultural, location, time, duration, expected, duration,
            packet.VisualEvidenceIds, incoming, outgoing, "");
        return draft with { DeterministicChecksum = SceneChecksum(input.ExecutionId, draft) };
    }

    private static NarrationPlanningTransition Transition(Phase7NarrationPlanningInputAuthority input, string variant,
        SceneKnowledgePacket? from, SceneKnowledgePacket? to, string kind)
    {
        var id = $"transition-{variant.ToLowerInvariant()}-{from?.StoryFrameId ?? "start"}-{to?.StoryFrameId ?? "end"}";
        return new(id, from?.StoryFrameId, to?.StoryFrameId, kind,
            Phase7Determinism.Hash(new { input.ExecutionId, Variant = variant, From = from?.StoryFrameId, To = to?.StoryFrameId, kind }));
    }

    internal static string SceneChecksum(string executionId, NarrationPlanningScene scene) => Phase7Determinism.Hash(new
    {
        Execution = executionId, scene.Variant, scene.StoryFrameId, scene.PacketId, scene.PacketChecksum,
        scene.RequiredClaims, scene.NarrationConstraints, IncomingTransitionId = scene.IncomingTransition.TransitionId,
        OutgoingTransitionId = scene.OutgoingTransition.TransitionId
    });
}

public sealed class NarrationPlanningValidator : INarrationPlanningValidator
{
    private static readonly string[] Names = ["InputAuthorityGate", "PlanningCoverageGate", "ScenePlanningGate",
        "PacketLineageGate", "ViewerQuestionGate", "LearningObjectiveGate", "NarrativeGoalGate", "TransitionGate",
        "ConstraintGate", "RequiredClaimPlanningGate", "SafetyPlanningGate", "CulturalPlanningGate",
        "LocationTimePlanningGate", "DeterminismGate"];

    public NarrationPlanningValidation Validate(Phase7NarrationPlanningInputAuthority input, NarrationPlanningAuthority authority)
    {
        var packets = input.SceneKnowledgePacketCollection.Long.Concat(input.SceneKnowledgePacketCollection.Short).ToArray();
        var scenes = authority.LongScenes.Concat(authority.ShortScenes).ToArray();
        var checks = new Dictionary<string,bool>
        {
            [Names[0]] = authority.ExecutionId == input.ExecutionId && authority.ProfileId == input.ProfileId,
            [Names[1]] = scenes.Length == packets.Length && packets.All(p => scenes.Count(s => s.PacketId == p.PacketId) == 1),
            [Names[2]] = scenes.All(s => !string.IsNullOrWhiteSpace(s.PlanningId) && !string.IsNullOrWhiteSpace(s.SceneId)),
            [Names[3]] = scenes.All(s => packets.Any(p => p.PacketId == s.PacketId && p.DeterministicChecksum == s.PacketChecksum && p.StoryFrameId == s.StoryFrameId)),
            [Names[4]] = scenes.All(s => !string.IsNullOrWhiteSpace(s.ViewerQuestion)),
            [Names[5]] = scenes.All(s => !string.IsNullOrWhiteSpace(s.LearningObjective)),
            [Names[6]] = scenes.All(s => !string.IsNullOrWhiteSpace(s.NarrativeGoal)),
            [Names[7]] = CompleteTransitions(authority.LongScenes) && CompleteTransitions(authority.ShortScenes),
            [Names[8]] = scenes.All(s => s.NarrationConstraints.MinimumSentenceCount > 0 && s.NarrationConstraints.MaximumSentenceCount >= s.NarrationConstraints.MinimumSentenceCount),
            [Names[9]] = scenes.All(s => packets.Single(p => p.PacketId == s.PacketId).RequiredClaims.Select(c => c.ClaimId).SequenceEqual(s.RequiredClaims)),
            [Names[10]] = scenes.All(s => packets.Single(p => p.PacketId == s.PacketId).SafetyRules.All(s.SafetyRequirements.Contains)),
            [Names[11]] = scenes.All(s => !packets.Single(p => p.PacketId == s.PacketId).RequiredClaims.Any(c => c.IsCultural || c.IsMythological) || s.CulturalQualificationRequirements.Count > 0),
            [Names[12]] = scenes.All(s => (!packets.Single(p => p.PacketId == s.PacketId).LocationDependence || s.LocationQualificationRequirements.Count > 0) && (!packets.Single(p => p.PacketId == s.PacketId).DateTimeDependence || s.TimeQualificationRequirements.Count > 0)),
            [Names[13]] = scenes.All(s => s.DeterministicChecksum == NarrationPlanningAuthorityBuilder.SceneChecksum(input.ExecutionId, s)) && authority.DeterministicChecksum == Phase7Determinism.Hash(authority with { DeterministicChecksum = "" })
        };
        var gates = Names.Select(n => new NarrationPlanningValidationGate(n, checks[n], checks[n] ? [] : [$"{n} failed."])).ToArray();
        var errors = gates.SelectMany(x => x.Errors).ToArray();
        var draft = new NarrationPlanningValidation(errors.Length == 0, errors.Length == 0 ? "NARRATION_PLANNING_VALID" : "NARRATION_PLANNING_INVALID", gates, errors, "");
        return draft with { DeterministicChecksum = Phase7Determinism.Hash(draft) };
    }

    private static bool CompleteTransitions(IReadOnlyList<NarrationPlanningScene> scenes) => scenes.Count > 0 &&
        scenes[0].IncomingTransition.FromStoryFrameId is null && scenes[^1].OutgoingTransition.ToStoryFrameId is null &&
        scenes.Zip(scenes.Skip(1)).All(x => x.First.OutgoingTransition.TransitionId == x.Second.IncomingTransition.TransitionId);
}
