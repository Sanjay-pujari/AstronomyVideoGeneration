using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astronomy.MediaFactory.Core.Certification;

public sealed record CertificationJsonReadResult(bool Exists, long Length, bool ValidJson, JsonDocument? Document, string? Error);

public interface ICertificationArtifactVerifier
{
    Task<IReadOnlyList<ArtifactCertificationResult>> VerifyAsync(FamilyCertificationContext context, IReadOnlyList<PhaseArtifactDefinition> definitions, CancellationToken cancellationToken);
}

public interface ICertificationJsonReader
{
    Task<CertificationJsonReadResult> ReadAsync(string path, CancellationToken cancellationToken);
    Task<JsonDocument?> ReadOptionalDocumentAsync(string path, CancellationToken cancellationToken);
    Task<JsonDocument> ReadRequiredDocumentAsync(string path, CancellationToken cancellationToken);
}

public sealed class CertificationJsonReader : ICertificationJsonReader
{
    private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    public async Task<CertificationJsonReadResult> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new(false, 0, false, null, "File does not exist.");
        var length = new FileInfo(path).Length;
        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonDocument.ParseAsync(stream, Options, cancellationToken);
            return new(true, length, true, document, null);
        }
        catch (JsonException ex) { return new(true, length, false, null, ex.Message); }
        catch (IOException ex) { return new(true, length, false, null, ex.Message); }
    }
    public async Task<JsonDocument?> ReadOptionalDocumentAsync(string path, CancellationToken cancellationToken) => (await ReadAsync(path, cancellationToken)).Document;
    public async Task<JsonDocument> ReadRequiredDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ReadAsync(path, cancellationToken);
        if (!result.Exists) throw new FileNotFoundException("Certification JSON artifact was not found.", path);
        if (!result.ValidJson || result.Document is null) throw new JsonException(result.Error);
        return result.Document;
    }
}

public sealed class CertificationArtifactVerifier(ICertificationJsonReader? reader = null) : ICertificationArtifactVerifier
{
    private readonly ICertificationJsonReader _reader = reader ?? new CertificationJsonReader();
    public async Task<IReadOnlyList<ArtifactCertificationResult>> VerifyAsync(FamilyCertificationContext context, IReadOnlyList<PhaseArtifactDefinition> definitions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(definitions);
        var results = new List<ArtifactCertificationResult>(definitions.Count);
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedPath = CertificationPathHelpers.ResolveArtifactPath(context, definition.RelativePath);
            var exists = File.Exists(expectedPath);
            long? length = exists ? new FileInfo(expectedPath).Length : null;
            var valid = exists || !definition.Required;
            string? message = null;
            if (!exists) message = definition.Required ? "Required artifact is missing." : "Optional artifact was not generated.";
            else if (definition.RequireNonEmpty && length == 0) { valid = false; message = "Artifact is empty."; }
            else if (definition.ValidateJson)
            {
                var json = await _reader.ReadAsync(expectedPath, cancellationToken);
                if (!json.ValidJson) { valid = false; message = "Artifact is not valid JSON: " + json.Error; }
            }
            results.Add(new ArtifactCertificationResult { ArtifactId = definition.ArtifactId, ExpectedPath = expectedPath, Required = definition.Required, Exists = exists, IsNonEmpty = exists && length > 0, IsValid = valid, LengthBytes = length, ValidationMessage = message });
        }
        return results;
    }
}

public sealed class PhaseArtifactRegistry : IPhaseArtifactRegistry
{
    public IReadOnlyList<PhaseArtifactDefinition> GetDefinitions(int phaseNumber, FamilyCertificationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var v = $"validation/phase-{phaseNumber:00}-validation.json";
        return phaseNumber switch
        {
            1 => [Req("phase-01-validation",1,v), Req("content-plan-production-request",1,"plan-input/content-plan-production-request.json"), Req("production-pipeline-request",1,"plan-input/production-pipeline-request.json")],
            2 => [Req("phase-02-validation",2,v), Req("production-event-intelligence",2,"plan-input/production-event-intelligence.json"), Req("production-event-intelligence-diagnostics",2,"plan-input/production-event-intelligence-diagnostics.json")],
            3 => [Req("phase-03-validation",3,v), Req("question-answer-set",3,"question-engine/question-answer-set.json"), Req("question-driven-scene-plan",3,"question-engine/question-driven-scene-plan.json"), Opt("question-driven-scene-plan-enriched",3,"question-engine/question-driven-scene-plan.enriched.json")],
            4 => [Req("phase-04-validation",4,v), Req("story-graph",4,"editorial/story-graph.json")],
            5 => [Req("phase-05-validation",5,v), Req("observation-metadata",5,"editorial/observation-metadata.json"), Req("scene-intents",5,"editorial/scene-intents.json"), Req("editorial-contract",5,"editorial/editorial-contract.json"), Opt("editorial-diagnostics",5,"editorial/editorial-diagnostics.json")],
            6 => [Req("phase-06-validation",6,v), Req("creative-storyboard",6,"creative/creative-storyboard.json"), Req("documentary-contract-long",6,"creative/documentary-contract.long.json"), Req("documentary-contract-short",6,"creative/documentary-contract.short.json"), Req("documentary-architecture-diagnostics",6,"creative/documentary-architecture-diagnostics.json"), Req("documentary-decision-log",6,"creative/documentary-decision-log.json"), Opt("story-frames-long-manifest",6,"story-frames/long/manifest.json"), Opt("story-frames-short-manifest",6,"story-frames/short/manifest.json")],
            7 => [Opt("narration-context",7,"narration-v5/narration-context.json"), Opt("narration-plan",7,"narration-v5/narration-plan.json"), Opt("narration-briefs",7,"narration-v5/narration-briefs.json"), Opt("documentary-script",7,"narration-v5/documentary-script/documentary-script.json"), Opt("scene-fact-cards",7,"narration-v5/scene-fact-cards/scene-fact-cards.json"), Opt("raw-narrative",7,"narration-v5/raw-narrative/raw-narrative.json"), Opt("runtime-composition-diagnostics",7,"narration-v5/runtime-composition-diagnostics.json"), Opt("narration-validation-diagnostics",7,"narration-v5/narration-validation-diagnostics.json"), Opt("prompt-quality",7,"narration-v5/prompt-quality.json")],
            _ => []
        };
    }
    private static PhaseArtifactDefinition Req(string id,int p,string path)=>new(){ArtifactId=id,PhaseNumber=p,RelativePath=path,Required=true,ValidateJson=true,RequireNonEmpty=true};
    private static PhaseArtifactDefinition Opt(string id,int p,string path)=>new(){ArtifactId=id,PhaseNumber=p,RelativePath=path,Required=false,ValidateJson=true,RequireNonEmpty=true};
}

public static class CertificationPathHelpers
{
    public static string ResolveArtifactPath(FamilyCertificationContext context, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Path.IsPathRooted(relativePath) || relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Any(p => p == "..")) throw new InvalidOperationException("Certification artifact path escapes an allowed root.");
        var outputRoot = Path.GetFullPath(context.OutputRoot) + Path.DirectorySeparatorChar;
        var validationRoot = Path.GetFullPath(context.ValidationRoot) + Path.DirectorySeparatorChar;
        var baseRoot = relativePath.Replace('\\','/').StartsWith("validation/", StringComparison.OrdinalIgnoreCase) ? validationRoot : outputRoot;
        var suffix = relativePath.Replace('\\','/').StartsWith("validation/", StringComparison.OrdinalIgnoreCase) ? relativePath["validation/".Length..] : relativePath;
        var resolved = Path.GetFullPath(Path.Combine(baseRoot, suffix.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(outputRoot, StringComparison.Ordinal) && !resolved.StartsWith(validationRoot, StringComparison.Ordinal)) throw new InvalidOperationException("Certification artifact path escapes an allowed root.");
        return resolved;
    }
}

public static class CertificationStatusAggregator
{
    public static CertificationStatus FromArtifacts(IEnumerable<ArtifactCertificationResult> artifacts)
    {
        var list = artifacts.ToArray();
        if (list.Length == 0) return CertificationStatus.NotApplicable;
        if (list.Any(a => a.Required && (!a.Exists || !a.IsNonEmpty || !a.IsValid))) return CertificationStatus.Failed;
        return list.Any(a => !a.Required && !a.Exists) ? CertificationStatus.PassedWithWarnings : CertificationStatus.Passed;
    }
    public static CertificationStatus Structural(IEnumerable<ArtifactCertificationResult> artifacts, IEnumerable<CertificationIssue> issues) => issues.Any(i => i.IsBlocking) ? CertificationStatus.Failed : FromArtifacts(artifacts);
    public static CertificationStatus SemanticUnavailable() => CertificationStatus.NotEvaluated;
    public static CertificationStatus QualityFromDiagnostics(params JsonDocument?[] diagnostics) => diagnostics.Any(d => JsonHasFailure(d?.RootElement)) ? CertificationStatus.Failed : CertificationStatus.NotApplicable;
    public static CertificationStatus Combine(params CertificationStatus[] statuses) => statuses.Contains(CertificationStatus.Failed) ? CertificationStatus.Failed : statuses.Contains(CertificationStatus.PassedWithWarnings) ? CertificationStatus.PassedWithWarnings : statuses.Contains(CertificationStatus.Passed) ? CertificationStatus.Passed : statuses.Contains(CertificationStatus.NotEvaluated) ? CertificationStatus.NotEvaluated : CertificationStatus.NotApplicable;
    private static bool JsonHasFailure(JsonElement? e) => e is { ValueKind: JsonValueKind.Object } v && v.EnumerateObject().Any(p => (p.Name.Contains("status", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("result", StringComparison.OrdinalIgnoreCase)) && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()?.Contains("fail", StringComparison.OrdinalIgnoreCase) == true);
}

public abstract class PhaseCertifierBase(IPhaseArtifactRegistry registry, ICertificationArtifactVerifier verifier) : IPhaseCertifier
{
    public abstract int PhaseNumber { get; }
    protected abstract string PhaseName { get; }
    public virtual async Task<PhaseCertificationResult> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken)
    {
        var artifacts = await verifier.VerifyAsync(context, registry.GetDefinitions(PhaseNumber, context), cancellationToken);
        var issues = artifacts.Where(a => a.Required && (!a.Exists || !a.IsNonEmpty || !a.IsValid)).Select(a => Issue($"P{PhaseNumber}.ArtifactInvalid", a.ValidationMessage ?? "Required artifact failed certification.", a.ExpectedPath)).Concat(await ValidateAsync(context, cancellationToken)).ToArray();
        return new PhaseCertificationResult { PhaseNumber = PhaseNumber, PhaseName = PhaseName, StructuralStatus = CertificationStatusAggregator.Structural(artifacts, issues), SemanticStatus = CertificationStatusAggregator.SemanticUnavailable(), QualityStatus = CertificationStatus.NotApplicable, Artifacts = artifacts, Issues = issues, Warnings = artifacts.Where(a => !a.Required && !a.Exists).Select(a => $"Optional artifact not present: {a.ArtifactId}.").ToArray(), GeneratedUtc = DateTimeOffset.UtcNow };
    }
    protected virtual Task<IEnumerable<CertificationIssue>> ValidateAsync(FamilyCertificationContext context, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<CertificationIssue>());
    protected static CertificationIssue Issue(string code, string message, string? path = null, bool blocking = true) => new() { Category = CertificationIssueCategory.DataQualityFailure, Code = code, Message = message, ArtifactPath = path, IsBlocking = blocking, Source = "PhaseCertification" };
    protected static async Task<JsonDocument?> Read(FamilyCertificationContext c, string p, CancellationToken t) => await new CertificationJsonReader().ReadOptionalDocumentAsync(CertificationPathHelpers.ResolveArtifactPath(c,p), t);
    protected static bool Has(JsonElement e, params string[] names) => names.Any(n => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined && (v.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(v.GetString())));
    protected static IEnumerable<JsonElement> Arr(JsonElement e, params string[] names) { foreach (var n in names) if (e.ValueKind==JsonValueKind.Object && e.TryGetProperty(n,out var v) && v.ValueKind==JsonValueKind.Array) return v.EnumerateArray(); return []; }
    protected static string? Str(JsonElement e, params string[] names) { foreach (var n in names) if (e.ValueKind==JsonValueKind.Object && e.TryGetProperty(n,out var v) && v.ValueKind==JsonValueKind.String) return v.GetString(); return null; }
}

public sealed class Phase1Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 1; protected override string PhaseName => "Run Setup / Plan Selection"; protected override Task<IEnumerable<CertificationIssue>> ValidateAsync(FamilyCertificationContext c, CancellationToken t){ var list=new List<CertificationIssue>(); if(string.IsNullOrWhiteSpace(c.PlanId))list.Add(Issue("P1.PlanIdMissing","PlanId is required.")); if(string.IsNullOrWhiteSpace(c.EventType))list.Add(Issue("P1.EventTypeMissing","EventType is required.")); if(string.IsNullOrWhiteSpace(c.Language))list.Add(Issue("P1.LanguageMissing","Language is required.")); if(string.IsNullOrWhiteSpace(c.RegionId))list.Add(Issue("P1.RegionMissing","Region is required.")); if(string.IsNullOrWhiteSpace(c.EventTitle))list.Add(Issue("P1.EventTitleMissing","Event title is required.")); if(c.RequestedStartPhase<1||c.RequestedEndPhase>7||c.RequestedStartPhase>c.RequestedEndPhase)list.Add(Issue("P1.RequestedPhaseRangeInvalid","Requested phase range must be within 1-7.")); return Task.FromResult<IEnumerable<CertificationIssue>>(list); } }
public sealed class Phase2Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 2; protected override string PhaseName => "Domain Intelligence"; protected override async Task<IEnumerable<CertificationIssue>> ValidateAsync(FamilyCertificationContext c, CancellationToken t){ var d=await Read(c,"plan-input/production-event-intelligence.json",t); var list=new List<CertificationIssue>(); if(d is null){list.Add(Issue("P2.IntelligenceMissing","ProductionEventIntelligence is required.")); return list;} var root=d.RootElement; if(Str(root,"eventType","EventType") is {} et && !string.Equals(et,c.EventType,StringComparison.OrdinalIgnoreCase))list.Add(Issue("P2.EventTypeMismatch","EventType does not match Phase 1.")); foreach(var (code,names) in new[]{("P2.VerificationStatusMissing",new[]{"verificationStatus","VerificationStatus"}),("P2.VerificationSourceMissing",new[]{"verificationSource","VerificationSource","source"})}) if(!Has(root,names))list.Add(Issue(code,code)); return list;} }
public sealed class Phase3Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 3; protected override string PhaseName => "Question / Story Planning"; protected override async Task<IEnumerable<CertificationIssue>> ValidateAsync(FamilyCertificationContext c, CancellationToken t){ var d=await Read(c,"question-engine/question-answer-set.json",t); var list=new List<CertificationIssue>(); if(d is null){list.Add(Issue("P3.QuestionAnswerSetMissing","QuestionAnswerSet is required.")); return list;} var qs=Arr(d.RootElement,"questions","Questions").ToArray(); if(qs.Length==0)list.Add(Issue("P3.QuestionsMissing","Questions are required.")); var ids=qs.Select(q=>Str(q,"id","questionId","QuestionId")).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray(); if(ids.Length!=ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())list.Add(Issue("P3.DuplicateQuestionId","Duplicate question ids detected.")); if(qs.Any(q=>!Has(q,"answer","Answer")))list.Add(Issue("P3.EmptyAnswer","Empty answers detected.")); return list;} }
public sealed class Phase4Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 4; protected override string PhaseName => "Story Intelligence"; protected override async Task<IEnumerable<CertificationIssue>> ValidateAsync(FamilyCertificationContext c, CancellationToken t){ var d=await Read(c,"editorial/story-graph.json",t); var list=new List<CertificationIssue>(); if(d is null){list.Add(Issue("P4.StoryGraphMissing","StoryGraph is required.")); return list;} var nodes=Arr(d.RootElement,"nodes","Nodes").ToArray(); if(nodes.Length==0)list.Add(Issue("P4.GraphEmpty","Story graph must contain nodes.")); var ids=nodes.Select(n=>Str(n,"id","nodeId","NodeId")).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray(); if(ids.Length!=ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())list.Add(Issue("P4.DuplicateNodeId","Duplicate node ids detected.")); return list;} }
public sealed class Phase5Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 5; protected override string PhaseName => "Editorial Intelligence"; protected override async Task<IEnumerable<CertificationIssue>> ValidateAsync(FamilyCertificationContext c, CancellationToken t){ var list=new List<CertificationIssue>(); foreach(var p in new[]{"editorial/observation-metadata.json","editorial/scene-intents.json","editorial/editorial-contract.json"}) if(await Read(c,p,t) is null) list.Add(Issue("P5.RequiredArtifactMissing",$"Required editorial artifact missing: {p}")); var si=await Read(c,"editorial/scene-intents.json",t); if(si!=null){var intents=Arr(si.RootElement,"intents","sceneIntents","SceneIntents").ToArray(); if(intents.Length==0)list.Add(Issue("P5.EmptyIntentCollection","Scene intents are required.")); var ids=intents.Select(i=>Str(i,"id","intentId","sceneId")).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray(); if(ids.Length!=ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())list.Add(Issue("P5.DuplicateIntentId","Duplicate intent ids detected."));} return list;} }
public sealed class Phase6Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 6; protected override string PhaseName => "Creative Intelligence / Story Frames"; protected override async Task<IEnumerable<CertificationIssue>> ValidateAsync(FamilyCertificationContext c, CancellationToken t){ var list=new List<CertificationIssue>(); foreach(var folder in new[]{"story-frames/short","story-frames/long"}){var dir=CertificationPathHelpers.ResolveArtifactPath(c,folder); if(!Directory.Exists(dir))continue; var scenes=new List<(string path,string? id)>(); foreach(var f in Directory.EnumerateFiles(dir,"scene-*.json").Where(f=>!Path.GetFileName(f).Contains("manifest",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(f).Contains("diagnostic",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(f).Contains("tmp",StringComparison.OrdinalIgnoreCase))){var rr=await new CertificationJsonReader().ReadAsync(f,t); if(!rr.ValidJson||rr.Document is null){list.Add(Issue("P6.InvalidSceneJson","Scene JSON is invalid.",f)); continue;} var id=Str(rr.Document.RootElement,"sceneId","id","SceneId"); if(string.IsNullOrWhiteSpace(id))list.Add(Issue("P6.SceneMissing","Scene identity is missing.",f)); if(!rr.Document.RootElement.ToString().Contains("intent",StringComparison.OrdinalIgnoreCase))list.Add(Issue("P6.NarrationIntentsMissing","Required narration intent collections are missing.",f)); scenes.Add((f,id));} var dups=scenes.Select(s=>s.id).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x!,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1); foreach(var _ in dups)list.Add(Issue("P6.DuplicateSceneId","Duplicate scene ids detected.")); var mf=Path.Combine(dir,"manifest.json"); var m=await new CertificationJsonReader().ReadAsync(mf,t); if(m.Exists&&m.ValidJson&&m.Document!=null){var refs=Arr(m.Document.RootElement,"scenes","sceneIds","Scenes").ToArray(); var count=refs.Length>0?refs.Length:(int?)null; if(count.HasValue&&count.Value!=scenes.Count)list.Add(Issue("P6.ManifestCountMismatch","Manifest scene count does not match physical scene count.",mf)); foreach(var rj in refs){var rid=rj.ValueKind==JsonValueKind.String?rj.GetString():Str(rj,"sceneId","id"); if(!string.IsNullOrWhiteSpace(rid)&&!scenes.Any(s=>string.Equals(s.id,rid,StringComparison.OrdinalIgnoreCase)))list.Add(Issue("P6.InvalidManifestReference","Manifest references a missing scene.",mf));}}} return list;} }
public sealed class Phase7Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v, IFamilyCertificationProfileRegistry profiles, ISemanticCertificationEvidenceReader evidenceReader, IForbiddenConceptValidator forbidden, IStoryBeatCoverageValidator storyBeat, ISemanticFactCatalog? factCatalog = null) : PhaseCertifierBase(r,v)
{
    public override int PhaseNumber => 7;
    protected override string PhaseName => "Narration Studio V5";
    private readonly ISemanticFactCatalog factCatalog = factCatalog ?? new CertificationSemanticFactCatalog();
    public override async Task<PhaseCertificationResult> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken)
    {
        IFamilyCertificationProfile? profile = null;
        var definitions = r.GetDefinitions(PhaseNumber, context).ToList();
        var semanticIssues = new List<CertificationIssue>();
        if (profiles.TryResolve(context.EventType, out profile)) definitions.AddRange(profile.GetAdditionalArtifacts(context));
        else semanticIssues.Add(SIssue(CertificationIssueCategory.FamilyMismatch, "P7.FamilyProfileMismatch", $"No certification profile resolved for EventType '{context.EventType}'.", null));
        var artifacts = await v.VerifyAsync(context, definitions, cancellationToken);
        var structuralIssues = artifacts.Where(a => a.Required && (!a.Exists || !a.IsNonEmpty || !a.IsValid)).Select(a => new CertificationIssue { Category = CertificationIssueCategory.MissingArtifact, Code = "P7.ArtifactInvalid", Message = a.ValidationMessage ?? "Required artifact failed certification.", ArtifactPath = a.ExpectedPath, IsBlocking = true, Source = "PhaseCertification" }).ToList();
        var evidence = await evidenceReader.ReadAsync(context, cancellationToken);
        if (profile is not null)
        {
            if (!string.IsNullOrWhiteSpace(evidence.FamilyId) && !profile.FamilyId.Equals(evidence.FamilyId, StringComparison.OrdinalIgnoreCase) && !profile.SupportedEventTypeAliases.Contains(evidence.FamilyId)) semanticIssues.Add(SIssue(CertificationIssueCategory.FamilyMismatch, "P7.FamilyProfileMismatch", $"Evidence family '{evidence.FamilyId}' does not match profile '{profile.FamilyId}'.", null));
            if (!evidence.CanonicalIdentityPresent) semanticIssues.Add(SIssue(CertificationIssueCategory.MissingSemanticFact, "P7.RequiredFactUnresolved", "Canonical event identity evidence is missing.", factCatalog.ResolveFactId("EventIdentity").FactId));
            if (profile.CanonicalSemanticValueId is not null && (!evidence.CanonicalFamilyValuePresent || !string.Equals(profile.CanonicalSemanticValueId, evidence.CanonicalSemanticValueId, StringComparison.OrdinalIgnoreCase))) semanticIssues.Add(SIssue(CertificationIssueCategory.MissingCanonicalValue, "P7.CanonicalFamilyValueMissing", $"Canonical semantic value '{profile.CanonicalSemanticValueId}' is missing.", profile.CanonicalSemanticValueId));
            foreach (var req in profile.GetRequiredFacts(context).Where(f=>f.Required))
            {
                var fact = evidence.Facts.FirstOrDefault(f => f.FactId.Equals(req.FactId, StringComparison.OrdinalIgnoreCase));
                if (fact is null || !fact.Resolved) semanticIssues.Add(SIssue(CertificationIssueCategory.ResolutionFailure, "P7.RequiredFactUnresolved", $"Required fact '{req.FactId}' was not resolved.", req.FactId));
                else
                {
                    if (!fact.Projected) semanticIssues.Add(SIssue(CertificationIssueCategory.ProjectionFailure, "P7.RequiredFactNotProjected", $"Required fact '{req.FactId}' was not projected.", req.FactId));
                    if (!fact.Retained) semanticIssues.Add(SIssue(CertificationIssueCategory.RetentionFailure, "P7.RequiredFactNotRetained", $"Required fact '{req.FactId}' was not retained.", req.FactId));
                    if (!fact.BeatAssigned) semanticIssues.Add(SIssue(CertificationIssueCategory.BeatAssignmentFailure, "P7.RequiredFactNotBeatAssigned", $"Required fact '{req.FactId}' was not assigned to a beat.", req.FactId));
                    if (!fact.NarrationEvidenceFound) semanticIssues.Add(SIssue(CertificationIssueCategory.NarrationEvidenceFailure, "P7.RequiredFactMissingFromNarration", $"Required fact '{req.FactId}' was not found in narration evidence.", req.FactId));
                }
            }
            semanticIssues.AddRange(storyBeat.Validate(profile, context, evidence));
            foreach (var leak in await forbidden.ValidateAsync(context, profile, cancellationToken)) if (leak.IsBlocking) semanticIssues.Add(new CertificationIssue { Category = CertificationIssueCategory.CrossFamilyLeakage, Code = "P7.ForbiddenConceptDetected", Message = $"Forbidden concept '{leak.ConceptId}' matched '{leak.MatchedTerm}' in {leak.SourceArtifact}.{leak.SourceField}.", ArtifactPath = leak.SourceArtifact, Source = "ForbiddenConceptValidator", IsBlocking = true });
        }
        var quality = await ReadQualityStatus(context, cancellationToken);
        var allIssues = structuralIssues.Concat(semanticIssues).ToArray();
        var semanticStatus = semanticIssues.Any(i=>i.IsBlocking) ? CertificationStatus.Failed : semanticIssues.Count > 0 ? CertificationStatus.PassedWithWarnings : profile is null ? CertificationStatus.NotEvaluated : CertificationStatus.Passed;
        return new PhaseCertificationResult { PhaseNumber = PhaseNumber, PhaseName = PhaseName, StructuralStatus = CertificationStatusAggregator.Structural(artifacts, structuralIssues), SemanticStatus = semanticStatus, QualityStatus = quality, Artifacts = artifacts, SemanticFacts = evidence.Facts, Issues = allIssues, Warnings = artifacts.Where(a => !a.Required && !a.Exists).Select(a => $"Optional artifact not present: {a.ArtifactId}.").ToArray(), GeneratedUtc = DateTimeOffset.UtcNow };
    }
    private static CertificationIssue SIssue(CertificationIssueCategory c, string code, string msg, string? fact) => new() { Category = c, Code = code, Message = msg, SemanticFactId = fact, IsBlocking = true, Source = "Phase7SemanticCertification" };
    private static async Task<CertificationStatus> ReadQualityStatus(FamilyCertificationContext c, CancellationToken t)
    {
        var docs = new[] { await Read(c,"narration-v5/narration-validation-diagnostics.json",t), await Read(c,"narration-v5/narration-diagnostics.json",t), await Read(c,"validation/phase-07-validation.json",t) }.Where(d=>d is not null).ToArray();
        if (docs.Length == 0) return CertificationStatus.NotEvaluated;
        var sawQuality = false; var sawWarning = false;
        foreach (var doc in docs) foreach (var value in QualityValues(doc!.RootElement))
        {
            sawQuality = true;
            if (value is bool b && !b) return CertificationStatus.Failed;
            if (value is string s)
            {
                if (s.Equals("Do Not Publish", StringComparison.OrdinalIgnoreCase) || s.Contains("Failed", StringComparison.OrdinalIgnoreCase) || s.Contains("Fail", StringComparison.OrdinalIgnoreCase)) return CertificationStatus.Failed;
                if (s.Contains("warning", StringComparison.OrdinalIgnoreCase)) sawWarning = true;
            }
        }
        return sawWarning ? CertificationStatus.PassedWithWarnings : sawQuality ? CertificationStatus.Passed : CertificationStatus.NotEvaluated;
    }
    private static IEnumerable<object> QualityValues(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject())
        {
            var n = p.Name;
            var isQuality = n.Equals("requiredFactsPreserved", StringComparison.OrdinalIgnoreCase) || n.Equals("longNarrationQualityAccepted", StringComparison.OrdinalIgnoreCase) || n.Equals("shortNarrationQualityAccepted", StringComparison.OrdinalIgnoreCase) || n.Equals("finalDecision", StringComparison.OrdinalIgnoreCase) || n.Equals("publicationDecision", StringComparison.OrdinalIgnoreCase) || n.Contains("warning", StringComparison.OrdinalIgnoreCase) || n.Contains("status", StringComparison.OrdinalIgnoreCase) || n.Contains("result", StringComparison.OrdinalIgnoreCase);
            if (isQuality && (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False)) yield return p.Value.GetBoolean();
            if (isQuality && p.Value.ValueKind == JsonValueKind.String) yield return p.Value.GetString() ?? string.Empty;
            foreach (var x in QualityValues(p.Value)) yield return x;
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var a in e.EnumerateArray()) foreach (var x in QualityValues(a)) yield return x;
    }
}

public static class CgA1CertificationTask2ServiceCollectionExtensions
{
    public static IServiceCollection AddCgA1PhaseCertification(this IServiceCollection services)
    {
        services.TryAddSingleton<CertificationSemanticFactCatalog>(); services.TryAddSingleton<ISemanticFactCatalog>(sp => sp.GetRequiredService<CertificationSemanticFactCatalog>()); services.TryAddSingleton<ICertificationJsonReader, CertificationJsonReader>(); services.TryAddSingleton<ICertificationArtifactVerifier, CertificationArtifactVerifier>(); services.TryAddSingleton<IPhaseArtifactRegistry, PhaseArtifactRegistry>(); services.TryAddSingleton<ISemanticCertificationEvidenceReader, SemanticCertificationEvidenceReader>(); services.TryAddSingleton<IForbiddenConceptValidator, ForbiddenConceptValidator>(); services.TryAddSingleton<IStoryBeatCoverageValidator, StoryBeatCoverageValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPhaseCertifier, Phase1Certifier>()); services.TryAddEnumerable(ServiceDescriptor.Singleton<IPhaseCertifier, Phase2Certifier>()); services.TryAddEnumerable(ServiceDescriptor.Singleton<IPhaseCertifier, Phase3Certifier>()); services.TryAddEnumerable(ServiceDescriptor.Singleton<IPhaseCertifier, Phase4Certifier>()); services.TryAddEnumerable(ServiceDescriptor.Singleton<IPhaseCertifier, Phase5Certifier>()); services.TryAddEnumerable(ServiceDescriptor.Singleton<IPhaseCertifier, Phase6Certifier>()); services.TryAddEnumerable(ServiceDescriptor.Singleton<IPhaseCertifier, Phase7Certifier>());
        return services;
    }
}
