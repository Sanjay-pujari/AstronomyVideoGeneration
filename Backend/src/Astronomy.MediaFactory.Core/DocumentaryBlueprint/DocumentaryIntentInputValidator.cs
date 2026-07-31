namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Validates all externally supplied authority projections before indexes or allocation are built.</summary>
public static class DocumentaryIntentInputValidator
{
    public static IReadOnlyList<DocumentaryPlanningIssue> Validate(DocumentaryIntentPlanningRequest r)
    {
        var issues = new List<DocumentaryPlanningIssue>();
        void Add(string code, string message) => issues.Add(new(code, message));
        if (string.IsNullOrWhiteSpace(r.ExecutionId) || string.IsNullOrWhiteSpace(r.PlanId) || string.IsNullOrWhiteSpace(r.EventId) || string.IsNullOrWhiteSpace(r.Language))
            Add("DI_UPSTREAM_IDENTITY_MISMATCH", "Execution, plan, event, and language identities must be non-empty.");
        if (r.SourceLineage is null || r.QuestionBank is null || r.LearningObjectives is null || r.QuestionPlan is null || r.Profile is null || r.CertifiedKnowledge is null)
        { Add("DI_INPUT_INVALID", "All authority inputs and profile collections are required."); return issues; }
        if (r.ExecutionId != r.SourceLineage.Phase1ExecutionId || r.PlanId != r.SourceLineage.Phase1PlanId ||
            r.QuestionBank.Metadata.ExecutionId != r.ExecutionId || r.LearningObjectives.Metadata.ExecutionId != r.ExecutionId || r.QuestionPlan.Metadata.ExecutionId != r.ExecutionId)
            Add("DI_UPSTREAM_IDENTITY_MISMATCH", "Phase 1, 2, and 3 lineage identities do not agree.");
        if (new[] { r.SourceLineage.Phase2SemanticChecksum, r.SourceLineage.CertifiedKnowledgeContextChecksum,
                r.SourceLineage.Phase3QuestionBankChecksum, r.SourceLineage.Phase3LearningObjectivesChecksum, r.SourceLineage.Phase3QuestionPlanChecksum,
                r.QuestionBank.Metadata.Checksum, r.LearningObjectives.Metadata.Checksum, r.QuestionPlan.Metadata.Checksum }.Any(string.IsNullOrWhiteSpace))
            Add("DI_LINEAGE_CHECKSUM_INVALID", "Phase 2 and Phase 3 semantic checksums are required.");
        if (new[] { r.SourceLineage.Language, r.QuestionBank.Metadata.Language, r.LearningObjectives.Metadata.Language, r.QuestionPlan.Metadata.Language }
            .Any(x => !string.Equals(x, r.Language, StringComparison.OrdinalIgnoreCase)) || r.QuestionBank.Questions.Any(x => !string.Equals(x.Language, r.Language, StringComparison.OrdinalIgnoreCase)))
            Add("DI_LANGUAGE_MISMATCH", "All upstream artifacts must use one language.");
        if (r.QuestionBank.Questions is null || r.LearningObjectives.Objectives is null) { Add("DI_INPUT_INVALID", "Phase 3 collections cannot be null."); return issues; }
        var questionIds = r.QuestionBank.Questions.Select(x => x?.QuestionId).ToArray();
        if (questionIds.Any(string.IsNullOrWhiteSpace) || questionIds.Distinct(StringComparer.Ordinal).Count() != questionIds.Length)
            Add("DI_QUESTION_BANK_INVALID", "Question IDs must be non-empty and unique.");
        var objectiveIds = r.LearningObjectives.Objectives.Select(x => x?.ObjectiveId).ToArray();
        if (objectiveIds.Any(string.IsNullOrWhiteSpace) || objectiveIds.Distinct(StringComparer.Ordinal).Count() != objectiveIds.Length)
            Add("DI_OBJECTIVE_REFERENCE_INVALID", "Objective IDs must be non-empty and unique.");
        var knownQuestions = questionIds.Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
        if (r.LearningObjectives.Objectives.Any(o => o.ViewerQuestionIds is null || o.ViewerQuestionIds.Any(q => !knownQuestions.Contains(q))))
            Add("DI_OBJECTIVE_REFERENCE_INVALID", "Every objective question reference must exist in the question bank.");
        var objectiveQuestions = r.LearningObjectives.Objectives.SelectMany(x => x.ViewerQuestionIds ?? []).ToHashSet(StringComparer.Ordinal);
        if (r.QuestionBank.Questions.Where(x => x.Priority == "High").Any(x => !objectiveQuestions.Contains(x.QuestionId)))
            Add("DI_OBJECTIVE_REFERENCE_INVALID", "Every required High question must have an objective.");
        if (r.QuestionPlan.TotalGeneratedQuestions < r.QuestionBank.Questions.Count || r.QuestionPlan.AcceptedQuestions != r.QuestionBank.Questions.Count ||
            r.QuestionPlan.QuestionsRequiringEditorialAttention.Any(x => !knownQuestions.Contains(x)))
            Add("DI_QUESTION_PLAN_INVALID", "Question-plan counts and IDs must reconcile with the question bank.");
        var knowledgeIds = r.CertifiedKnowledge.Select(x => x?.ReferenceId).ToArray();
        if (knowledgeIds.Any(string.IsNullOrWhiteSpace) || knowledgeIds.Distinct(StringComparer.Ordinal).Count() != knowledgeIds.Length)
            Add("DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID", "Certified knowledge IDs must be non-empty and unique.");
        if (r.CertifiedKnowledge.Any(x => x is null || string.IsNullOrWhiteSpace(x.SourceArtifact) || x.SourceArtifact.Contains("compatibility/", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(x.SourcePointer) || string.IsNullOrWhiteSpace(x.SemanticChecksum)))
            Add("DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID", "Certified authorities require an allowed artifact, source pointer, and semantic checksum.");
        var certifiedById = r.CertifiedKnowledge.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.ReferenceId))
            .GroupBy(x => x.ReferenceId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var reference in r.QuestionBank.Questions.SelectMany(q => q.KnowledgeReferences ?? [])
                     .Where(x => x.ResolutionStatus.Equals("Resolved", StringComparison.OrdinalIgnoreCase)))
            if (!certifiedById.TryGetValue(reference.ReferenceId, out var authority) ||
                !string.Equals(reference.SourceArtifact, authority.SourceArtifact, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(authority.SourcePointer) || string.IsNullOrWhiteSpace(authority.SemanticChecksum))
                Add("DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID", $"Claimed resolved reference '{reference.ReferenceId}' does not reconcile with certified authority.");
        if (string.IsNullOrWhiteSpace(r.Profile.ProfileId) || string.IsNullOrWhiteSpace(r.Profile.ProfileVersion) || r.Profile.ProfileId != r.SourceLineage.ProfileId || r.Profile.ProfileVersion != r.SourceLineage.ProfileVersion)
            Add("DI_PROFILE_INVALID", "Profile identity must be non-empty and agree with lineage.");
        ValidateProfile(r.Profile.LongProfile, "Long"); ValidateProfile(r.Profile.ShortProfile, "Short");
        return issues;

        void ValidateProfile(DocumentaryVariantProfile p, string expected)
        {
            if (p is null || !string.Equals(p.Variant, expected, StringComparison.Ordinal) || p.NarrativeSlots is null || p.RequiredQuestionCoverage is null || p.RequiredKnowledgeCoverage is null || p.AuthorizedHighQuestionDeferrals is null)
            { Add("DI_PROFILE_INVALID", $"{expected} profile and all collections are required."); return; }
            var slots = p.NarrativeSlots.OrderBy(x => x.Order).ToArray();
            if (slots.Any(x => x is null || string.IsNullOrWhiteSpace(x.SlotId)) || slots.Select(x => x.SlotId).Distinct(StringComparer.Ordinal).Count() != slots.Length)
                Add("DI_PROFILE_INVALID", $"{expected} slot IDs must be non-empty and unique.");
            if (!slots.Select(x => x.Order).SequenceEqual(Enumerable.Range(1, slots.Length))) Add("DI_PROFILE_INVALID", $"{expected} slot order must be contiguous from 1.");
            var terminal = slots.Where(x => x.ClosingBehavior.Equals("Terminal", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (terminal.Length != 1 || slots.Length == 0 || terminal[0].Order != slots[^1].Order) Add("DI_PROFILE_INVALID", $"{expected} requires exactly one terminal closing slot, and it must be last.");
            if (p.ExpectedSceneCount != slots.Length || p.MinimumSceneCount < 1 || p.MinimumSceneCount > p.ExpectedSceneCount || p.ExpectedSceneCount > p.MaximumSceneCount ||
                p.MinimumSceneDurationSeconds <= 0 || p.MaximumSceneDurationSeconds < p.MinimumSceneDurationSeconds || p.DurationBudgetSeconds <= 0 ||
                p.DurationBudgetSeconds < (long)slots.Length * p.MinimumSceneDurationSeconds || p.DurationBudgetSeconds > (long)slots.Length * p.MaximumSceneDurationSeconds || slots.Any(x => x.DurationWeight <= 0 || x.AllowedQuestionCategories is null || x.PreferredQuestionCategories is null || x.PreferredKnowledgeCategories is null || string.IsNullOrWhiteSpace(x.VisualOpportunityIntent) || string.IsNullOrWhiteSpace(x.EditorialOutcome)))
                Add("DI_PROFILE_INVALID", $"{expected} scene counts, durations, weights, and slot-owned values must be valid.");
            if (p.RequiredQuestionCoverage.Any(x => !knownQuestions.Contains(x)) || p.AuthorizedHighQuestionDeferrals.Keys.Any(x => !knownQuestions.Contains(x)))
                Add("DI_PROFILE_INVALID", $"{expected} coverage policy references an unknown question.");
        }
    }
}
