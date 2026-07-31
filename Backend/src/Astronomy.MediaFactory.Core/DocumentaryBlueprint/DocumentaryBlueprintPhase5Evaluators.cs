namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryBlueprintCoverageEvaluator
{
    public BlueprintVariantCoverageResult Evaluate(DocumentaryBlueprintVariantArtifact variant)
    {
        var scenes = variant.Blueprint.Scenes;
        var traces = variant.SceneTraceability.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var orphan = scenes.Any(s => !traces.ContainsKey(s.SceneId)) || traces.Keys.Any(id => scenes.All(s => s.SceneId != id));
        var unresolved = variant.SceneTraceability.Any(t => string.IsNullOrWhiteSpace(t.PrimaryViewerQuestionId) ||
            string.IsNullOrWhiteSpace(t.LearningObjectiveId) || t.KnowledgeSelections.Any(k => string.IsNullOrWhiteSpace(k.KnowledgeReferenceId)));
        var duplicate = scenes.Any(s => s.KnowledgeReferences.GroupBy(k => k.KnowledgeEntryId, StringComparer.Ordinal).Any(g => g.Count() > 1));
        var deferrals = variant.DeferredQuestions.All(d => !string.IsNullOrWhiteSpace(d.QuestionId) && !string.IsNullOrWhiteSpace(d.ReasonCode));
        var questions = variant.QuestionCoverage.CoveredQuestions.All(q => variant.SceneTraceability.Any(t => t.PrimaryViewerQuestionId == q || t.SupportingViewerQuestionIds.Contains(q)));
        var objectives = variant.SceneTraceability.All(t => !string.IsNullOrWhiteSpace(t.LearningObjectiveId));
        var knowledge = scenes.All(s => s.KnowledgeReferences.Count > 0);
        var stages = scenes.Count > 0 && scenes.Select(s => s.NarrativeStage).Distinct().Count() > 1;
        var issues = new List<string>();
        if (orphan) issues.Add("P5COVERAGE_ORPHAN_REFERENCE"); if (unresolved) issues.Add("P5COVERAGE_UNRESOLVED_ID");
        if (duplicate) issues.Add("P5COVERAGE_INVALID_KNOWLEDGE_DUPLICATION"); if (!deferrals) issues.Add("P5COVERAGE_DEFERRAL_REASON_REQUIRED");
        if (!questions) issues.Add("P5COVERAGE_QUESTION_MISSING"); if (!objectives) issues.Add("P5COVERAGE_OBJECTIVE_MISSING");
        if (!knowledge) issues.Add("P5COVERAGE_KNOWLEDGE_MISSING"); if (!stages) issues.Add("P5COVERAGE_NARRATIVE_STAGES_MISSING");
        return new(variant.Variant, questions, objectives, knowledge, stages, unresolved, orphan, deferrals, !duplicate, issues.Count == 0, issues);
    }
}

public sealed class DocumentaryBlueprintTransitionEvaluator
{
    public BlueprintVariantTransitionResult Evaluate(DocumentaryBlueprintVariantArtifact variant)
    {
        var s = variant.Blueprint.Scenes.OrderBy(x => x.SceneNumber).ToArray();
        var opening = s.Length > 0 && s[0].SceneRole is DocumentarySceneRole.OpeningHook or DocumentarySceneRole.Orientation;
        var closing = s.Length > 0 && s[^1].SceneRole is DocumentarySceneRole.ReflectiveClosing or DocumentarySceneRole.PracticalObservation;
        var contiguous = s.Select(x => x.SceneNumber).SequenceEqual(Enumerable.Range(1, s.Length));
        var progression = s.Zip(s.Skip(1)).All(p => (int)p.Second.NarrativeStage >= (int)p.First.NarrativeStage ||
            p.Second.NarrativeStage is DocumentaryNarrativeStage.Clarification or DocumentaryNarrativeStage.Observation);
        var consistent = s.All(x => !string.IsNullOrWhiteSpace(x.Transition.TransitionIntent) && !string.IsNullOrWhiteSpace(x.Transition.EditorialDirection));
        var handoffs = s.Take(Math.Max(0, s.Length - 1)).All(x => !string.IsNullOrWhiteSpace(x.Transition.NextQuestionSeed));
        var abrupt = s.Zip(s.Skip(1)).Any(p => Math.Abs((int)p.Second.NarrativeStage - (int)p.First.NarrativeStage) > 3 && string.IsNullOrWhiteSpace(p.First.Transition.NextQuestionSeed));
        var issues = new List<string>(); if (!opening) issues.Add("P5TRANSITION_INVALID_OPENING"); if (!closing) issues.Add("P5TRANSITION_INVALID_CLOSING");
        if (!contiguous) issues.Add("P5TRANSITION_NONCONTIGUOUS"); if (!progression) issues.Add("P5TRANSITION_INVALID_STAGE_PROGRESSION");
        if (abrupt) issues.Add("P5TRANSITION_ABRUPT_UNSUPPORTED_JUMP"); if (!consistent) issues.Add("P5TRANSITION_IN_OUT_INCONSISTENT"); if (!handoffs) issues.Add("P5TRANSITION_MISSING_HANDOFF");
        return new(variant.Variant, opening, closing, contiguous, progression, !abrupt, consistent, handoffs, issues.Count == 0, issues);
    }
}

public sealed class DocumentaryBlueprintPauseTestEvaluator
{
    public IReadOnlyList<BlueprintPauseTestSceneResult> Evaluate(DocumentaryBlueprintVariantArtifact variant, BlueprintVariantTransitionResult transition)
    {
        var traces = variant.SceneTraceability.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        return variant.Blueprint.Scenes.OrderBy(x => x.SceneNumber).Select((s, i) =>
        {
            var trace = traces.GetValueOrDefault(s.SceneId); var last = i == variant.Blueprint.Scenes.Count - 1;
            var rules = new Dictionary<string, bool> {
                ["P5PAUSE_COMPLETE_THOUGHT"] = !string.IsNullOrWhiteSpace(s.ViewerQuestion.Text) && !string.IsNullOrWhiteSpace(s.EditorialOutcome.NarrativeContribution),
                ["P5PAUSE_AUDIENCE_TAKEAWAY"] = !string.IsNullOrWhiteSpace(s.EditorialOutcome.ViewerTakeaway),
                ["P5PAUSE_CONTEXT_COMPLETE"] = trace is not null && !string.IsNullOrWhiteSpace(trace.LearningObjectiveId),
                ["P5PAUSE_NO_DANGLING_REFERENCE"] = last || !string.IsNullOrWhiteSpace(s.Transition.NextQuestionSeed),
                ["P5PAUSE_KNOWLEDGE_SUPPORTED"] = trace is not null && s.KnowledgeReferences.Count > 0 && s.KnowledgeReferences.All(k => trace.KnowledgeSelections.Any(x => x.KnowledgeReferenceId == k.KnowledgeEntryId)),
                ["P5PAUSE_VALID_HANDOFF"] = last || transition.IntermediateHandoffsValid,
                ["P5PAUSE_DURATION_VALID"] = s.EstimatedDurationSeconds > 0 && (trace is null || s.EstimatedDurationSeconds >= trace.MinimumDurationSeconds && s.EstimatedDurationSeconds <= trace.MaximumDurationSeconds)
            };
            return new BlueprintPauseTestSceneResult(variant.Variant, s.SceneId, s.SceneNumber, rules.Values.All(x => x), rules, rules.Where(x => !x.Value).Select(x => x.Key).ToArray());
        }).ToArray();
    }
}
