namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryIntentValidator
{
    public static IReadOnlyList<DocumentaryPlanningIssue> Validate(DocumentaryIntent intent, DocumentaryIntentPlanningRequest request)
    {
        var errors = new List<DocumentaryPlanningIssue>();
        if (intent.ExecutionId != request.ExecutionId || intent.PlanId != request.PlanId || intent.EventId != request.EventId)
            Add("DI_UPSTREAM_IDENTITY_MISMATCH", "Intent identity does not match its upstream snapshot.");
        if (intent.ProfileId != request.Profile.ProfileId || intent.ProfileVersion != request.Profile.ProfileVersion)
            Add("DI_PROFILE_INVALID", "Intent profile identity is invalid.");
        ValidateVariant(intent.LongVariantIntent, request.Profile.LongProfile);
        ValidateVariant(intent.ShortVariantIntent, request.Profile.ShortProfile);
        if (intent.LongVariantIntent.SceneOpportunities.Select(x=>x.OpportunityId).Intersect(intent.ShortVariantIntent.SceneOpportunities.Select(x=>x.OpportunityId), StringComparer.Ordinal).Any())
            Add("DI_SHORT_INTENT_INVALID", "Long and Short opportunity identities overlap.");
        if (!DocumentaryIntentChecksum.HasValidChecksum(intent)) Add("DI_CHECKSUM_FAILED", "Intent checksum is invalid.");
        return errors;

        void ValidateVariant(DocumentaryVariantIntent variant, DocumentaryVariantProfile profile)
        {
            var scenes=variant.SceneOpportunities;
            if(scenes.Count!=profile.ExpectedSceneCount) Add("DI_SLOT_ALLOCATION_FAILED",$"{variant.Variant} scene count differs from profile.");
            if(!scenes.Select(x=>x.Order).SequenceEqual(Enumerable.Range(1,scenes.Count))) Add("DI_SLOT_ALLOCATION_FAILED",$"{variant.Variant} order is not contiguous.");
            if(scenes.Select(x=>x.OpportunityId).Distinct(StringComparer.Ordinal).Count()!=scenes.Count || scenes.Select(x=>x.ProfileSlotId).Distinct(StringComparer.Ordinal).Count()!=scenes.Count) Add("DI_SLOT_ALLOCATION_FAILED",$"{variant.Variant} contains duplicate identities.");
            if(scenes.Any(x=>string.IsNullOrWhiteSpace(x.PrimaryViewerQuestionId) || !request.QuestionBank.Questions.Any(q=>q.QuestionId==x.PrimaryViewerQuestionId))) Add("DI_QUESTION_BANK_INVALID",$"{variant.Variant} has an unknown primary question.");
            if(scenes.Any(x=>!request.LearningObjectives.Objectives.Any(o=>o.ObjectiveId==x.LearningObjectiveId))) Add("DI_OBJECTIVE_REFERENCE_INVALID",$"{variant.Variant} has an unknown objective.");
            var certified=request.CertifiedKnowledge.Select(x=>x.ReferenceId).ToHashSet(StringComparer.Ordinal);
            if(scenes.SelectMany(x=>x.SelectedKnowledgeReferences).Any(x=>!certified.Contains(x.KnowledgeReferenceId) || x.SourceArtifact.Contains("compatibility/",StringComparison.OrdinalIgnoreCase))) Add("DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID",$"{variant.Variant} has an uncertified knowledge reference.");
            if(scenes.Where(x=>x.QuestionEvidenceStatus==QuestionEvidenceStatus.EditorialOnly).Any(x=>x.SelectedKnowledgeReferences.Count!=0 || x.EditorialConstraints.Count==0)) Add("DI_UNSUPPORTED_FACT_PROMOTION",$"{variant.Variant} editorial-only safety failed.");
            if(scenes.Sum(x=>x.TargetDurationSeconds)!=profile.DurationBudgetSeconds || scenes.Any(x=>x.TargetDurationSeconds<x.MinimumDurationSeconds||x.TargetDurationSeconds>x.MaximumDurationSeconds)) Add("DI_DURATION_ALLOCATION_FAILED",$"{variant.Variant} durations do not reconcile.");
            if(scenes.Count>0 && (scenes.Take(scenes.Count-1).Any(x=>string.IsNullOrWhiteSpace(x.TransitionIntent)) || scenes[^1].TransitionIntent!="Close")) Add("DI_SLOT_ALLOCATION_FAILED",$"{variant.Variant} transitions are incomplete.");
            foreach(var high in profile.RequiredQuestionCoverage) if(!variant.QuestionCoverage.CoveredQuestions.Contains(high,StringComparer.Ordinal) && !variant.DeferredQuestions.Contains(high,StringComparer.Ordinal)) Add("DI_REQUIRED_HIGH_QUESTION_UNCOVERED",$"Required question '{high}' is neither covered nor deferred.");
        }
        void Add(string code,string message)=>errors.Add(new(code,message));
    }
}
