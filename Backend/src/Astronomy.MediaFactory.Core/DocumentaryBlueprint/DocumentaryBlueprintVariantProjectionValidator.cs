namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed record DocumentaryBlueprintVariantProjectionValidationResult(
    IReadOnlyList<DocumentaryBlueprintProjectionDiagnostic> Errors, bool ScenePassed, bool QuestionPassed,
    bool ObjectivePassed, bool KnowledgePassed, bool SafetyPassed, bool DurationPassed, bool TransitionPassed)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Reconciles the builder output rather than trusting it. Field authority table:
/// SceneId/order = deterministic projector identity/opportunity order; title/question = primary question;
/// stage/role = profile slot; objective summary/learning goal = certified learning objective, while
/// curiosity/emotional goals = profile slot authorities; outcome/priority = profile outcome and priority;
/// knowledge = certified selections; visual fields = profile visual authority; all three transition
/// fields = their distinct profile authorities; duration = planner allocation. Every rule is exact equality.
/// </summary>
public sealed class DocumentaryBlueprintVariantProjectionValidator
{
    public DocumentaryBlueprintVariantProjectionValidationResult Validate(DocumentaryVariantIntent source,
        IReadOnlyList<DocumentarySceneBlueprintInput> expected, DocumentaryBlueprint actual,
        IReadOnlyList<DocumentarySceneBlueprintTraceability> traceability)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual); ArgumentNullException.ThrowIfNull(traceability);
        var errors = new List<DocumentaryBlueprintProjectionDiagnostic>();
        var sceneOk = true; var questionOk = true; var objectiveOk = true; var knowledgeOk = true;
        var safetyOk = true; var durationOk = true; var transitionOk = true;
        void Fail(string code, string message) => errors.Add(new(code, $"{source.Variant}: {message}"));
        if (source.SceneOpportunities.Count != expected.Count || actual.Scenes.Count != expected.Count || traceability.Count != expected.Count)
        { sceneOk = false; Fail("P4P_SCENE_RECONCILIATION_FAILED", "A scene is missing or additional."); }
        var count = Math.Min(Math.Min(source.SceneOpportunities.Count, expected.Count), Math.Min(actual.Scenes.Count, traceability.Count));
        for (var i = 0; i < count; i++)
        {
            var opportunity = source.SceneOpportunities[i]; var input = expected[i]; var output = actual.Scenes[i]; var trace = traceability[i];
            if (opportunity.Order != i + 1 || input.SceneNumber != opportunity.Order || output.SceneNumber != input.SceneNumber ||
                output.SceneId != input.SceneId || trace.SceneId != output.SceneId || output.NarrativeStage != input.NarrativeStage ||
                output.SceneRole != input.SceneRole || output.Title != input.Title)
            { sceneOk = false; Fail("P4P_SCENE_RECONCILIATION_FAILED", $"Scene at position {i + 1} was reordered or substituted."); }
            if (output.ViewerQuestion != input.ViewerQuestion || trace.PrimaryViewerQuestionId != opportunity.PrimaryViewerQuestionId ||
                !trace.SupportingViewerQuestionIds.SequenceEqual(opportunity.SupportingViewerQuestionIds))
            { questionOk = false; Fail("P4P_PRIMARY_QUESTION_INVALID", $"Question drift in '{opportunity.OpportunityId}'."); }
            if (output.SceneObjective != input.SceneObjective || trace.LearningObjectiveId != opportunity.LearningObjectiveId)
            { objectiveOk = false; Fail("P4P_OBJECTIVE_INVALID", $"Objective drift in '{opportunity.OpportunityId}'."); }
            if (output.KnowledgeReferences.Count != input.KnowledgeReferences.Count || !output.KnowledgeReferences.SequenceEqual(input.KnowledgeReferences) ||
                !trace.KnowledgeSelections.SequenceEqual(opportunity.SelectedKnowledgeReferences))
            { knowledgeOk = false; Fail("P4P_KNOWLEDGE_RECONCILIATION_FAILED", $"Knowledge drift in '{opportunity.OpportunityId}'."); }
            if (output.EditorialOutcome != input.EditorialOutcome || output.EditorialPriority != input.EditorialPriority ||
                trace.QuestionEvidenceStatus != opportunity.QuestionEvidenceStatus || !trace.EditorialConstraints.SequenceEqual(opportunity.EditorialConstraints) ||
                !trace.MustNotClaim.SequenceEqual(opportunity.MustNotClaim))
            { safetyOk = false; Fail("P4P_EDITORIAL_SAFETY_FAILED", $"Editorial safety drift in '{opportunity.OpportunityId}'."); }
            if (output.EstimatedDurationSeconds != opportunity.TargetDurationSeconds || trace.MinimumDurationSeconds != opportunity.MinimumDurationSeconds ||
                trace.MaximumDurationSeconds != opportunity.MaximumDurationSeconds)
            { durationOk = false; Fail("P4P_DURATION_RECONCILIATION_FAILED", $"Duration drift in '{opportunity.OpportunityId}'."); }
            if (output.Transition != input.Transition || !output.VisualOpportunities.SequenceEqual(input.VisualOpportunities))
            { transitionOk = false; Fail("P4P_TRANSITION_RECONCILIATION_FAILED", $"Transition or visual drift in '{opportunity.OpportunityId}'."); }
            if (trace.SourceOpportunityId != opportunity.OpportunityId || trace.SourceOpportunityChecksum != opportunity.DeterministicChecksum ||
                trace.ProfileSlotId != opportunity.ProfileSlotId)
            { sceneOk = false; Fail("P4P_SCENE_RECONCILIATION_FAILED", $"Traceability drift in '{opportunity.OpportunityId}'."); }
        }
        return new(errors, sceneOk, questionOk, objectiveOk, knowledgeOk, safetyOk, durationOk, transitionOk);
    }
}
