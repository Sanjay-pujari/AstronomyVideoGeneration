namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

using CuriosityViewerQuestion = global::Astronomy.MediaFactory.Core.ViewerQuestion;

/// <summary>Pure, filesystem-free Phase 4 intent planner. Both variants are independently allocated from the request.</summary>
public sealed class DocumentaryIntentPlanner : IDocumentaryIntentPlanner
{
    public const string Version = "1.0";
    public DocumentaryIntentPlanningResult Plan(DocumentaryIntentPlanningRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var errors = ValidateInput(r);
        if (errors.Count != 0) return Failed(r, errors);
        var questions = r.QuestionBank.Questions.OrderBy(q => Priority(q.Priority)).ThenBy(q => q.Order).ThenBy(q => q.QuestionId, StringComparer.Ordinal).ToArray();
        try
        {
            // Deliberately no variant result is passed to the other allocation.
            var longIntent = Allocate(r, r.Profile.LongProfile, questions);
            var shortIntent = Allocate(r, r.Profile.ShortProfile, questions);
            var all = longIntent.SceneOpportunities.Concat(shortIntent.SceneOpportunities).ToArray();
            var coverage = Coverage(questions, all);
            var constraints = all.SelectMany(x => x.EditorialConstraints).Distinct().OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
            var intentId = DocumentaryIntentChecksum.Id("di-", Version, r.ExecutionId, r.PlanId, r.EventId, r.Profile.ProfileId, r.Profile.ProfileVersion);
            var intent = new DocumentaryIntent("1.0", "1.0", Version, intentId, r.ExecutionId, r.PlanId, r.EventId,
                r.Language, r.Profile.ProfileId, r.Profile.ProfileVersion, r.SourceLineage.Phase2SemanticChecksum,
                r.QuestionBank.Metadata.Checksum, r.SourceLineage, r.AudienceIntent, r.DocumentaryGoal,
                r.LearningObjectives.Objectives.OrderBy(x=>x.ObjectiveId,StringComparer.Ordinal).Select(x=>x.Text).ToArray(),
                constraints.Select(x=>x.Code).ToArray(), ["UseCertifiedKnowledgeOnly"], r.Profile.KnowledgeCoveragePolicy,
                r.Profile.QuestionCoveragePolicy, longIntent, shortIntent, coverage, constraints, "");
            intent = intent with { DeterministicChecksum = DocumentaryIntentChecksum.Calculate(intent) };
            var validation = DocumentaryIntentValidator.Validate(intent, r);
            return validation.Count == 0
                ? new(true, intent, [], [], "Resolved:" + r.Profile.ProfileId, longIntent.QuestionCoverage,
                    longIntent.KnowledgeCoverage, coverage, ["SHA-256 canonical JSON", "Long and Short allocated independently"])
                : Failed(r, validation);
        }
        catch (PlanningException ex) { return Failed(r, [new(ex.Code, ex.Message)]); }
    }

    private static DocumentaryVariantIntent Allocate(DocumentaryIntentPlanningRequest r, DocumentaryVariantProfile p, CuriosityViewerQuestion[] qs)
    {
        var slots = p.NarrativeSlots.OrderBy(x=>x.Order).ThenBy(x=>x.SlotId,StringComparer.Ordinal).ToArray();
        if (slots.Length != p.ExpectedSceneCount || p.DurationBudgetSeconds < slots.Length*p.MinimumSceneDurationSeconds || p.DurationBudgetSeconds > slots.Length*p.MaximumSceneDurationSeconds)
            throw new PlanningException("DI_DURATION_ALLOCATION_FAILED", $"{p.Variant} profile count or duration constraints are impossible.");
        var durations = AllocateDurations(p, slots);
        var objectiveByQuestion = r.LearningObjectives.Objectives.SelectMany(o=>o.ViewerQuestionIds.Select(q=>(q,o))).GroupBy(x=>x.q,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.OrderBy(x=>x.o.ObjectiveId,StringComparer.Ordinal).First().o,StringComparer.Ordinal);
        var certified = r.CertifiedKnowledge.ToDictionary(x=>x.ReferenceId,StringComparer.Ordinal);
        var use = new Dictionary<string,int>(StringComparer.Ordinal); var scenes = new List<DocumentarySceneOpportunity>();
        foreach (var slot in slots)
        {
            var candidates = qs.Where(q => q.ApplicableVariants.Contains(p.Variant, StringComparer.OrdinalIgnoreCase))
                .Where(q => slot.AllowedQuestionCategories.Count == 0 || slot.AllowedQuestionCategories.Contains(q.Category,StringComparer.Ordinal))
                .Where(q => !q.RequiresEditorialAttention || slot.CanUseEditorialOnlyQuestion).ToArray();
            if (candidates.Length == 0) candidates = qs.Where(q=>!q.RequiresEditorialAttention || slot.CanUseEditorialOnlyQuestion).ToArray();
            var q = candidates.OrderByDescending(x=>slot.PreferredQuestionCategories.Contains(x.Category,StringComparer.Ordinal))
                .ThenBy(x=>use.GetValueOrDefault(x.QuestionId)).ThenBy(x=>Priority(x.Priority)).ThenBy(x=>x.Order).ThenBy(x=>x.QuestionId,StringComparer.Ordinal).FirstOrDefault();
            if (q is null || (use.GetValueOrDefault(q.QuestionId)>0 && !slot.CanReusePrimaryQuestion)) throw new PlanningException("DI_SLOT_ALLOCATION_FAILED", $"Slot '{slot.SlotId}' has no safe traceable question.");
            if (!objectiveByQuestion.TryGetValue(q.QuestionId,out var objective)) throw new PlanningException("DI_OBJECTIVE_REFERENCE_INVALID", $"Question '{q.QuestionId}' has no objective.");
            use[q.QuestionId]=use.GetValueOrDefault(q.QuestionId)+1;
            var resolvedRefs=q.KnowledgeReferences.Where(x=>x.ResolutionStatus=="Resolved" && certified.ContainsKey(x.ReferenceId)).ToArray();
            var status=q.RequiresEditorialAttention ? resolvedRefs.Length>0?QuestionEvidenceStatus.Mixed:QuestionEvidenceStatus.EditorialOnly : QuestionEvidenceStatus.ResolvedGrounded;
            var oid=DocumentaryIntentChecksum.Id($"dso-{p.Variant.ToLowerInvariant()}-{slot.Order:00}-",Version,r.ExecutionId,r.PlanId,r.EventId,r.Profile.ProfileId,r.Profile.ProfileVersion,p.Variant,slot.SlotId,slot.Order,q.QuestionId,objective.ObjectiveId);
            var selections = resolvedRefs.Select((x,i)=> { var k=certified[x.ReferenceId]; return new DocumentaryKnowledgeSelection(DocumentaryIntentChecksum.Id("dks-",oid,k.ReferenceId),p.Variant,oid,q.QuestionId,k.ReferenceId,k.SourceArtifact,k.SourcePointer,k.SemanticChecksum,slot.PurposeCode,"QuestionCertifiedReference",i==0,status); }).ToArray();
            if(slot.RequiredKnowledge && selections.Length==0 && status!=QuestionEvidenceStatus.EditorialOnly) throw new PlanningException("DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID",$"Slot '{slot.SlotId}' requires knowledge.");
            var ec=status is QuestionEvidenceStatus.EditorialOnly or QuestionEvidenceStatus.Mixed ? new[]{new DocumentaryEditorialConstraint("RequiresEditorialCompletion"),new DocumentaryEditorialConstraint("DoNotClaimUnsupportedDetail")} : [];
            var supporting=qs.Where(x=>x.QuestionId!=q.QuestionId && slot.PreferredQuestionCategories.Contains(x.Category,StringComparer.Ordinal)).Take(1).Select(x=>x.QuestionId).ToArray();
            var transition=slot.ClosingBehavior.Equals("Terminal",StringComparison.OrdinalIgnoreCase)?"Close":slot.TransitionIntentCode;
            var scene=new DocumentarySceneOpportunity(oid,p.Variant,slot.Order,slot.SlotId,slot.NarrativeStage,slot.SceneRole,slot.PurposeCode,q.QuestionId,q.QuestionText,supporting,status,objective.ObjectiveId,objective.Text,slot.OutcomeTemplateCode,slot.OutcomeTemplateCode,selections,ec,status==QuestionEvidenceStatus.ResolvedGrounded?[]:["SpecificViewingTime","SpecificHorizon","EquipmentRequirement"],transition,durations[slot.Order-1],p.MinimumSceneDurationSeconds,p.MaximumSceneDurationSeconds,Visual(slot),"");
            scene=scene with { DeterministicChecksum=DocumentaryIntentChecksum.Hash(scene with { DeterministicChecksum="" })}; scenes.Add(scene);
        }
        var coverage=Coverage(qs,scenes); var deferred=qs.Select(x=>x.QuestionId).Except(coverage.CoveredQuestions,StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var vi=new DocumentaryVariantIntent(p.Variant,DocumentaryIntentChecksum.Id($"dvi-{p.Variant.ToLowerInvariant()}-",Version,r.ExecutionId,r.PlanId,r.EventId,r.Profile.ProfileId,r.Profile.ProfileVersion,p.Variant),r.Profile.ProfileId,r.Profile.ProfileVersion,p.ExpectedSceneCount,p.DurationBudgetSeconds,scenes,coverage,coverage,deferred,scenes.SelectMany(x=>x.EditorialConstraints).Distinct().ToArray(),durations.Sum(),"");
        return vi with { DeterministicChecksum=DocumentaryIntentChecksum.Hash(vi with { DeterministicChecksum="" })};
    }
    private static int[] AllocateDurations(DocumentaryVariantProfile p, DocumentaryNarrativeSlot[] slots)
    {
        var result=Enumerable.Repeat(p.MinimumSceneDurationSeconds,slots.Length).ToArray(); var remaining=p.DurationBudgetSeconds-result.Sum();
        while(remaining>0){var eligible=slots.Select((s,i)=>(s,i)).Where(x=>result[x.i]<p.MaximumSceneDurationSeconds).OrderByDescending(x=>(decimal)x.s.DurationWeight/(result[x.i]-p.MinimumSceneDurationSeconds+1)).ThenBy(x=>x.s.Order).ThenBy(x=>x.s.SlotId,StringComparer.Ordinal).FirstOrDefault(); if(eligible.s is null) throw new PlanningException("DI_DURATION_ALLOCATION_FAILED","Duration maximums cannot satisfy budget."); result[eligible.i]++;remaining--;}
        return result;
    }
    private static DocumentaryCoverageSummary Coverage(IEnumerable<CuriosityViewerQuestion> qs,IEnumerable<DocumentarySceneOpportunity> scenes){var a=scenes.ToArray();var primary=a.Select(x=>x.PrimaryViewerQuestionId).ToArray();var supporting=a.SelectMany(x=>x.SupportingViewerQuestionIds).ToArray();var covered=primary.Concat(supporting).Distinct().Order(StringComparer.Ordinal).ToArray();return new(covered,qs.Where(x=>x.RequiresEditorialAttention).Select(x=>x.QuestionId).Where(x=>covered.Contains(x,StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray(),qs.Select(x=>x.QuestionId).Except(covered,StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),primary.GroupBy(x=>x,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key).Order(StringComparer.Ordinal).ToArray(),supporting.Distinct().Order(StringComparer.Ordinal).ToArray(),a.SelectMany(x=>x.SelectedKnowledgeReferences).Select(x=>x.KnowledgeReferenceId).Distinct().Order(StringComparer.Ordinal).ToArray());}
    private static List<DocumentaryPlanningIssue> ValidateInput(DocumentaryIntentPlanningRequest r){var e=new List<DocumentaryPlanningIssue>();if(r.Profile.ProfileId!=r.SourceLineage.ProfileId||r.Profile.ProfileVersion!=r.SourceLineage.ProfileVersion)e.Add(new("DI_PROFILE_INVALID","Profile and lineage differ."));if(!string.Equals(r.Language,r.QuestionBank.Metadata.Language,StringComparison.OrdinalIgnoreCase)||!string.Equals(r.Language,r.SourceLineage.Language,StringComparison.OrdinalIgnoreCase))e.Add(new("DI_LANGUAGE_MISMATCH","Upstream languages differ."));if(r.QuestionBank.Questions.Select(x=>x.QuestionId).Distinct(StringComparer.Ordinal).Count()!=r.QuestionBank.Questions.Count)e.Add(new("DI_QUESTION_BANK_INVALID","Question IDs must be unique."));if(r.CertifiedKnowledge.Any(x=>x.SourceArtifact.Contains("compatibility/",StringComparison.OrdinalIgnoreCase)))e.Add(new("DI_CERTIFIED_KNOWLEDGE_REFERENCE_INVALID","Compatibility artifacts cannot be authoritative."));return e;}
    private static int Priority(string p)=>p switch{"High"=>0,"Medium"=>1,"Normal"=>2,_=>3};
    private static string Visual(DocumentaryNarrativeSlot s)=>s.NarrativeStage.Contains("Clos",StringComparison.OrdinalIgnoreCase)?"ClosingReflection":s.SceneRole.Contains("Science",StringComparison.OrdinalIgnoreCase)?"ScientificExplanation":"Orientation";
    private static DocumentaryIntentPlanningResult Failed(DocumentaryIntentPlanningRequest r,IReadOnlyList<DocumentaryPlanningIssue> e)=>new(false,null,e,[],"Resolved:"+r.Profile.ProfileId,null,null,null,[]);
    private sealed class PlanningException(string code,string message):Exception(message){public string Code{get;}=code;}
}
