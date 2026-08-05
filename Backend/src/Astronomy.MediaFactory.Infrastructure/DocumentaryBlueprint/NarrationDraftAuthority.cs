using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7NarrationDraftInputAuthorityEvaluator(
    IPhase7NarrationPlanningCommittedStateEvaluator committedEvaluator, IPhase7KnowledgeCommittedStateEvaluator knowledgeEvaluator,
    IFamilyNarrationProfileResolver profiles)
    : IPhase7NarrationDraftInputAuthorityEvaluator
{
    public async Task<Phase7NarrationDraftInputAuthorityEvaluation> EvaluateAsync(Phase7NarrationDraftInputAuthorityRequest request,CancellationToken token=default)
    {
        if(request?.PlanningRequest is null)return Fail(NarrationDraftReasonCodes.PlanningMissing,["Planning request is required."],[]);
        if(request.CommittedClaimAuthorityRequest is null)return Fail(NarrationDraftReasonCodes.ClaimAuthorityMissing,["Committed claim authority coordinates are required."],[]);
        var result=await committedEvaluator.EvaluateAsync(request.PlanningRequest,token);
        if(!result.IsValid||result.Authority is null||result.ReasonCode!=NarrationPlanningPublicationReasonCodes.ReuseValid)
            return Fail(result.Authority is null?NarrationDraftReasonCodes.PlanningMissing:NarrationDraftReasonCodes.PlanningInvalid,result.Errors,result.Warnings);
        var p=result.Authority;var a=p.Authority;var r=request.PlanningRequest;
        if(a.DeterministicChecksum!=NarrationPlanningCanonicalizer.AuthorityChecksum(a)||
           p.Diagnostics.DeterministicChecksum!=NarrationPlanningCanonicalizer.DiagnosticsChecksum(p.Diagnostics)||
           a.Diagnostics.DeterministicChecksum!=NarrationPlanningCanonicalizer.DiagnosticsChecksum(a.Diagnostics)||
           p.Diagnostics.DeterministicChecksum!=a.Diagnostics.DeterministicChecksum||
           p.Report.DeterministicChecksum!=NarrationPlanningPublicationCanonicalizer.ComputeReportChecksum(p.Report)||
           p.Validation.DeterministicChecksum!=NarrationPlanningPublicationCanonicalizer.ComputePhysicalValidationChecksum(p.Validation)||
           p.ManifestEntry.DeterministicChecksum!=NarrationPlanningPublicationCanonicalizer.ComputeManifestEntryChecksum(p.ManifestEntry)||
           p.PublicationEvidence.DeterministicChecksum!=NarrationPlanningPublicationCanonicalizer.ComputePublicationEvidenceChecksum(p.PublicationEvidence)||
           p.Report.AuthorityId!=a.AuthorityId||p.Report.AuthorityChecksum!=a.DeterministicChecksum||
           p.Validation.AuthorityId!=a.AuthorityId||p.Validation.AuthorityChecksum!=a.DeterministicChecksum||
           p.ManifestEntry.DiagnosticsChecksum!=p.Diagnostics.DeterministicChecksum||p.ManifestEntry.ReportChecksum!=p.Report.DeterministicChecksum||p.ManifestEntry.ValidationChecksum!=p.Validation.DeterministicChecksum||
           p.PublicationEvidence.ValidationChecksum!=p.Validation.DeterministicChecksum||p.PublicationEvidence.ManifestEntryChecksum!=p.ManifestEntry.DeterministicChecksum||
           !p.Validation.PhysicalReadbackPassed||!p.Validation.CommittedStatePassed||p.Validation.GateResults.Any(x=>!x.Passed)||
           p.ManifestEntry.AuthorityChecksum!=a.DeterministicChecksum||p.PublicationEvidence.AuthorityChecksum!=a.DeterministicChecksum||
           p.ManifestEntry.PublicationStatus!="Committed"||!p.PublicationEvidence.CommittedPhysical)
            return Fail(NarrationDraftReasonCodes.PlanningInvalid,["Committed planning checksums, gates, manifest, or evidence are invalid."],result.Warnings);
        if(a.ExecutionId!=r.ExecutionId||a.PlanId!=r.PlanId||a.EventId!=r.EventId)
            return Fail(NarrationDraftReasonCodes.LineageStale,["Committed planning identity is stale."],result.Warnings);
        if(a.ProfileId!=r.ProfileId||a.ProfileVersion!=r.ProfileVersion)
            return Fail(NarrationDraftReasonCodes.ProfileMismatch,["Planning profile does not match the request."],result.Warnings);
        if(!GovernedNarrationLanguage.TryNormalize(a.Language,out var normalized)||
           !GovernedNarrationLanguage.TryNormalize(r.Language,out var requestedLanguage)||normalized!=requestedLanguage)
            return Fail(NarrationDraftReasonCodes.LanguageMismatch,["Planning language does not match the request."],result.Warnings);
        var matches=profiles.Profiles.Where(x=>x.ProfileId==a.ProfileId&&x.ContractVersion==a.ProfileVersion).ToArray();
        if(matches.Length!=1) return Fail(NarrationDraftReasonCodes.ProfileMismatch,[matches.Length==0?"The committed family profile is unavailable.":"The committed family profile identity is duplicated."],result.Warnings);
        var profile=matches[0];
        if(a.RuntimeCompatibilityEvidence.Count==0||a.RuntimeCompatibilityEvidence.Any(x=>string.IsNullOrWhiteSpace(x.Value)))
            return Fail(NarrationDraftReasonCodes.RuntimeIncompatible,["Runtime compatibility evidence is incomplete."],result.Warnings);
        var knowledge=await knowledgeEvaluator.EvaluateAsync(request.CommittedClaimAuthorityRequest,token);
        if(!knowledge.IsValid||knowledge.Authority is null)return Fail(NarrationDraftReasonCodes.ClaimAuthorityInvalid,
            ["Committed claim authority could not be validated.",..knowledge.Errors],result.Warnings.Concat(knowledge.Warnings).ToArray());
        if(knowledge.ReasonCode!="P7KNOWLEDGE_VALID")return Fail(NarrationDraftReasonCodes.UnsupportedKnowledgeResult,
            ["Committed knowledge evaluation returned an unsupported governed result."],MergeWarnings(result.Warnings,knowledge.Warnings));
        var publishedKnowledge=knowledge.Authority;var ka=publishedKnowledge.KnowledgeAuthority;
        if(ka.ExecutionId!=a.ExecutionId||ka.PlanId!=a.PlanId||ka.EventId!=a.EventId||
           ka.SemanticChecksum!=a.KnowledgeAuthorityChecksum||
           !a.Phase4To7Lineage.TryGetValue("phase7KnowledgeAuthorityId",out var knowledgeId)||knowledgeId!=ka.AuthorityId)
            return Fail(NarrationDraftReasonCodes.ClaimLineageStale,["Committed claim authority does not match planning lineage."],result.Warnings);
        var expectedLineage=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["phase4AggregateId"]=ka.SourcePhase4AggregateId,["phase4Checksum"]=ka.SourcePhase4Checksum,
            ["phase5PublicationId"]=ka.SourcePhase5PublicationId,["phase6AuthorityId"]=ka.SourcePhase6AuthorityId,
            ["phase6AuthorityChecksum"]=ka.SourcePhase6AuthorityChecksum,["phase7KnowledgeAuthorityId"]=ka.AuthorityId,
            ["phase7KnowledgeAuthorityChecksum"]=ka.SemanticChecksum
        };
        var stale=expectedLineage.FirstOrDefault(x=>!a.Phase4To7Lineage.TryGetValue(x.Key,out var value)||value!=x.Value);
        if(!string.IsNullOrEmpty(stale.Key))return Fail(NarrationDraftReasonCodes.ClaimLineageStale,[$"Committed lineage component '{stale.Key}' is missing or stale."],MergeWarnings(result.Warnings,knowledge.Warnings));
        if(ka.RuntimeCompatibilityEvidence.Any(x=>!a.RuntimeCompatibilityEvidence.TryGetValue(x.Key,out var value)||value!=x.Value))
            return Fail(NarrationDraftReasonCodes.RuntimeIncompatible,["Committed runtime evidence does not match the knowledge runtime."],MergeWarnings(result.Warnings,knowledge.Warnings));
        if(!GovernedNarrationLanguage.TryNormalize(ka.Language,out var claimLanguage)||claimLanguage!=normalized)
            return Fail(NarrationDraftReasonCodes.LanguageMismatch,["Committed claim authority language differs from planning."],result.Warnings);
        if(ka.Claims.Any(c=>c.Checksum!=Phase7Determinism.Hash(c with{Checksum=""})))
            return Fail(NarrationDraftReasonCodes.ClaimChecksumMismatch,["A committed certified claim checksum is invalid."],result.Warnings);
        if(ka.Claims.GroupBy(c=>c.ClaimId,StringComparer.Ordinal).Any(g=>g.Count()!=1))
            return Fail(NarrationDraftReasonCodes.ClaimAuthorityInvalid,["Committed claim identities must be unique."],result.Warnings);
        var catalog=ka.Claims.ToDictionary(c=>c.ClaimId,StringComparer.Ordinal);
        foreach(var scene in a.LongScenes.Concat(a.ShortScenes))
        foreach(var item in scene.RequiredClaims.Select(id=>(id,Phase7ClaimDisposition.Required))
            .Concat(scene.OptionalClaims.Select(id=>(id,Phase7ClaimDisposition.Optional)))
            .Concat(scene.DeferredClaims.Select(id=>(id,Phase7ClaimDisposition.Deferred))))
        { if(!catalog.TryGetValue(item.id,out var claim))return Fail(NarrationDraftReasonCodes.UnknownPlanningClaim,[$"Planning references unknown committed claim '{item.id}'."],result.Warnings);
          if(claim.Disposition!=item.Item2)return Fail(NarrationDraftReasonCodes.ClaimPartitionMismatch,[$"Claim '{item.id}' has a different committed partition."],result.Warnings);
          if(!GovernedNarrationLanguage.TryNormalize(claim.Language,out var cl)||cl!=normalized||string.IsNullOrWhiteSpace(claim.Text))return Fail(NarrationDraftReasonCodes.CertifiedLanguageClaimMissing,[$"Claim '{item.id}' has no certified text for '{normalized}'."],result.Warnings); }
        var input=new Phase7NarrationDraftInputAuthority(p,a,p.Diagnostics,p.Report,p.Validation,p.ManifestEntry,p.PublicationEvidence,
            profile,a.ExecutionId,a.PlanId,a.EventId,a.Language,a.ProfileId,a.ProfileVersion,a.Phase4To7Lineage,a.RuntimeCompatibilityEvidence)
            {CertifiedClaims=ka.Claims,CommittedClaimAuthority=publishedKnowledge,Warnings=result.Warnings.Concat(knowledge.Warnings).Distinct().ToArray()};
        return new(true,input,NarrationDraftReasonCodes.InputValid,[],input.Warnings);
    }
    private static string[] MergeWarnings(params IEnumerable<string>[] sources)=>sources.SelectMany(x=>x).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static Phase7NarrationDraftInputAuthorityEvaluation Fail(string code,IReadOnlyList<string> errors,IReadOnlyList<string> warnings)=>new(false,null,code,errors,warnings){BlockingIssues=errors};
}

internal static class GovernedNarrationLanguage
{
    public static bool TryNormalize(string? value,out string normalized)
    { normalized=value?.Trim() switch { "en"=>"en", "en-IN"=>"en-IN", "hi"=>"hi", "hi-IN"=>"hi-IN", _=>"" };return normalized.Length>0; }
    public static bool IsHindi(string value)=>TryNormalize(value,out var normalized)&&normalized.StartsWith("hi",StringComparison.Ordinal);
}

public static class NarrationDraftCanonicalizer
{
    public static string ComputeSentenceId(NarrationDraftSentence x)=>$"sentence-{x.Ordinal.ToString(CultureInfo.InvariantCulture)}-{ComputeSentenceChecksum(x)[..20]}";
    public static string ComputeSentenceChecksum(NarrationDraftSentence x)=>Phase7Determinism.Hash(x with{SentenceId="",ClaimIds=Sort(x.ClaimIds),KnowledgeReferenceIds=Sort(x.KnowledgeReferenceIds),QualificationIds=Sort(x.QualificationIds),SafetyRuleIds=Sort(x.SafetyRuleIds),DeterministicChecksum=""});
    public static string ComputeSceneId(NarrationDraftScene x)=>$"draft-{x.Variant.ToLowerInvariant()}-{ComputeSceneChecksum(x)[..20]}";
    public static string ComputeSceneChecksum(NarrationDraftScene x)=>Phase7Determinism.Hash(x with{DraftSceneId="",RequiredClaimUsage=SortUsage(x.RequiredClaimUsage),OptionalClaimUsage=SortUsage(x.OptionalClaimUsage),DeferredClaimIds=Sort(x.DeferredClaimIds),AppliedQualifications=Sort(x.AppliedQualifications),AppliedSafetyRules=Sort(x.AppliedSafetyRules),AppliedEditorialConstraints=Sort(x.AppliedEditorialConstraints),DeterministicChecksum=""});
    public static string ComputeDiagnosticsChecksum(NarrationDraftDiagnostics x)=>Phase7Determinism.Hash(x with{Warnings=Sort(x.Warnings),Errors=Sort(x.Errors),DeterministicChecksum=""});
    public static string ComputeAuthorityId(NarrationDraftAuthority x)=>$"narration-draft-{ComputeAuthorityChecksum(x)[..20]}";
    public static string ComputeAuthorityChecksum(NarrationDraftAuthority x)=>Phase7Determinism.Hash(x with{AuthorityId="",Phase4To7Lineage=Sort(x.Phase4To7Lineage),RuntimeCompatibilityEvidence=Sort(x.RuntimeCompatibilityEvidence),DeterministicChecksum=""});
    public static string ComputeValidationChecksum(NarrationDraftValidation x)=>Phase7Determinism.Hash(x with{DeterministicChecksum=""});
    public static string ComputeClaimUsageChecksum(NarrationDraftClaimUsage x)=>Phase7Determinism.Hash(x with{QualificationIds=Sort(x.QualificationIds),DeterministicChecksum=""});
    public static string ComputeTransitionPhraseChecksum(NarrationDraftTransitionPhrase x)=>Phase7Determinism.Hash(x with{PlanningTransitionIds=Sort(x.PlanningTransitionIds),DeterministicChecksum=""});
    private static string[] Sort(IEnumerable<string> x)=>x.Order(StringComparer.Ordinal).ToArray();
    private static NarrationDraftClaimUsage[] SortUsage(IEnumerable<NarrationDraftClaimUsage>x)=>x.OrderBy(y=>y.ClaimId,StringComparer.Ordinal).ToArray();
    private static SortedDictionary<string,string> Sort(IReadOnlyDictionary<string,string>x){var result=new SortedDictionary<string,string>(StringComparer.Ordinal);foreach(var pair in x)result[pair.Key]=pair.Value;return result;}
}

public sealed partial class DeterministicNarrationDraftLanguagePolicy:INarrationDraftLanguagePolicy
{
    public bool Supports(string x)=>GovernedNarrationLanguage.TryNormalize(x,out _);
    public string Terminate(string text,string language){text=text.Trim();if(text.Length==0)return text;return text.EndsWith('.')||text.EndsWith('?')||text.EndsWith('!')||text.EndsWith('।')?text:text+(GovernedNarrationLanguage.IsHindi(language)?"।":".");}
    public string OpeningBridge(string q,string objective,string language)=>Terminate(!string.IsNullOrWhiteSpace(q)?q:objective,language);
    public string Conjunction(string language)=>GovernedNarrationLanguage.IsHindi(language)?" और ":" and ";
    public decimal EstimateReadingTime(string text,string language)=>Math.Round(Count(text)/(GovernedNarrationLanguage.IsHindi(language)?130m:150m)*60m,3);
    public bool PreservesProtectedTokens(string certified,string realized)=>Protected().Matches(certified).Select(x=>x.Value).All(x=>realized.Contains(x,StringComparison.Ordinal));
    internal static int Count(string x)=>x.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length;
    [GeneratedRegex(@"\b(?:\d+(?:\.\d+)?|[A-Z][\p{L}\p{M}'-]*)(?:\s*(?:km|m|cm|AU|°|degrees?|hours?|minutes?|seconds?|north|south|east|west))?\b",RegexOptions.CultureInvariant)]private static partial Regex Protected();
}
public sealed class DeterministicNarrationDraftTimingPolicy:INarrationDraftTimingPolicy
{
    public const string PolicyId = "NarrationPlanningAuthority.SceneConstraints";
    public const string PolicyVersion = NarrationPlanningContract.Version;
    public NarrationDraftSceneTimingDecision Resolve(NarrationDraftTimingPolicyRequest r) => new(r.Variant,r.SectionKey,r.MinimumSentenceCount,r.MaximumSentenceCount,r.MinimumDurationSeconds,r.MaximumDurationSeconds,PolicyId,PolicyVersion);
    public NarrationDraftTimingBudget Budget(NarrationDraftTimingPolicyRequest r){var wpm=GovernedNarrationLanguage.IsHindi(r.Language)?130m:150m;var min=(int)Math.Floor(r.MinimumDurationSeconds*wpm/60m);var max=(int)Math.Ceiling(r.MaximumDurationSeconds*wpm/60m);var target=(int)Math.Round(r.TargetDurationSeconds*wpm/60m);return new(target,min,max,r.TargetDurationSeconds,1m,Math.Max(0,r.MaximumSentenceCount-r.RequiredClaimCount-r.MandatoryStructuralSentenceCount),wpm);}
}
public sealed class DeterministicNarrationDraftRealizationPolicy(INarrationDraftLanguagePolicy language):INarrationDraftRealizationPolicy
{
    // The only transformations are whitespace trimming, governed qualification prefix insertion, and terminal punctuation.
    public string Realize(CertifiedNarrationClaim c,IReadOnlyList<string> q,string l)=>language.Terminate(string.Join(" ",q.Append(c.Text)).Trim(),l);
}
internal static class NarrationDraftFactualRealization
{
    internal static string Expected(string certified,IReadOnlyList<string> qualifications,string language,INarrationDraftLanguagePolicy policy)=>
        policy.Terminate(string.Join(" ",qualifications.Append(certified.Trim())).Trim(),language);
}
public sealed class ConservativeNarrationDraftClaimCoalescingPolicy:INarrationDraftClaimCoalescingPolicy{public bool CanCoalesce(NarrationPlanningScene s,CertifiedNarrationClaim a,CertifiedNarrationClaim b,int m)=>false;}
public sealed class DeterministicNarrationDraftOpeningPolicy(INarrationDraftLanguagePolicy language):INarrationDraftOpeningPolicy{public string Create(NarrationPlanningScene s,string l)=>language.OpeningBridge(s.ViewerQuestion,s.LearningObjective,l);}
public sealed class DeterministicNarrationDraftClosingPolicy(INarrationDraftLanguagePolicy language):INarrationDraftClosingPolicy{public string Create(NarrationPlanningScene s,bool next,string l)=>string.IsNullOrWhiteSpace(s.LearningObjective)?"":language.Terminate(s.LearningObjective,l);}
public sealed class DeterministicNarrationDraftTransitionPhrasePolicy(INarrationDraftLanguagePolicy language):INarrationDraftTransitionPhrasePolicy
{
    public NarrationDraftTransitionPhrase? Create(NarrationDraftTransitionPhraseRequest r){var t=r.Transition;var text=r.Ownership==NarrationDraftTransitionOwnership.IncomingDestination?t.DestinationTransitionIn:t.SourceTransitionOut;if(string.IsNullOrWhiteSpace(text)||t.Variant!=r.Variant)return null;var d=new NarrationDraftTransitionPhrase(t.TransitionId,t.Kind,language.Terminate(text,r.Language),t.Variant,[t.TransitionId],"");return d with{DeterministicChecksum=NarrationDraftCanonicalizer.ComputeTransitionPhraseChecksum(d)};}
}
public sealed class NarrationDraftSafetyValidator:INarrationDraftSafetyValidator
{
    public IReadOnlyList<string> Validate(NarrationPlanningScene p,NarrationDraftScene d,IReadOnlyDictionary<string,CertifiedNarrationClaim> claims)
    {var errors=new List<string>();var text=string.Join(" ",d.Sentences.Select(x=>x.Text));foreach(var prohibited in p.ForbiddenStatements.Where(x=>!string.IsNullOrWhiteSpace(x)))if(text.Contains(prohibited,StringComparison.OrdinalIgnoreCase))errors.Add($"Prohibited statement used: {prohibited}");
     foreach(var u in d.RequiredClaimUsage.Concat(d.OptionalClaimUsage)){if(!claims.TryGetValue(u.ClaimId,out var c)){errors.Add($"Unknown claim: {u.ClaimId}");continue;}if(c.RequiresHumanReview)errors.Add($"Human-review claim used: {u.ClaimId}");if(c.RequiresQualification&&u.QualificationIds.Count==0)errors.Add($"Qualification omitted: {u.ClaimId}");}
     foreach(var id in d.DeferredClaimIds)if(d.RequiredClaimUsage.Concat(d.OptionalClaimUsage).Any(x=>x.ClaimId==id)||d.Sentences.Any(x=>x.ClaimIds.Contains(id)))errors.Add($"Deferred claim used: {id}");return errors;}
}

public sealed class NarrationDraftAuthorityBuilder(INarrationDraftLanguagePolicy language,INarrationDraftTimingPolicy timing,
    INarrationDraftRealizationPolicy realization,INarrationDraftOpeningPolicy openings,INarrationDraftClosingPolicy closings,
    INarrationDraftTransitionPhrasePolicy transitions,INarrationDraftSafetyValidator safety):INarrationDraftAuthorityBuilder
{
    public NarrationDraftAuthorityBuildResult Build(Phase7NarrationDraftInputAuthority input)
    {
        if(input is null||input.NarrationPlanningAuthority is null)return Fail(NarrationDraftReasonCodes.InputInvalid,"Draft input is incomplete.");
        var planning=input.NarrationPlanningAuthority;
        if(planning.DeterministicChecksum!=NarrationPlanningCanonicalizer.AuthorityChecksum(planning))return Fail(NarrationDraftReasonCodes.InputInvalid,"Planning authority checksum is invalid.");
        if(!language.Supports(input.Language))return Fail(NarrationDraftReasonCodes.LanguageInvalid,"Only governed English and Hindi policies are available.");
        var duplicate=input.CertifiedClaims.GroupBy(x=>x.ClaimId,StringComparer.Ordinal).FirstOrDefault(x=>x.Count()!=1);
        if(duplicate is not null)return Fail(NarrationDraftReasonCodes.RequiredDuplicated,$"Certified claim identity is duplicated: {duplicate.Key}");
        var claims=input.CertifiedClaims.ToDictionary(x=>x.ClaimId,StringComparer.Ordinal);
        var longResult=BuildVariant(planning.LongScenes,"Long",claims,input.Language);if(longResult.Error is not null)return longResult.Error;
        var shortResult=BuildVariant(planning.ShortScenes,"Short",claims,input.Language);if(shortResult.Error is not null)return shortResult.Error;
        var scenes=longResult.Scenes!.Concat(shortResult.Scenes!).ToArray();var warnings=input.Warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var dd=new NarrationDraftDiagnostics(planning.LongScenes.Count+planning.ShortScenes.Count,scenes.Length,longResult.Scenes.Count,shortResult.Scenes.Count,
            scenes.Sum(x=>x.SentenceCount),scenes.Sum(x=>x.RequiredClaimUsage.Count),scenes.Sum(x=>x.RequiredClaimUsage.Count),
            planning.LongScenes.Concat(planning.ShortScenes).Sum(x=>x.OptionalClaims.Count),scenes.Sum(x=>x.OptionalClaimUsage.Count),
            scenes.Sum(x=>x.DeferredClaimIds.Count),0,scenes.Sum(x=>x.RequiredClaimUsage.Concat(x.OptionalClaimUsage).Count(u=>u.QualificationIds.Count>0)),
            scenes.Sum(x=>x.Sentences.Count(y=>y.IsTransition)),scenes.Sum(x=>x.WordCount),scenes.Sum(x=>x.EstimatedReadingTimeSeconds),0,0,warnings.Length,0,warnings,[],"");
        var diagnostics=dd with{DeterministicChecksum=NarrationDraftCanonicalizer.ComputeDiagnosticsChecksum(dd)};
        var draft=new NarrationDraftAuthority(NarrationDraftContract.Version,"",input.ExecutionId,input.PlanId,input.EventId,input.Language,input.ProfileId,input.ProfileVersion,
            planning.AuthorityId,planning.DeterministicChecksum,longResult.Scenes,shortResult.Scenes,diagnostics,input.Phase4To7Lineage,input.RuntimeCompatibilityEvidence,"");
        draft=draft with{AuthorityId=NarrationDraftCanonicalizer.ComputeAuthorityId(draft)};draft=draft with{DeterministicChecksum=NarrationDraftCanonicalizer.ComputeAuthorityChecksum(draft)};
        return new(true,draft,NarrationDraftReasonCodes.AuthorityValid,[],warnings,[]);
    }
    private (IReadOnlyList<NarrationDraftScene>? Scenes,NarrationDraftAuthorityBuildResult? Error) BuildVariant(IReadOnlyList<NarrationPlanningScene> source,string variant,IReadOnlyDictionary<string,CertifiedNarrationClaim> claims,string lang)
    {var result=new List<NarrationDraftScene>();for(var i=0;i<source.Count;i++){var built=BuildScene(source[i],i+1<source.Count,claims,lang);if(built.Error is not null)return(null,built.Error);result.Add(built.Scene!);}return(result,null);}
    private (NarrationDraftScene? Scene,NarrationDraftAuthorityBuildResult? Error) BuildScene(NarrationPlanningScene p,bool hasNext,IReadOnlyDictionary<string,CertifiedNarrationClaim> catalog,string lang)
    {
        if(p.DeterministicChecksum!=NarrationPlanningCanonicalizer.SceneChecksum(p))return(null,Fail(NarrationDraftReasonCodes.SceneInvalid,$"Planning scene checksum is invalid: {p.PlanningId}"));
        var required=new List<CertifiedNarrationClaim>();foreach(var id in p.RequiredClaims){if(!catalog.TryGetValue(id,out var c))return(null,Fail(NarrationDraftReasonCodes.RequiredMissing,$"Certified Required claim is missing: {id}"));required.Add(c);}
        if(required.Any(x=>x.RequiresHumanReview))return(null,Fail(NarrationDraftReasonCodes.HumanReviewInvalid,"A Required factual claim requires human review."));
        if(required.Any(x=>!string.Equals(x.Language,lang,StringComparison.OrdinalIgnoreCase)))return(null,Fail(NarrationDraftReasonCodes.CertifiedLanguageClaimMissing,"A Required claim has no certified text in the planning language."));
        var incoming=transitions.Create(new(p.IncomingTransition,NarrationDraftTransitionOwnership.IncomingDestination,p.Variant,lang));
        var outgoing=transitions.Create(new(p.OutgoingTransition,NarrationDraftTransitionOwnership.OutgoingSource,p.Variant,lang));
        var openingCandidate=openings.Create(p,lang);
        var closingCandidate=closings.Create(p,hasNext,lang);
        var mandatoryTransitionCount=(incoming is null?0:1)+(outgoing is null?0:1);
        var minimumMandatorySentenceCount=required.Count+mandatoryTransitionCount;
        if(minimumMandatorySentenceCount>p.NarrationConstraints.MaximumSentenceCount)
            return(null,Fail(NarrationDraftReasonCodes.RequiredContentExceedsTimingCapacity,$"Required claims and owned transitions exceed the governed maximum: planningId={p.PlanningId};variant={p.Variant};required={required.Count};transitions={mandatoryTransitionCount};maximum={p.NarrationConstraints.MaximumSentenceCount}."));
        var availableStructuralCapacity=p.NarrationConstraints.MaximumSentenceCount-minimumMandatorySentenceCount;
        var includeOpening=!string.IsNullOrWhiteSpace(openingCandidate)&&availableStructuralCapacity>0;
        if(includeOpening)availableStructuralCapacity--;
        var includeClosing=!string.IsNullOrWhiteSpace(closingCandidate)&&availableStructuralCapacity>0;
        var opening=includeOpening?openingCandidate:"";
        var closing=includeClosing?closingCandidate:"";
        var structural=mandatoryTransitionCount+(includeOpening?1:0)+(includeClosing?1:0);
        var timingRequest=new NarrationDraftTimingPolicyRequest(lang,p.Variant,p.NarrativeGoal.SectionKey,p.MinimumDuration,p.ExpectedDuration,p.MaximumDuration,p.NarrationConstraints.PreferredSentenceCount,p.NarrationConstraints.MinimumSentenceCount,p.NarrationConstraints.MaximumSentenceCount,required.Count,p.OptionalClaims.Count,p.NarrationConstraints.PauseStrategy,structural);
        var decision=timing.Resolve(timingRequest);
        var budget=timing.Budget(timingRequest);
        var requiredWords=required.Sum(c=>DeterministicNarrationDraftLanguagePolicy.Count(realization.Realize(c,Qualifications(p,c),lang)));
        if(minimumMandatorySentenceCount>decision.MaximumSentenceCount||requiredWords>budget.MaximumWords)
            return(null,Fail(NarrationDraftReasonCodes.RequiredContentExceedsTimingCapacity,"Required factual realization exceeds the governed maximum."));
        var sentences=new List<NarrationDraftSentence>();
        if(incoming is not null)AddSentence(incoming.Text,"IncomingTransition",[],[],false,false,true,[]);
        if(!string.IsNullOrWhiteSpace(opening))AddSentence(opening,"Opening",[],[],false,false,false,p.VisualSynchronizationTargets);
        var usages=new List<NarrationDraftClaimUsage>();
        foreach(var c in required){var q=Qualifications(p,c);if(c.RequiresQualification&&q.Count==0)return(null,Fail(NarrationDraftReasonCodes.QualificationMissing,$"Qualification authority is missing: {c.ClaimId}"));var text=realization.Realize(c,q,lang);if(!ExactRealization(c.Text,q,text,lang))return(null,Fail(NarrationDraftReasonCodes.SafetyInvalid,$"Certified claim realization changed: {c.ClaimId}"));var s=AddSentence(text,"RequiredClaim",[c.ClaimId],q,true,false,false,c.KnowledgeReferenceIds);usages.Add(Usage(c.ClaimId,"Required",s,q));}
        var optionalUsages=new List<NarrationDraftClaimUsage>();
        foreach(var id in p.OptionalClaims.Order(StringComparer.Ordinal))
        {
            var remainingStructuralAfterOption=(includeClosing?1:0)+(outgoing is null?0:1);
            if(sentences.Count+remainingStructuralAfterOption>=decision.MaximumSentenceCount)break;
            if(optionalUsages.Count>=budget.PermittedOptionalClaimCapacity)break;
            if(!catalog.TryGetValue(id,out var c)||c.RequiresHumanReview||!SameLanguage(c.Language,lang))continue;
            var q=Qualifications(p,c);if(c.RequiresQualification&&q.Count==0)continue;
            var text=realization.Realize(c,q,lang);if(!ExactRealization(c.Text,q,text,lang))continue;
            var projectedWords=sentences.Sum(x=>DeterministicNarrationDraftLanguagePolicy.Count(x.Text))+DeterministicNarrationDraftLanguagePolicy.Count(text)+(includeClosing?DeterministicNarrationDraftLanguagePolicy.Count(closing):0)+(outgoing is null?0:DeterministicNarrationDraftLanguagePolicy.Count(outgoing.Text));
            if(projectedWords>budget.MaximumWords)continue;
            var s=AddSentence(text,"OptionalClaim",[id],q,false,true,false,c.KnowledgeReferenceIds);optionalUsages.Add(Usage(id,"Optional",s,q));
        }
        if(!string.IsNullOrWhiteSpace(closing))AddSentence(closing,"Closing",[],[],false,false,false,p.VisualSynchronizationTargets);
        if(outgoing is not null)AddSentence(outgoing.Text,"OutgoingTransition",[],[],false,false,true,[]);
        var governed=GovernedSentences(sentences);
        var words=governed.Sum(x=>DeterministicNarrationDraftLanguagePolicy.Count(x.Text));var read=governed.Sum(x=>x.EstimatedDurationSeconds);
        if(governed.Count<decision.MinimumSentenceCount)return(null,TimingFail(NarrationDraftReasonCodes.SceneSentenceCountBelowMinimum,"SceneSentenceRangeGate",p,governed,decision,read,usages,optionalUsages,$"Scene {p.Variant}/{p.SceneId} has {governed.Count} governed sentences; minimum is {decision.MinimumSentenceCount}."));
        if(governed.Count>decision.MaximumSentenceCount)return(null,TimingFail(NarrationDraftReasonCodes.SceneSentenceCountAboveMaximum,"SceneSentenceRangeGate",p,governed,decision,read,usages,optionalUsages,$"Scene {p.Variant}/{p.SceneId} has {governed.Count} governed sentences; maximum is {decision.MaximumSentenceCount}."));
        if(read<decision.MinimumDurationSeconds)return(null,TimingFail(NarrationDraftReasonCodes.SceneDurationBelowMinimum,"SceneDurationRangeGate",p,governed,decision,read,usages,optionalUsages,$"Scene {p.Variant}/{p.SceneId} duration {read} is below minimum {decision.MinimumDurationSeconds}."));
        if(read>decision.MaximumDurationSeconds)return(null,TimingFail(NarrationDraftReasonCodes.SceneDurationAboveMaximum,"SceneDurationRangeGate",p,governed,decision,read,usages,optionalUsages,$"Scene {p.Variant}/{p.SceneId} duration {read} is above maximum {decision.MaximumDurationSeconds}."));
        var draft=new NarrationDraftScene("",p.PlanningId,p.SceneId,p.Variant,p.StoryFrameId,p.PacketId,p.DeterministicChecksum,p.ViewerQuestion,p.LearningObjective,opening,sentences,
            closing,incoming,outgoing,usages,optionalUsages,p.DeferredClaims,usages.Concat(optionalUsages).SelectMany(x=>x.QualificationIds).Distinct().ToArray(),p.SafetyRequirements,p.EditorialConstraints,words,governed.Count,read,p.MinimumDuration,p.ExpectedDuration,p.MaximumDuration,"");
        var errors=safety.Validate(p,draft,catalog);if(errors.Count>0)return(null,new(false,null,NarrationDraftReasonCodes.SafetyInvalid,errors,[],errors));
        draft=draft with{DraftSceneId=NarrationDraftCanonicalizer.ComputeSceneId(draft)};draft=draft with{DeterministicChecksum=NarrationDraftCanonicalizer.ComputeSceneChecksum(draft)};return(draft,null);
        NarrationDraftSentence AddSentence(string text,string role,IReadOnlyList<string> ids,IReadOnlyList<string> q,bool req,bool opt,bool transition,IReadOnlyList<string> refs){var d=language.EstimateReadingTime(text,lang);var x=new NarrationDraftSentence("",sentences.Count+1,text,role,ids,refs,q,p.SafetyRequirements,p.VisualSynchronizationTargets,req,opt,transition,d,"");x=x with{SentenceId=NarrationDraftCanonicalizer.ComputeSentenceId(x)};x=x with{DeterministicChecksum=NarrationDraftCanonicalizer.ComputeSentenceChecksum(x)};sentences.Add(x);return x;}
    }
    private static IReadOnlyList<NarrationDraftSentence> GovernedSentences(IReadOnlyList<NarrationDraftSentence> sentences)=>sentences.Where(x=>x.IsRequired||x.IsOptional||x.IsTransition).ToArray();
    private bool ExactRealization(string certified,IReadOnlyList<string> qualifications,string realized,string lang)=>
        realized==NarrationDraftFactualRealization.Expected(certified,qualifications,lang,language);
    private static bool SameLanguage(string a,string b)=>GovernedNarrationLanguage.TryNormalize(a,out var x)&&GovernedNarrationLanguage.TryNormalize(b,out var y)&&x==y;
    private static IReadOnlyList<string> Qualifications(NarrationPlanningScene p,CertifiedNarrationClaim c)=>
        (c.IsCultural?p.CulturalQualificationRequirements:[]).Concat(c.IsMythological?p.CulturalQualificationRequirements:[]).Concat(c.IsAstrologyRelated?p.AstrologyQualificationRequirements:[]).Concat(c.IsLocationDependent?p.LocationQualificationRequirements:[]).Concat(c.IsDateTimeDependent?p.TimeQualificationRequirements:[]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static NarrationDraftClaimUsage Usage(string id,string part,NarrationDraftSentence sentence,IReadOnlyList<string> q){var x=new NarrationDraftClaimUsage(id,part,sentence.SentenceId,"ExactCertifiedText",q,"");return x with{DeterministicChecksum=NarrationDraftCanonicalizer.ComputeClaimUsageChecksum(x)};}
    private static NarrationDraftAuthorityBuildResult Fail(string code,string error)=>new(false,null,code,[error],[],[error]);
    private static NarrationDraftAuthorityBuildResult TimingFail(string code,string gate,NarrationPlanningScene p,IReadOnlyList<NarrationDraftSentence> governed,NarrationDraftSceneTimingDecision d,decimal read,IReadOnlyList<NarrationDraftClaimUsage> required,IReadOnlyList<NarrationDraftClaimUsage> optional,string error)
    {
        var summary=new NarrationDraftSceneFailureSummary(p.Variant,0,p.SceneId,p.StoryFrameId,p.PlanningId,p.NarrativeGoal.SectionKey,governed.Count,d.MinimumSentenceCount,d.MaximumSentenceCount,read,d.MinimumDurationSeconds,d.MaximumDurationSeconds,p.RequiredClaims,required.Select(x=>x.ClaimId).ToArray(),optional.Select(x=>x.ClaimId).ToArray(),governed.Where(x=>x.IsTransition).Select(x=>x.SentenceId).ToArray(),[gate],[code]);
        return Fail(code,error) with { SceneFailureSummaries = [summary] };
    }
}

public sealed class NarrationDraftValidator(INarrationDraftSafetyValidator safety):INarrationDraftValidator
{
    public static readonly IReadOnlyList<string> GateNames=["ContractGate","InputAuthorityGate","ProfileGate","LanguageGate","PlanningCoverageGate","SceneCoverageGate","SceneLineageGate","SentenceIdentityGate","SentenceChecksumGate","RequiredClaimCoverageGate","RequiredClaimUniquenessGate","OptionalClaimGate","DeferredClaimExclusionGate","ClaimUsageGate","QualificationGate","SafetyGate","AstrologySeparationGate","CulturalContextGate","LocationTimeGate","HumanReviewGate","TransitionGate","SentenceBudgetGate","TimingGate","LongShortIndependenceGate","DiagnosticsGate","AuthorityChecksumGate","DeterminismGate"];
    public NarrationDraftValidation Validate(Phase7NarrationDraftInputAuthority input,NarrationDraftAuthority authority)
    {
        var p=input.NarrationPlanningAuthority;var ps=p.LongScenes.Concat(p.ShortScenes).ToArray();var ds=authority.LongScenes.Concat(authority.ShortScenes).ToArray();
        var map=ps.GroupBy(x=>x.PlanningId,StringComparer.Ordinal).Where(x=>x.Count()==1).ToDictionary(x=>x.Key,x=>x.First(),StringComparer.Ordinal);
        var claims=input.CertifiedClaims.GroupBy(x=>x.ClaimId).Where(x=>x.Count()==1).ToDictionary(x=>x.Key,x=>x.First(),StringComparer.Ordinal);
        bool ScenePairs(Func<NarrationPlanningScene,NarrationDraftScene,bool> f)=>ds.Length==ps.Length&&ds.All(d=>map.TryGetValue(d.PlanningId,out var x)&&f(x,d));
        var checks=new Dictionary<string,bool>(StringComparer.Ordinal)
        {
            [GateNames[0]]=authority.ContractVersion==NarrationDraftContract.Version,[GateNames[1]]=p.DeterministicChecksum==NarrationPlanningCanonicalizer.AuthorityChecksum(p),
            [GateNames[2]]=authority.ProfileId==input.ProfileId&&authority.ProfileVersion==input.ProfileVersion,[GateNames[3]]=authority.Language==input.Language,
            [GateNames[4]]=authority.PlanningAuthorityId==p.AuthorityId&&authority.PlanningAuthorityChecksum==p.DeterministicChecksum,[GateNames[5]]=ds.Length==ps.Length&&ds.Select(x=>x.PlanningId).SequenceEqual(ps.Select(x=>x.PlanningId)),
            [GateNames[6]]=ScenePairs((x,d)=>d.SceneId==x.SceneId&&d.StoryFrameId==x.StoryFrameId&&d.PacketId==x.PacketId&&d.PlanningChecksum==x.DeterministicChecksum&&d.Variant==x.Variant),
            [GateNames[7]]=ds.All(d=>d.SentenceCount>=map.GetValueOrDefault(d.PlanningId)?.NarrationConstraints.MinimumSentenceCount&&d.Sentences.Select((s,i)=>s.Ordinal==i+1).All(x=>x)&&d.Sentences.Select(x=>x.Ordinal).Distinct().Count()==d.Sentences.Count&&d.Sentences.Select(x=>x.SentenceId).Distinct(StringComparer.Ordinal).Count()==d.Sentences.Count&&d.Sentences.All(x=>x.SentenceId==NarrationDraftCanonicalizer.ComputeSentenceId(x)&&ValidFlags(x))),[GateNames[8]]=ds.SelectMany(x=>x.Sentences).All(x=>x.DeterministicChecksum==NarrationDraftCanonicalizer.ComputeSentenceChecksum(x)),
            [GateNames[9]]=ScenePairs((x,d)=>x.RequiredClaims.Order(StringComparer.Ordinal).SequenceEqual(d.RequiredClaimUsage.Select(y=>y.ClaimId).Order(StringComparer.Ordinal))),
            [GateNames[10]]=ds.All(d=>d.RequiredClaimUsage.Select(x=>x.ClaimId).Distinct(StringComparer.Ordinal).Count()==d.RequiredClaimUsage.Count),
            [GateNames[11]]=ScenePairs((x,d)=>d.OptionalClaimUsage.All(y=>x.OptionalClaims.Contains(y.ClaimId,StringComparer.Ordinal))),
            [GateNames[12]]=ScenePairs((x,d)=>d.Sentences.All(y=>!y.ClaimIds.Intersect(x.DeferredClaims,StringComparer.Ordinal).Any())&&!d.RequiredClaimUsage.Concat(d.OptionalClaimUsage).Any(y=>x.DeferredClaims.Contains(y.ClaimId,StringComparer.Ordinal))),
            [GateNames[13]]=ScenePairs((x,d)=>d.RequiredClaimUsage.Concat(d.OptionalClaimUsage).All(u=>ValidUsage(x,d,u))),
            [GateNames[14]]=ds.All(d=>d.RequiredClaimUsage.Concat(d.OptionalClaimUsage).All(u=>claims.TryGetValue(u.ClaimId,out var c)&&(!c.RequiresQualification||u.QualificationIds.Count>0))),
            [GateNames[15]]=ScenePairs((x,d)=>safety.Validate(x,d,claims).Count==0),[GateNames[16]]=Qualified(c=>c.IsAstrologyRelated),[GateNames[17]]=Qualified(c=>c.IsCultural||c.IsMythological),
            [GateNames[18]]=Qualified(c=>c.IsLocationDependent||c.IsDateTimeDependent),[GateNames[19]]=ds.SelectMany(x=>x.RequiredClaimUsage.Concat(x.OptionalClaimUsage)).All(u=>claims.TryGetValue(u.ClaimId,out var c)&&!c.RequiresHumanReview),
            [GateNames[20]]=ScenePairs(ValidTransitions),
            [GateNames[21]]=ScenePairs((x,d)=>d.SentenceCount==d.Sentences.Count(s=>s.IsRequired||s.IsOptional||s.IsTransition)&&d.SentenceCount>=x.NarrationConstraints.MinimumSentenceCount&&d.SentenceCount<=x.NarrationConstraints.MaximumSentenceCount),[GateNames[22]]=ds.All(d=>d.EstimatedReadingTimeSeconds>=d.MinimumDurationSeconds&&d.EstimatedReadingTimeSeconds<=d.MaximumDurationSeconds),
            [GateNames[23]]=Independent(authority),
            [GateNames[24]]=Diagnostics(authority.Diagnostics,ps,ds),[GateNames[25]]=authority.DeterministicChecksum==NarrationDraftCanonicalizer.ComputeAuthorityChecksum(authority)&&ds.All(x=>x.DeterministicChecksum==NarrationDraftCanonicalizer.ComputeSceneChecksum(x)),
            [GateNames[26]]=authority.AuthorityId==NarrationDraftCanonicalizer.ComputeAuthorityId(authority)
        };
        var gates=GateNames.Select(x=>new NarrationDraftValidationGate(x,checks[x],checks[x]?[]:[$"{x} failed."])).ToArray();var errors=gates.SelectMany(x=>x.Errors).ToArray();var v=new NarrationDraftValidation(errors.Length==0,errors.Length==0?NarrationDraftReasonCodes.AuthorityValid:"NARRATION_DRAFT_AUTHORITY_INVALID",gates,errors,input.Warnings,"");return v with{DeterministicChecksum=NarrationDraftCanonicalizer.ComputeValidationChecksum(v)};
        bool Qualified(Func<CertifiedNarrationClaim,bool> applies)=>ds.SelectMany(x=>x.RequiredClaimUsage.Concat(x.OptionalClaimUsage)).Where(u=>claims.TryGetValue(u.ClaimId,out var c)&&applies(c)).All(u=>u.QualificationIds.Count>0);
        bool ValidUsage(NarrationPlanningScene scene,NarrationDraftScene draft,NarrationDraftClaimUsage u)
        {
            if(!claims.TryGetValue(u.ClaimId,out var c)||u.UsageMode!="ExactCertifiedText"||u.DeterministicChecksum!=NarrationDraftCanonicalizer.ComputeClaimUsageChecksum(u))return false;
            var required=u.ClaimPartition=="Required"&&scene.RequiredClaims.Contains(u.ClaimId,StringComparer.Ordinal);
            var optional=u.ClaimPartition=="Optional"&&scene.OptionalClaims.Contains(u.ClaimId,StringComparer.Ordinal);
            if(!required&&!optional)return false;
            var matches=draft.Sentences.Where(s=>s.SentenceId==u.SentenceId).ToArray();if(matches.Length!=1)return false;var s=matches[0];
            return s.ClaimIds.Count(x=>x==u.ClaimId)==1&&s.ClaimIds.Count==1&&s.IsRequired==required&&s.IsOptional==optional&&!s.IsTransition&&
                s.SentenceRole==(required?"RequiredClaim":"OptionalClaim")&&s.QualificationIds.Order(StringComparer.Ordinal).SequenceEqual(u.QualificationIds.Order(StringComparer.Ordinal))&&
                s.KnowledgeReferenceIds.Order(StringComparer.Ordinal).SequenceEqual(c.KnowledgeReferenceIds.Order(StringComparer.Ordinal))&&
                s.Text==NarrationDraftFactualRealization.Expected(c.Text,u.QualificationIds,input.Language,new DeterministicNarrationDraftLanguagePolicy());
        }
        static bool ValidFlags(NarrationDraftSentence s)=>s.SentenceRole switch {"RequiredClaim"=>s.IsRequired&&!s.IsOptional&&!s.IsTransition,"OptionalClaim"=>!s.IsRequired&&s.IsOptional&&!s.IsTransition,"IncomingTransition" or "OutgoingTransition"=>!s.IsRequired&&!s.IsOptional&&s.IsTransition,"Opening" or "Closing"=>!s.IsRequired&&!s.IsOptional&&!s.IsTransition,_=>false};
        bool ValidTransitions(NarrationPlanningScene plan,NarrationDraftScene draft)
        {
            bool One(NarrationDraftTransitionPhrase? phrase,NarrationPlanningTransition source,string? authored,string role,bool first)=>phrase is null
                ? string.IsNullOrWhiteSpace(authored)&&draft.Sentences.All(s=>s.SentenceRole!=role)
                : phrase.TransitionId==source.TransitionId&&phrase.Kind==source.Kind&&phrase.Variant==draft.Variant&&phrase.PlanningTransitionIds.SequenceEqual([source.TransitionId])&&phrase.DeterministicChecksum==NarrationDraftCanonicalizer.ComputeTransitionPhraseChecksum(phrase)&&
                  phrase.Text==new DeterministicNarrationDraftLanguagePolicy().Terminate(authored!,input.Language)&&draft.Sentences.Count(s=>s.SentenceRole==role&&s.IsTransition&&s.Text==phrase.Text&&s.ClaimIds.Count==0&&s.KnowledgeReferenceIds.Count==0)==1&&(!first||draft.Sentences[0].SentenceRole==role)&&(!first||draft.Sentences[0].Text==phrase.Text);
            return One(draft.IncomingTransitionPhrase,plan.IncomingTransition,plan.IncomingTransition.DestinationTransitionIn,"IncomingTransition",true)&&One(draft.OutgoingTransitionPhrase,plan.OutgoingTransition,plan.OutgoingTransition.SourceTransitionOut,"OutgoingTransition",false)&&
                (draft.OutgoingTransitionPhrase is null||draft.Sentences[^1].SentenceRole=="OutgoingTransition");
        }
    }
    private static bool Independent(NarrationDraftAuthority authority)
    {
        if(authority.LongScenes.Any(x=>x.Variant!="Long")||authority.ShortScenes.Any(x=>x.Variant!="Short")||
           authority.LongScenes.Select(x=>x.DraftSceneId).Intersect(authority.ShortScenes.Select(x=>x.DraftSceneId),StringComparer.Ordinal).Any()||
           authority.LongScenes.Select(x=>x.PlanningId).Intersect(authority.ShortScenes.Select(x=>x.PlanningId),StringComparer.Ordinal).Any())return false;
        var longs=authority.LongScenes.SelectMany(x=>x.Sentences).Select(x=>x.Text).ToArray();var shorts=authority.ShortScenes.SelectMany(x=>x.Sentences).Select(x=>x.Text).ToArray();
        if(shorts.Length==0||longs.Length==0)return true;
        if(longs.SequenceEqual(shorts,StringComparer.Ordinal)||shorts.Length<=longs.Length&&Enumerable.Range(0,longs.Length-shorts.Length+1).Any(i=>longs.Skip(i).Take(shorts.Length).SequenceEqual(shorts,StringComparer.Ordinal)))return false;
        var cursor=0;foreach(var text in longs)if(cursor<shorts.Length&&text==shorts[cursor])cursor++;return cursor!=shorts.Length;
    }
    private static bool Diagnostics(NarrationDraftDiagnostics d,IReadOnlyList<NarrationPlanningScene> p,IReadOnlyList<NarrationDraftScene> s)=>d.PlanningSceneCount==p.Count&&d.DraftSceneCount==s.Count&&d.RequiredClaimCount==p.Sum(x=>x.RequiredClaims.Count)&&d.RequiredClaimUsageCount==s.Sum(x=>x.RequiredClaimUsage.Count)&&d.RequiredClaimUsageCount==d.RequiredClaimCount&&d.DeferredClaimUsageCount==0&&d.BlockingIssueCount==0&&d.FailedGateCount==0&&d.SentenceCount==s.Sum(x=>x.SentenceCount)&&d.TotalWordCount==s.Sum(x=>x.WordCount)&&d.DeterministicChecksum==NarrationDraftCanonicalizer.ComputeDiagnosticsChecksum(d);
}

/// <summary>Provider-free, read-only P7.1C-A orchestration.</summary>
public sealed class Phase7NarrationDraftAuthorityService(IPhase7NarrationDraftInputAuthorityEvaluator inputEvaluator,
    INarrationDraftAuthorityBuilder builder,INarrationDraftValidator validator):IPhase7NarrationDraftAuthorityService
{
    public async Task<Phase7NarrationDraftAuthorityServiceResult> ExecuteAsync(Phase7NarrationDraftInputAuthorityRequest request,CancellationToken token=default)
    {
        var evaluated=await inputEvaluator.EvaluateAsync(request,token);
        if(!evaluated.IsValid||evaluated.Authority is null)return new(false,evaluated.ReasonCode,"NotRun","NotRun",null,null,evaluated.Errors,evaluated.Warnings,evaluated.BlockingIssues);
        var built=builder.Build(evaluated.Authority);
        if(!built.IsValid||built.Authority is null)return new Phase7NarrationDraftAuthorityServiceResult(false,evaluated.ReasonCode,built.ReasonCode,built.ReasonCode,null,null,built.Errors,Merge(evaluated.Warnings,built.Warnings),built.BlockingIssues) with { SceneFailureSummaries = built.SceneFailureSummaries };
        var validation=validator.Validate(evaluated.Authority,built.Authority);
        if(!validation.IsValid)return new(false,evaluated.ReasonCode,built.ReasonCode,validation.ReasonCode,built.Authority,validation,validation.Errors,Merge(evaluated.Warnings,built.Warnings,validation.Warnings),validation.Errors);
        return new(true,evaluated.ReasonCode,built.ReasonCode,validation.ReasonCode,built.Authority,validation,[],Merge(evaluated.Warnings,built.Warnings,validation.Warnings),[]);
    }
    private static string[] Merge(params IEnumerable<string>[] warnings)=>warnings.SelectMany(x=>x).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
