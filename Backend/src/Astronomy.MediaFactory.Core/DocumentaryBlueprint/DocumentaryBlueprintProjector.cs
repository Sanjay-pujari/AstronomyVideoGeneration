namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Pure projection from a certified intent; it validates but never reallocates planner decisions.</summary>
public sealed class DocumentaryBlueprintProjector(
    DocumentaryBlueprintBuilder builder,
    DocumentaryBlueprintAggregateValidator aggregateValidator,
    DocumentaryBlueprintVariantProjectionValidator? variantValidator = null) : IDocumentaryBlueprintProjector
{
    public const string ProjectionVersion = "1.0";

    public DocumentaryBlueprintProjectionResult Project(DocumentaryBlueprintProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Intent);
        ArgumentNullException.ThrowIfNull(request.Profile);
        var intent = request.Intent;
        var profile = request.Profile;
        var errors = new List<DocumentaryBlueprintProjectionDiagnostic>();

        if (!DocumentaryIntentChecksum.HasValidChecksum(intent))
            errors.Add(new("P4P_INTENT_INVALID", "Documentary Intent checksum is invalid."));
        if (intent.ProfileId != profile.ProfileId || intent.ProfileVersion != profile.ProfileVersion ||
            intent.SourceLineage.ProfileId != profile.ProfileId || intent.SourceLineage.ProfileVersion != profile.ProfileVersion ||
            intent.LongVariantIntent.ProfileId != profile.ProfileId || intent.LongVariantIntent.ProfileVersion != profile.ProfileVersion ||
            intent.ShortVariantIntent.ProfileId != profile.ProfileId || intent.ShortVariantIntent.ProfileVersion != profile.ProfileVersion)
            errors.Add(new("P4P_PROFILE_MISMATCH", "Intent and requested profile identities do not match."));

        ValidateVariant(intent.LongVariantIntent, profile.LongProfile, "Long", errors);
        ValidateVariant(intent.ShortVariantIntent, profile.ShortProfile, "Short", errors);
        if (errors.Count != 0) return Failed(errors);

        DocumentaryBlueprintVariantArtifact longArtifact;
        DocumentaryBlueprintVariantArtifact shortArtifact;
        try { longArtifact = ProjectVariant(intent, intent.LongVariantIntent, profile.LongProfile); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return Failed([new("P4P_BUILD_LONG_FAILED", ex.Message)]); }
        try { shortArtifact = ProjectVariant(intent, intent.ShortVariantIntent, profile.ShortProfile); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return Failed([new("P4P_BUILD_SHORT_FAILED", ex.Message)]); }

        var coverage = AggregateCoverage(longArtifact, shortArtifact);
        var duration = new DocumentaryBlueprintAggregateDurationSummary(
            longArtifact.TotalAllocatedDurationSeconds, shortArtifact.TotalAllocatedDurationSeconds,
            checked(longArtifact.TotalAllocatedDurationSeconds + shortArtifact.TotalAllocatedDurationSeconds));
        var reconciler = variantValidator ?? new DocumentaryBlueprintVariantProjectionValidator();
        var longReconciliation = reconciler.Validate(intent.LongVariantIntent,
            intent.LongVariantIntent.SceneOpportunities.OrderBy(x => x.Order).Select(x => MapScene(intent, intent.LongVariantIntent, x)).ToArray(),
            longArtifact.Blueprint, longArtifact.SceneTraceability);
        var shortReconciliation = reconciler.Validate(intent.ShortVariantIntent,
            intent.ShortVariantIntent.SceneOpportunities.OrderBy(x => x.Order).Select(x => MapScene(intent, intent.ShortVariantIntent, x)).ToArray(),
            shortArtifact.Blueprint, shortArtifact.SceneTraceability);
        var reconciliationErrors = longReconciliation.Errors.Concat(shortReconciliation.Errors).ToArray();
        if (reconciliationErrors.Length != 0)
            return Failed(reconciliationErrors, longArtifact, shortArtifact, longReconciliation, shortReconciliation);

        var aggregate = new DocumentaryBlueprintAggregate(
            "1.0", "1.0", ProjectionVersion,
            DocumentaryBlueprintProjectionChecksum.Id("dba-", ProjectionVersion, intent.IntentId, intent.ProfileId, intent.ProfileVersion),
            intent.ExecutionId, intent.PlanId, intent.EventId, intent.Language, intent.ProfileId, intent.ProfileVersion,
            intent.IntentId, intent.DeterministicChecksum, intent.SourceLineage,
            longArtifact, shortArtifact, coverage, duration, [], string.Empty);
        aggregate = aggregate with { DeterministicChecksum = DocumentaryBlueprintProjectionChecksum.CalculateAggregate(aggregate) };
        var aggregateErrors = aggregateValidator.Validate(aggregate, longArtifact, shortArtifact);
        if (aggregateErrors.Count != 0) return Failed(aggregateErrors, longArtifact, shortArtifact, longReconciliation, shortReconciliation);

        return new(true, aggregate, longArtifact, shortArtifact, [], [],
            longArtifact.ActualSceneCount, shortArtifact.ActualSceneCount,
            longArtifact.TotalAllocatedDurationSeconds, shortArtifact.TotalAllocatedDurationSeconds,
            longReconciliation.QuestionPassed && shortReconciliation.QuestionPassed,
            longReconciliation.ObjectivePassed && shortReconciliation.ObjectivePassed,
            longReconciliation.KnowledgePassed && shortReconciliation.KnowledgePassed,
            longReconciliation.SafetyPassed && shortReconciliation.SafetyPassed,
            longReconciliation.DurationPassed && shortReconciliation.DurationPassed,
            longReconciliation.TransitionPassed && shortReconciliation.TransitionPassed, true, true,
            ["IntentChecksumValidated", "ExistingBuilderInvokedOncePerVariant", "SourceOpportunityValuesReconciled",
             "LongShortSceneIdsDisjoint", "VariantAndAggregateChecksumsValidated"]);

        DocumentaryBlueprintProjectionResult Failed(IReadOnlyList<DocumentaryBlueprintProjectionDiagnostic> issues,
            DocumentaryBlueprintVariantArtifact? longValue = null, DocumentaryBlueprintVariantArtifact? shortValue = null,
            DocumentaryBlueprintVariantProjectionValidationResult? longValidation = null,
            DocumentaryBlueprintVariantProjectionValidationResult? shortValidation = null)
        {
            bool Both(Func<DocumentaryBlueprintVariantProjectionValidationResult, bool> predicate) =>
                longValidation is not null && shortValidation is not null &&
                predicate(longValidation) && predicate(shortValidation);

            return new(false, null, longValue, shortValue, issues, [], longValue?.ActualSceneCount ?? 0,
                shortValue?.ActualSceneCount ?? 0, longValue?.TotalAllocatedDurationSeconds ?? 0,
                shortValue?.TotalAllocatedDurationSeconds ?? 0,
                Both(x => x.QuestionPassed), Both(x => x.ObjectivePassed), Both(x => x.KnowledgePassed),
                Both(x => x.SafetyPassed), Both(x => x.DurationPassed), Both(x => x.TransitionPassed), false, false, []);
        }
    }

    private DocumentaryBlueprintVariantArtifact ProjectVariant(DocumentaryIntent intent,
        DocumentaryVariantIntent variant, DocumentaryVariantProfile profile)
    {
        // This method receives one variant only: Short can neither enumerate nor derive from Long (and vice versa).
        var orderedOpportunities = variant.SceneOpportunities.OrderBy(x => x.Order)
            .ThenBy(x => x.OpportunityId, StringComparer.Ordinal).ToArray();
        var sceneInputs = orderedOpportunities.Select(scene => MapScene(intent, variant, scene)).ToArray();
        var artifactId = DocumentaryBlueprintProjectionChecksum.Id($"dbv-{variant.Variant.ToLowerInvariant()}-",
            ProjectionVersion, intent.IntentId, variant.VariantIntentId, variant.Variant);
        var request = new DocumentaryBlueprintBuildRequest(
            artifactId + "-blueprint", intent.GeneratedFromPhase2Checksum, intent.EventId, intent.DocumentaryGoal,
            variant.Variant == "Long" ? BlueprintPublicationFormat.LongDocumentary : BlueprintPublicationFormat.ShortDocumentary,
            intent.Language, ProjectionVersion,
            new(DateTimeOffset.UnixEpoch, nameof(DocumentaryBlueprintProjector), ProjectionVersion,
                intent.GeneratedFromPhase2Checksum, "1.0", artifactId), sceneInputs);
        var blueprint = builder.Build(request); // The certified mapper is the sole blueprint constructor.
        var traceability = orderedOpportunities.Zip(blueprint.Scenes).Select(pair =>
            new DocumentarySceneBlueprintTraceability(pair.Second.SceneId, pair.First.OpportunityId,
                pair.First.DeterministicChecksum, pair.First.PrimaryViewerQuestionId,
                pair.First.SupportingViewerQuestionIds, pair.First.LearningObjectiveId,
                pair.First.QuestionEvidenceStatus, pair.First.ProfileSlotId,
                pair.First.MinimumDurationSeconds, pair.First.MaximumDurationSeconds,
                pair.First.EditorialConstraints, pair.First.MustNotClaim,
                pair.First.SelectedKnowledgeReferences)).ToArray();
        var artifact = new DocumentaryBlueprintVariantArtifact(
            "1.0", "1.0", ProjectionVersion, artifactId, intent.ExecutionId, intent.PlanId, intent.EventId,
            intent.Language, intent.ProfileId, intent.ProfileVersion, variant.Variant, intent.IntentId,
            variant.VariantIntentId, intent.DeterministicChecksum, variant.DeterministicChecksum,
            intent.SourceLineage, blueprint, traceability, variant.QuestionCoverage, variant.KnowledgeCoverage,
            variant.DeferredQuestions, variant.EditorialConstraints, profile.ExpectedSceneCount,
            blueprint.Scenes.Count, profile.DurationBudgetSeconds, variant.TotalAllocatedDurationSeconds, string.Empty);
        return artifact with { DeterministicChecksum = DocumentaryBlueprintProjectionChecksum.CalculateVariant(artifact) };
    }

    internal static DocumentarySceneBlueprintInput MapScene(DocumentaryIntent intent,
        DocumentaryVariantIntent variant, DocumentarySceneOpportunity scene)
    {
        if (!Enum.TryParse<DocumentaryNarrativeStage>(scene.NarrativeStage, true, out var stage) ||
            !Enum.TryParse<DocumentarySceneRole>(scene.SceneRole, true, out var role))
            throw new ArgumentException($"Opportunity '{scene.OpportunityId}' has an unsupported stage or role.");
        var sceneId = DocumentaryBlueprintProjectionChecksum.Id($"dbs-{variant.Variant.ToLowerInvariant()}-{scene.Order:00}-",
            ProjectionVersion, intent.IntentId, variant.VariantIntentId, scene.OpportunityId, variant.Variant,
            scene.Order, scene.ProfileSlotId);
        return new(sceneId, scene.Order, scene.PrimaryViewerQuestionText, stage, role,
            new(scene.PrimaryViewerQuestionText),
            new(scene.LearningObjectiveText, scene.LearningObjectiveText, scene.ObjectiveCuriosityGoal, scene.ObjectiveEmotionalGoal),
            EditorialOutcomeFor(role, scene),
            scene.EditorialPriority,
            scene.SelectedKnowledgeReferences.Select(x => new KnowledgeReference(
                x.KnowledgeReferenceId, x.SourcePointer, x.PurposeCode, x.IsPrimary)).ToArray(),
            [new(scene.VisualOpportunityIntent, scene.VisualOpportunityType, null, null, scene.VisualIsScientificallyRequired)],
            new(scene.TransitionIntent, scene.TransitionNextQuestionSeed, scene.TransitionEditorialDirection), scene.TargetDurationSeconds);
    }

    private static EditorialOutcome EditorialOutcomeFor(DocumentarySceneRole role, DocumentarySceneOpportunity scene) => role switch
    {
        DocumentarySceneRole.OpeningHook => new(scene.EditorialOutcome, scene.EditorialOutcomeCode, false, false, true, false, false),
        DocumentarySceneRole.PracticalObservation or DocumentarySceneRole.AstrophotographyGuide => new(scene.EditorialOutcome, scene.EditorialOutcomeCode, false, true, false, true, false),
        DocumentarySceneRole.ReflectiveClosing => new(scene.EditorialOutcome, scene.EditorialOutcomeCode, false, true, false, false, true),
        DocumentarySceneRole.ScientificExplanation or DocumentarySceneRole.CoreDiscovery or DocumentarySceneRole.MisconceptionCorrection => new(scene.EditorialOutcome, scene.EditorialOutcomeCode, true, true, false, false, false),
        _ => new(scene.EditorialOutcome, scene.EditorialOutcomeCode, true, false, false, false, false)
    };

    private static void ValidateVariant(DocumentaryVariantIntent variant, DocumentaryVariantProfile profile,
        string expectedVariant, List<DocumentaryBlueprintProjectionDiagnostic> errors)
    {
        void Add(string code, string message) => errors.Add(new(code, $"{expectedVariant}: {message}"));
        var orderedScenes = variant.SceneOpportunities.OrderBy(x => x.Order)
            .ThenBy(x => x.OpportunityId, StringComparer.Ordinal).ToArray();
        if (variant.Variant != expectedVariant || profile.Variant != expectedVariant)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Variant identity is invalid.");
        if (DocumentaryIntentChecksum.Hash(variant with { DeterministicChecksum = "" }) != variant.DeterministicChecksum ||
            variant.SceneOpportunities.Any(x => DocumentaryIntentChecksum.Hash(x with { DeterministicChecksum = "" }) != x.DeterministicChecksum))
            Add("P4P_INTENT_INVALID", "Variant or opportunity checksum is invalid.");
        if (variant.SceneOpportunities.Count != profile.ExpectedSceneCount || variant.ExpectedSceneCount != profile.ExpectedSceneCount)
            Add("P4P_DURATION_RECONCILIATION_FAILED", "Expected scene count differs from the profile.");
        if (orderedScenes.Select(x => x.Order).SequenceEqual(Enumerable.Range(1, orderedScenes.Length)) is false)
            Add("P4P_VARIANT_RECONCILIATION_FAILED", "Scene order is not contiguous.");
        foreach (var scene in orderedScenes)
        {
            var primary = scene.QuestionCoverageRecords.Where(x => x.CoverageType == "Primary").ToArray();
            if (string.IsNullOrWhiteSpace(scene.PrimaryViewerQuestionId) || primary.Length != 1 ||
                primary[0].QuestionId != scene.PrimaryViewerQuestionId || primary[0].PrimaryQuestionId != scene.PrimaryViewerQuestionId)
                Add("P4P_PRIMARY_QUESTION_INVALID", $"Opportunity '{scene.OpportunityId}' has no single resolvable primary question.");
            if (string.IsNullOrWhiteSpace(scene.LearningObjectiveId) || string.IsNullOrWhiteSpace(scene.LearningObjectiveText))
                Add("P4P_OBJECTIVE_INVALID", $"Opportunity '{scene.OpportunityId}' has an invalid objective authority.");
            if (scene.SelectedKnowledgeReferences.Any(x => x.SceneOpportunityId != scene.OpportunityId ||
                x.PrimaryViewerQuestionId != scene.PrimaryViewerQuestionId || x.Variant != variant.Variant ||
                string.IsNullOrWhiteSpace(x.KnowledgeReferenceId) || string.IsNullOrWhiteSpace(x.SourceArtifact) ||
                string.IsNullOrWhiteSpace(x.SourcePointer) || string.IsNullOrWhiteSpace(x.SemanticChecksum) ||
                x.PurposeCode != scene.PurposeCode || x.EvidenceStatus != scene.QuestionEvidenceStatus))
                Add("P4P_KNOWLEDGE_RECONCILIATION_FAILED", $"Opportunity '{scene.OpportunityId}' contains knowledge drift.");
            if (scene.SelectedKnowledgeReferences.Count == 0 || scene.SelectedKnowledgeReferences.Count(x => x.IsPrimary) != 1)
                Add("P4P_KNOWLEDGE_RECONCILIATION_FAILED", $"Opportunity '{scene.OpportunityId}' must contain exactly one primary certified knowledge reference.");
            if (scene.EditorialConstraints.Any(x => x.Code == "RequiresEditorialCompletion"))
                Add("P4P_EDITORIAL_SAFETY_FAILED", $"Opportunity '{scene.OpportunityId}' remains unresolved at publication projection.");
            if ((scene.QuestionEvidenceStatus is QuestionEvidenceStatus.EditorialOnly or QuestionEvidenceStatus.Mixed) &&
                (scene.EditorialConstraints.Count == 0 || scene.MustNotClaim.Count == 0 ||
                 scene.QuestionEvidenceStatus == QuestionEvidenceStatus.EditorialOnly && scene.SelectedKnowledgeReferences.Count != 0))
                Add("P4P_EDITORIAL_SAFETY_FAILED", $"Opportunity '{scene.OpportunityId}' lost editorial safety controls.");
            if (scene.TargetDurationSeconds < scene.MinimumDurationSeconds || scene.TargetDurationSeconds > scene.MaximumDurationSeconds)
                Add("P4P_DURATION_RECONCILIATION_FAILED", $"Opportunity '{scene.OpportunityId}' duration is outside its certified range.");
        }
        if (orderedScenes.Sum(x => x.TargetDurationSeconds) != profile.DurationBudgetSeconds ||
            variant.DurationBudgetSeconds != profile.DurationBudgetSeconds ||
            variant.TotalAllocatedDurationSeconds != profile.DurationBudgetSeconds)
            Add("P4P_DURATION_RECONCILIATION_FAILED", "Duration totals differ from the certified profile.");
        if (orderedScenes.Length != 0 &&
            (orderedScenes.Take(orderedScenes.Length - 1).Any(x => string.IsNullOrWhiteSpace(x.TransitionIntent)) ||
             orderedScenes[^1].TransitionIntent != "Close"))
            Add("P4P_TRANSITION_RECONCILIATION_FAILED", "Transition intent does not satisfy the profile terminal policy.");
    }

    private static DocumentaryBlueprintAggregateCoverage AggregateCoverage(
        DocumentaryBlueprintVariantArtifact longArtifact, DocumentaryBlueprintVariantArtifact shortArtifact) => new(
        Union(longArtifact.QuestionCoverage.CoveredQuestions, shortArtifact.QuestionCoverage.CoveredQuestions),
        Union(longArtifact.QuestionCoverage.EditorialQuestions, shortArtifact.QuestionCoverage.EditorialQuestions),
        Union(longArtifact.QuestionCoverage.DeferredQuestions, shortArtifact.QuestionCoverage.DeferredQuestions),
        Union(longArtifact.KnowledgeCoverage.CoveredKnowledgeReferences, shortArtifact.KnowledgeCoverage.CoveredKnowledgeReferences));

    private static IReadOnlyList<string> Union(IEnumerable<string> first, IEnumerable<string> second) =>
        first.Concat(second).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
