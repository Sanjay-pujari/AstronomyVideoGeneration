namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

using CuriosityViewerQuestion = global::Astronomy.MediaFactory.Core.ViewerQuestion;

/// <summary>Pure, filesystem-free Phase 4 intent planner. Variants are allocated independently.</summary>
public sealed class DocumentaryIntentPlanner : IDocumentaryIntentPlanner
{
    public const string Version = "1.1";

    public DocumentaryIntentPlanningResult Plan(DocumentaryIntentPlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = DocumentaryIntentInputValidator.Validate(request);
        if (errors.Count != 0) return Failed(request, errors);

        var questions = request.QuestionBank.Questions.OrderBy(q => Priority(q.Priority)).ThenBy(q => q.Order)
            .ThenBy(q => q.QuestionId, StringComparer.Ordinal).ToArray();
        try
        {
            var longIntent = Allocate(request, request.Profile.LongProfile, questions);
            var shortIntent = Allocate(request, request.Profile.ShortProfile, questions);
            var all = longIntent.SceneOpportunities.Concat(shortIntent.SceneOpportunities).ToArray();
            var aggregate = Coverage(questions, all);
            var constraints = all.SelectMany(x => x.EditorialConstraints).Distinct().OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
            var intent = new DocumentaryIntent("1.1", "1.1", Version,
                DocumentaryIntentChecksum.Id("di-", Version, request.ExecutionId, request.PlanId, request.EventId, request.Profile.ProfileId, request.Profile.ProfileVersion),
                request.ExecutionId, request.PlanId, request.EventId, request.Language, request.Profile.ProfileId, request.Profile.ProfileVersion,
                request.SourceLineage.Phase2SemanticChecksum, request.QuestionBank.Metadata.Checksum, request.SourceLineage,
                request.AudienceIntent, request.DocumentaryGoal,
                request.LearningObjectives.Objectives.OrderBy(x => x.ObjectiveId, StringComparer.Ordinal).Select(x => x.Text).ToArray(),
                constraints.Select(x => x.Code).ToArray(), ["UseCertifiedKnowledgeOnly"], request.Profile.KnowledgeCoveragePolicy,
                request.Profile.QuestionCoveragePolicy, longIntent, shortIntent, aggregate, constraints, "");
            intent = intent with { DeterministicChecksum = DocumentaryIntentChecksum.Calculate(intent) };
            var validation = DocumentaryIntentValidator.Validate(intent, request);
            if (validation.Count != 0) return Failed(request, validation);
            return new(true, intent, [], [], "Resolved:" + request.Profile.ProfileId, aggregate, aggregate, aggregate,
                ["SHA-256 deterministic JSON", "Long and Short allocated independently", "Weighted largest remainder durations"])
            {
                LongQuestionCoverage = longIntent.QuestionCoverage, ShortQuestionCoverage = shortIntent.QuestionCoverage,
                LongKnowledgeCoverage = longIntent.KnowledgeCoverage, ShortKnowledgeCoverage = shortIntent.KnowledgeCoverage,
                AggregateCoverage = aggregate
            };
        }
        catch (PlanningException ex) { return Failed(request, [new(ex.Code, ex.Message)]); }
        catch (KeyNotFoundException ex) { return Failed(request, [new("DI_INPUT_REFERENCE_INVALID", ex.Message)]); }
        catch (ArgumentException ex) { return Failed(request, [new("DI_INPUT_INVALID", ex.Message)]); }
    }

    private static DocumentaryVariantIntent Allocate(DocumentaryIntentPlanningRequest r, DocumentaryVariantProfile p, CuriosityViewerQuestion[] questions)
    {
        var slots = p.NarrativeSlots.OrderBy(x => x.Order).ThenBy(x => x.SlotId, StringComparer.Ordinal).ToArray();
        var durationBySlot = AllocateDurations(p, slots);
        var objectives = r.LearningObjectives.Objectives.SelectMany(o => o.ViewerQuestionIds.Select(q => (q, o)))
            .GroupBy(x => x.q, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(x => x.o.ObjectiveId, StringComparer.Ordinal).First().o, StringComparer.Ordinal);
        var certified = r.CertifiedKnowledge.ToDictionary(x => x.ReferenceId, StringComparer.Ordinal);
        var uses = new Dictionary<string, int>(StringComparer.Ordinal);
        var scenes = new List<DocumentarySceneOpportunity>();

        foreach (var slot in slots)
        {
            var candidates = questions.Where(q => IsSlotEligible(q, p, slot))
                .Where(q => uses.GetValueOrDefault(q.QuestionId) == 0 || slot.CanReusePrimaryQuestion)
                .Select(q => new { Question = q, Evidence = Evidence(q, certified) })
                .Where(x => x.Evidence.Status != QuestionEvidenceStatus.Rejected &&
                    (x.Evidence.Status is not (QuestionEvidenceStatus.EditorialOnly or QuestionEvidenceStatus.Mixed) || slot.CanUseEditorialOnlyQuestion))
                .Where(x => !slot.RequiredKnowledge || x.Evidence.References.Count != 0)
                .OrderByDescending(x => slot.PreferredQuestionCategories.Contains(x.Question.Category, StringComparer.Ordinal))
                .ThenBy(x => uses.GetValueOrDefault(x.Question.QuestionId)).ThenBy(x => Priority(x.Question.Priority))
                .ThenBy(x => x.Question.Order).ThenBy(x => x.Question.QuestionId, StringComparer.Ordinal).ToArray();
            var selected = candidates.FirstOrDefault();
            if (selected is null) throw new PlanningException("DI_SLOT_ALLOCATION_FAILED", $"Slot '{slot.SlotId}' has no question satisfying its variant, category, editorial, reuse, and knowledge contract.");
            var q = selected.Question;
            if (!objectives.TryGetValue(q.QuestionId, out var objective)) throw new PlanningException("DI_OBJECTIVE_REFERENCE_INVALID", $"Question '{q.QuestionId}' has no objective.");
            uses[q.QuestionId] = uses.GetValueOrDefault(q.QuestionId) + 1;
            var opportunityId = DocumentaryIntentChecksum.Id($"dso-{p.Variant.ToLowerInvariant()}-{slot.Order:00}-", Version,
                r.ExecutionId, r.PlanId, r.EventId, r.Profile.ProfileId, r.Profile.ProfileVersion, p.Variant, slot.SlotId, slot.Order, q.QuestionId, objective.ObjectiveId);
            var selections = selected.Evidence.References.Select((k, i) => new DocumentaryKnowledgeSelection(
                DocumentaryIntentChecksum.Id("dks-", opportunityId, k.ReferenceId), p.Variant, opportunityId, q.QuestionId,
                k.ReferenceId, k.SourceArtifact, k.SourcePointer, k.SemanticChecksum, slot.PurposeCode, "QuestionCertifiedReference", i == 0, selected.Evidence.Status)).ToArray();

            var supporting = slot.CanConsolidateSupportingQuestions
                ? questions.Where(x => x.QuestionId != q.QuestionId && IsSlotEligible(x, p, slot) && Evidence(x, certified).Status != QuestionEvidenceStatus.Rejected)
                    .Where(x => Evidence(x, certified).Status is not (QuestionEvidenceStatus.EditorialOnly or QuestionEvidenceStatus.Mixed) || slot.CanUseEditorialOnlyQuestion)
                    .OrderBy(x => Priority(x.Priority)).ThenBy(x => x.Order).ThenBy(x => x.QuestionId, StringComparer.Ordinal).Take(1).ToArray()
                : [];
            var records = new List<DocumentaryQuestionCoverageRecord>
            {
                new(q.QuestionId, p.Variant, "Primary", opportunityId, q.QuestionId, "PrimarySlotAllocation", "SlotPrimaryQuestion")
            };
            records.AddRange(supporting.Select(x => new DocumentaryQuestionCoverageRecord(x.QuestionId, p.Variant, "Supporting", opportunityId,
                q.QuestionId, "ConsolidatedSupportingQuestion", "SlotAllowsSupportingConsolidation")));
            var editorial = selected.Evidence.Status is QuestionEvidenceStatus.EditorialOnly or QuestionEvidenceStatus.Mixed
                ? new[] { new DocumentaryEditorialConstraint("RequiresEditorialCompletion"), new DocumentaryEditorialConstraint("DoNotClaimUnsupportedDetail") } : [];
            var scene = new DocumentarySceneOpportunity(opportunityId, p.Variant, slot.Order, slot.SlotId, slot.NarrativeStage, slot.SceneRole,
                slot.PurposeCode, q.QuestionId, q.QuestionText, supporting.Select(x => x.QuestionId).ToArray(), selected.Evidence.Status,
                objective.ObjectiveId, objective.Text, slot.OutcomeTemplateCode, slot.EditorialOutcome, selections, records, editorial,
                selected.Evidence.Status == QuestionEvidenceStatus.ResolvedGrounded ? [] : ["SpecificViewingTime", "SpecificHorizon", "EquipmentRequirement"],
                slot.ClosingBehavior.Equals("Terminal", StringComparison.OrdinalIgnoreCase) ? "Close" : slot.TransitionIntentCode,
                durationBySlot[slot.SlotId], p.MinimumSceneDurationSeconds, p.MaximumSceneDurationSeconds, slot.VisualOpportunityIntent, "");
            scene = scene with {
                EditorialPriority = slot.EditorialPriority, ObjectiveCuriosityGoal = slot.ObjectiveCuriosityGoal,
                ObjectiveEmotionalGoal = slot.ObjectiveEmotionalGoal, VisualOpportunityType = slot.VisualOpportunityType,
                VisualIsScientificallyRequired = slot.VisualIsScientificallyRequired,
                TransitionNextQuestionSeed = slot.TransitionNextQuestionSeed,
                TransitionEditorialDirection = slot.TransitionEditorialDirection };
            scene = scene with { DeterministicChecksum = DocumentaryIntentChecksum.Hash(scene with { DeterministicChecksum = "" }) };
            scenes.Add(scene);
        }

        var coverage = Coverage(questions, scenes);
        var covered = coverage.CoveredQuestions.ToHashSet(StringComparer.Ordinal);
        var deferrals = questions.Where(x => !covered.Contains(x.QuestionId)).Select(q => Deferral(p, q)).OrderBy(x => x.QuestionId, StringComparer.Ordinal).ToArray();
        foreach (var required in p.RequiredQuestionCoverage.Order(StringComparer.Ordinal))
            if (!covered.Contains(required) && !deferrals.Any(x => x.QuestionId == required && x.ProfilePermissionCode == "ProfileAuthorizedHighDeferral"))
                throw new PlanningException("DI_REQUIRED_HIGH_QUESTION_UNCOVERED", $"Required High question '{required}' is not safely covered or profile-authorized for deferral.");
        var variant = new DocumentaryVariantIntent(p.Variant, DocumentaryIntentChecksum.Id($"dvi-{p.Variant.ToLowerInvariant()}-", Version,
            r.ExecutionId, r.PlanId, r.EventId, r.Profile.ProfileId, r.Profile.ProfileVersion, p.Variant), r.Profile.ProfileId,
            r.Profile.ProfileVersion, p.ExpectedSceneCount, p.DurationBudgetSeconds, scenes, coverage, coverage, deferrals,
            scenes.SelectMany(x => x.EditorialConstraints).Distinct().OrderBy(x => x.Code, StringComparer.Ordinal).ToArray(), scenes.Sum(x => x.TargetDurationSeconds), "");
        return variant with { DeterministicChecksum = DocumentaryIntentChecksum.Hash(variant with { DeterministicChecksum = "" }) };
    }

    private static bool IsSlotEligible(CuriosityViewerQuestion q, DocumentaryVariantProfile p, DocumentaryNarrativeSlot slot) =>
        !string.IsNullOrWhiteSpace(q.QuestionId) && q.ApplicableVariants.Contains(p.Variant, StringComparer.OrdinalIgnoreCase) &&
        (slot.AllowedQuestionCategories.Count == 0 || slot.AllowedQuestionCategories.Contains(q.Category, StringComparer.Ordinal)) &&
        (!q.RequiresEditorialAttention || slot.CanUseEditorialOnlyQuestion);

    private static (QuestionEvidenceStatus Status, IReadOnlyList<CertifiedDocumentaryKnowledgeReference> References) Evidence(
        CuriosityViewerQuestion q, IReadOnlyDictionary<string, CertifiedDocumentaryKnowledgeReference> certified)
    {
        var claimed = q.KnowledgeReferences.Where(x => x.ResolutionStatus.Equals("Resolved", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var reference in claimed)
            if (!certified.TryGetValue(reference.ReferenceId, out var authority) || !ValidAuthority(authority))
                throw new PlanningException("DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID", $"Resolved reference '{reference.ReferenceId}' is absent or invalid in certified knowledge.");
        var refs = claimed.Select(x => certified[x.ReferenceId]).DistinctBy(x => x.ReferenceId).OrderBy(x => x.ReferenceId, StringComparer.Ordinal).ToArray();
        var phase3Resolved = q.AnswerResolutionStatus.Equals("Resolved", StringComparison.OrdinalIgnoreCase) && q.AnswerUsability.Equals("Certified", StringComparison.OrdinalIgnoreCase);
        var requiresGrounding = q.Category is "ScientificExplanation" or "CulturalHistoricalContext";
        var grounded = phase3Resolved && refs.Length != 0 && q.GroundingWarnings.Count == 0;
        var status = grounded ? (q.RequiresEditorialAttention ? QuestionEvidenceStatus.Mixed : QuestionEvidenceStatus.ResolvedGrounded)
            : q.RequiresEditorialAttention || !requiresGrounding ? QuestionEvidenceStatus.EditorialOnly : QuestionEvidenceStatus.Rejected;
        return (status, refs);
    }

    private static bool ValidAuthority(CertifiedDocumentaryKnowledgeReference x) => !string.IsNullOrWhiteSpace(x.ReferenceId) &&
        !string.IsNullOrWhiteSpace(x.SourceArtifact) && !x.SourceArtifact.Contains("compatibility/", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(x.SourcePointer) && !string.IsNullOrWhiteSpace(x.SemanticChecksum);

    internal static IReadOnlyDictionary<string, int> AllocateDurations(DocumentaryVariantProfile p, IReadOnlyList<DocumentaryNarrativeSlot> slots)
    {
        if (slots.Count != p.ExpectedSceneCount || p.DurationBudgetSeconds < (long)slots.Count * p.MinimumSceneDurationSeconds ||
            p.DurationBudgetSeconds > (long)slots.Count * p.MaximumSceneDurationSeconds || slots.Any(x => x.DurationWeight <= 0))
            throw new PlanningException("DI_DURATION_ALLOCATION_FAILED", $"{p.Variant} duration constraints are impossible.");
        var result = slots.ToDictionary(x => x.SlotId, _ => p.MinimumSceneDurationSeconds, StringComparer.Ordinal);
        var remaining = p.DurationBudgetSeconds - result.Values.Sum();
        while (remaining > 0)
        {
            var eligible = slots.Where(s => result[s.SlotId] < p.MaximumSceneDurationSeconds).ToArray();
            if (eligible.Length == 0) throw new PlanningException("DI_DURATION_ALLOCATION_FAILED", "Duration maximums cannot satisfy budget.");
            var weight = eligible.Sum(x => (decimal)x.DurationWeight);
            var awards = eligible.Select(s => new { Slot = s, Exact = remaining * (decimal)s.DurationWeight / weight })
                .Select(x => new { x.Slot, Floor = Math.Min((int)decimal.Floor(x.Exact), p.MaximumSceneDurationSeconds - result[x.Slot.SlotId]), Fraction = x.Exact - decimal.Floor(x.Exact) }).ToArray();
            var granted = 0;
            foreach (var a in awards) { result[a.Slot.SlotId] += a.Floor; granted += a.Floor; }
            remaining -= granted;
            if (remaining == 0) break;
            var remainderWinner = awards.Where(x => result[x.Slot.SlotId] < p.MaximumSceneDurationSeconds)
                .OrderByDescending(x => x.Fraction).ThenBy(x => x.Slot.Order).ThenBy(x => x.Slot.SlotId, StringComparer.Ordinal).FirstOrDefault();
            if (remainderWinner is null) continue;
            result[remainderWinner.Slot.SlotId]++; remaining--;
        }
        return result;
    }

    private static DocumentaryQuestionDeferral Deferral(DocumentaryVariantProfile p, CuriosityViewerQuestion q)
    {
        var authorized = p.AuthorizedHighQuestionDeferrals.TryGetValue(q.QuestionId, out var reason);
        return new(q.QuestionId, p.Variant, q.RequiresEditorialAttention ? QuestionEvidenceStatus.EditorialOnly : QuestionEvidenceStatus.Deferred,
            authorized ? reason! : "NotSelectedCapacity", authorized ? "ProfileAuthorizedHighDeferral" : "ProfileNonRequiredQuestion",
            null, authorized ? "StableProfileDeferral" : "StableCapacityDeferral");
    }

    private static DocumentaryCoverageSummary Coverage(IEnumerable<CuriosityViewerQuestion> questions, IEnumerable<DocumentarySceneOpportunity> scenes)
    {
        var all = scenes.ToArray(); var primary = all.Select(x => x.PrimaryViewerQuestionId).ToArray();
        var supporting = all.SelectMany(x => x.QuestionCoverageRecords).Where(x => x.CoverageType == "Supporting").Select(x => x.QuestionId).ToArray();
        var covered = primary.Concat(supporting).Distinct().Order(StringComparer.Ordinal).ToArray();
        return new(covered, questions.Where(x => x.RequiresEditorialAttention && covered.Contains(x.QuestionId, StringComparer.Ordinal)).Select(x => x.QuestionId).Order(StringComparer.Ordinal).ToArray(),
            questions.Select(x => x.QuestionId).Except(covered, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            primary.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).Order(StringComparer.Ordinal).ToArray(),
            supporting.Distinct().Order(StringComparer.Ordinal).ToArray(), all.SelectMany(x => x.SelectedKnowledgeReferences).Select(x => x.KnowledgeReferenceId).Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    private static int Priority(string value) => value switch { "High" => 0, "Medium" => 1, "Normal" => 2, _ => 3 };
    private static DocumentaryIntentPlanningResult Failed(DocumentaryIntentPlanningRequest r, IReadOnlyList<DocumentaryPlanningIssue> errors) =>
        new(false, null, errors, [], "Unresolved:" + (r.Profile?.ProfileId ?? "unknown"), null, null, null, []);
    internal sealed class PlanningException(string code, string message) : Exception(message) { public string Code { get; } = code; }
}
