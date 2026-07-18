using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.Certification;

public sealed class CertificationOptions
{
    public const string SectionName = "Certification";
    public bool Enabled { get; set; } = false;
    public bool WriteMarkdownReport { get; set; } = true;
    public bool WriteDashboardJson { get; set; } = true;
    public bool FailPipelineOnCertificationFailure { get; set; } = false;
}

public enum CertificationDecision { Certified, CertifiedWithWarnings, NotCertified, NotEvaluated }
public enum PublicationDecision { Publish, PublishWithWarnings, DoNotPublish, ManualReview, NotEvaluated }
public enum DashboardSeverity { Success, Warning, Error, Neutral }

public interface ICertificationPathService
{
    string GetCertificationRoot(FamilyCertificationContext context);
    string GetPhasePath(FamilyCertificationContext context, int phaseNumber);
    string GetSummaryPath(FamilyCertificationContext context);
    string GetDashboardPath(FamilyCertificationContext context);
    string GetMarkdownReportPath(FamilyCertificationContext context);
}

public sealed class CertificationPathService : ICertificationPathService
{
    public const string CertificationDirectoryName = "certification";
    public const string SummaryFileName = "certification-summary.json";
    public const string DashboardFileName = "certification-dashboard.json";
    public const string MarkdownReportFileName = "certification-report.md";
    public string GetCertificationRoot(FamilyCertificationContext context) => ResolveUnderOutput(context, CertificationDirectoryName);
    public string GetPhasePath(FamilyCertificationContext context, int phaseNumber) => ResolveUnderCertification(context, $"phase-{phaseNumber:00}-certification.json");
    public string GetSummaryPath(FamilyCertificationContext context) => ResolveUnderCertification(context, SummaryFileName);
    public string GetDashboardPath(FamilyCertificationContext context) => ResolveUnderCertification(context, DashboardFileName);
    public string GetMarkdownReportPath(FamilyCertificationContext context) => ResolveUnderCertification(context, MarkdownReportFileName);
    private static string ResolveUnderCertification(FamilyCertificationContext c, string file) => ResolveUnderOutput(c, Path.Combine(CertificationDirectoryName, file));
    private static string ResolveUnderOutput(FamilyCertificationContext c, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p == "..")) throw new InvalidOperationException("Certification output path escapes the output root.");
        var root = Path.GetFullPath(c.OutputRoot) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, relative));
        if (!resolved.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException("Certification output path escapes the output root.");
        return resolved;
    }
}

public interface ICertificationOutputLock { Task<IAsyncDisposable> AcquireAsync(string outputRoot, CancellationToken cancellationToken); }
public sealed class CertificationOutputLock : ICertificationOutputLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);
    public async Task<IAsyncDisposable> AcquireAsync(string outputRoot, CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var gate = locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(key, gate, locks);
    }
    private sealed class Releaser(string key, SemaphoreSlim gate, ConcurrentDictionary<string, SemaphoreSlim> locks) : IAsyncDisposable
    { public ValueTask DisposeAsync(){ gate.Release(); if (gate.CurrentCount == 1) locks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate)); return ValueTask.CompletedTask; } }
}

public interface ICertificationSummaryAggregator { FamilyCertificationSummary Aggregate(FamilyCertificationContext context, string familyId, IReadOnlyList<PhaseCertificationResult> phases, DateTimeOffset generatedUtc); }
public sealed class CertificationSummaryAggregator : ICertificationSummaryAggregator
{
    public const string SchemaVersion = "cg-a1-certification.v1";
    public FamilyCertificationSummary Aggregate(FamilyCertificationContext context, string familyId, IReadOnlyList<PhaseCertificationResult> phases, DateTimeOffset generatedUtc)
    {
        var structural = AggregateDimension(phases.Select(p => p.StructuralStatus), phases, CertificationLevel.Structural);
        var semantic = AggregateSemantic(phases);
        var quality = AggregateQuality(phases.Select(p => p.QualityStatus));
        var blocking = phases.SelectMany(p => p.Issues.Where(i => i.IsBlocking)).ToArray();
        var warnings = phases.SelectMany(p => p.Warnings).ToArray();
        var cert = EvaluateCertificationDecision(structural, semantic);
        var pub = EvaluatePublicationDecision(structural, semantic, quality, phases);
        return new FamilyCertificationSummary { PlanId=context.PlanId, EventTitle=context.EventTitle, EventType=context.EventType, FamilyId=familyId, Language=context.Language, RegionId=context.RegionId, RequestedStartPhase=context.RequestedStartPhase, RequestedEndPhase=context.RequestedEndPhase, SchemaVersion=SchemaVersion, ExecutionStatus=structural, StructuralStatus=structural, SemanticStatus=semantic, QualityStatus=quality, CertificationDecision=cert, PublicationDecision=pub, Phases=phases.OrderBy(p=>p.PhaseNumber).ToArray(), BlockingIssues=blocking, Warnings=warnings, FailedPhaseNumbers=phases.Where(p=>p.StructuralStatus==CertificationStatus.Failed||p.SemanticStatus==CertificationStatus.Failed||p.QualityStatus==CertificationStatus.Failed).Select(p=>p.PhaseNumber).Distinct().OrderBy(x=>x).ToArray(), ExecutionCertified=structural is CertificationStatus.Passed or CertificationStatus.PassedWithWarnings, SemanticCertified=semantic is CertificationStatus.Passed or CertificationStatus.PassedWithWarnings, PublicationCertified=pub is PublicationDecision.Publish or PublicationDecision.PublishWithWarnings, GeneratedUtc=generatedUtc };
    }
    private static CertificationStatus AggregateDimension(IEnumerable<CertificationStatus> statuses, IReadOnlyList<PhaseCertificationResult> phases, CertificationLevel level)
    { var applicable=statuses.Where(IsApplicable).ToArray(); if(applicable.Length==0) return CertificationStatus.NotApplicable; if(applicable.Contains(CertificationStatus.Failed)) return CertificationStatus.Failed; if(applicable.Contains(CertificationStatus.PassedWithWarnings) || phases.Any(p=>p.Warnings.Count>0)) return CertificationStatus.PassedWithWarnings; return applicable.All(s=>s==CertificationStatus.Passed) ? CertificationStatus.Passed : CertificationStatus.NotEvaluated; }
    private static CertificationStatus AggregateSemantic(IReadOnlyList<PhaseCertificationResult> phases)
    { var s=phases.Select(p=>p.SemanticStatus).Where(IsApplicable).ToArray(); if(s.Length==0) return CertificationStatus.NotEvaluated; if(s.Contains(CertificationStatus.Failed)) return CertificationStatus.Failed; if(s.Contains(CertificationStatus.PassedWithWarnings)) return CertificationStatus.PassedWithWarnings; return s.All(x=>x==CertificationStatus.Passed) ? CertificationStatus.Passed : CertificationStatus.NotEvaluated; }
    private static CertificationStatus AggregateQuality(IEnumerable<CertificationStatus> statuses)
    { var s=statuses.Where(IsApplicable).ToArray(); if(s.Length==0) return CertificationStatus.NotEvaluated; if(s.Contains(CertificationStatus.Failed)) return CertificationStatus.Failed; if(s.Contains(CertificationStatus.NotEvaluated)) return CertificationStatus.NotEvaluated; if(s.Contains(CertificationStatus.PassedWithWarnings)) return CertificationStatus.PassedWithWarnings; return s.All(x=>x==CertificationStatus.Passed) ? CertificationStatus.Passed : CertificationStatus.NotEvaluated; }
    private static bool IsApplicable(CertificationStatus s) => s != CertificationStatus.NotApplicable && s != CertificationStatus.NotEvaluated;
    public static CertificationDecision EvaluateCertificationDecision(CertificationStatus structural, CertificationStatus semantic) => structural==CertificationStatus.Failed||semantic==CertificationStatus.Failed ? CertificationDecision.NotCertified : structural==CertificationStatus.NotEvaluated||semantic==CertificationStatus.NotEvaluated||semantic==CertificationStatus.NotApplicable ? CertificationDecision.NotEvaluated : structural==CertificationStatus.PassedWithWarnings||semantic==CertificationStatus.PassedWithWarnings ? CertificationDecision.CertifiedWithWarnings : CertificationDecision.Certified;
    public static PublicationDecision EvaluatePublicationDecision(CertificationStatus structural, CertificationStatus semantic, CertificationStatus quality, IReadOnlyList<PhaseCertificationResult> phases) => structural==CertificationStatus.Failed||semantic==CertificationStatus.Failed||quality==CertificationStatus.Failed||phases.SelectMany(p=>p.Issues).Any(i=>i.Code.Contains("DoNotPublish",StringComparison.OrdinalIgnoreCase)) ? PublicationDecision.DoNotPublish : structural==CertificationStatus.NotEvaluated||semantic==CertificationStatus.NotEvaluated ? PublicationDecision.NotEvaluated : quality==CertificationStatus.NotEvaluated ? PublicationDecision.ManualReview : structural==CertificationStatus.Passed&&semantic==CertificationStatus.Passed&&quality==CertificationStatus.Passed ? PublicationDecision.Publish : PublicationDecision.PublishWithWarnings;
}

public sealed record CertificationDashboard(string Header, object Identity, IReadOnlyList<DashboardStatusCard> OverallStatusCards, IReadOnlyList<DashboardPhaseTimelineItem> PhaseTimeline, object IssueSummary, IReadOnlyList<DashboardSemanticFact> SemanticLifecycleSummary, DashboardStatusCard PublicationCard, IReadOnlyDictionary<string,string?> ArtifactLinks, DateTimeOffset GeneratedUtc, string SchemaVersion);
public sealed record DashboardStatusCard(string Status, string Label, DashboardSeverity Severity, string Explanation, int BlockingIssueCount, int WarningCount);
public sealed record DashboardPhaseTimelineItem(int PhaseNumber,string PhaseName,CertificationStatus StructuralStatus,CertificationStatus SemanticStatus,CertificationStatus QualityStatus,int ArtifactTotal,int ValidArtifactTotal,int BlockingIssueCount,int WarningCount,string? PhaseReportPath);
public sealed record DashboardSemanticFact(string FactId,string DisplayName,bool Required,bool Resolved,bool Projected,bool Retained,bool BeatAssigned,bool NarrationEvidenceFound,double? Confidence,IReadOnlyList<string> BeatIds,IReadOnlyList<string> SceneIds);
public interface ICertificationDashboardMapper { CertificationDashboard Map(FamilyCertificationContext context, FamilyCertificationSummary summary); }
public sealed class CertificationDashboardMapper(ISemanticFactCatalog catalog) : ICertificationDashboardMapper
{
    public CertificationDashboard Map(FamilyCertificationContext context, FamilyCertificationSummary s)
    {
        var facts=s.Phases.Where(p=>p.PhaseNumber==7).SelectMany(p=>p.SemanticFacts).GroupBy(f=>f.FactId,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(f=>f.FactId).Select(f=>new DashboardSemanticFact(f.FactId, ResolveDisplayName(catalog, f.FactId), f.Required, f.Resolved, f.Projected, f.Retained, f.BeatAssigned, f.NarrationEvidenceFound, f.Confidence, f.BeatIds, f.SceneIds)).ToArray();
        var links=s.ReportPaths;
        return new CertificationDashboard("Astronomy Family Certification", new { s.PlanId, s.EventTitle, s.EventType, s.FamilyId, s.Language, s.RegionId, s.RequestedStartPhase, s.RequestedEndPhase }, Cards(s), s.Phases.OrderBy(p=>p.PhaseNumber).Select(p=>new DashboardPhaseTimelineItem(p.PhaseNumber,p.PhaseName,p.StructuralStatus,p.SemanticStatus,p.QualityStatus,p.Artifacts.Count,p.Artifacts.Count(a=>a.IsValid),p.Issues.Count(i=>i.IsBlocking),p.Warnings.Count,links.GetValueOrDefault($"phase{p.PhaseNumber:00}"))).ToArray(), new { blockingIssueCount=s.BlockingIssues.Count, warningCount=s.Warnings.Count, failedPhaseNumbers=s.FailedPhaseNumbers }, facts, Card("Publication Decision", s.PublicationDecision.ToString(), s.PublicationDecision==PublicationDecision.DoNotPublish?DashboardSeverity.Error:s.PublicationDecision==PublicationDecision.Publish?DashboardSeverity.Success:s.PublicationDecision==PublicationDecision.NotEvaluated?DashboardSeverity.Neutral:DashboardSeverity.Warning, s.BlockingIssues.Count, s.Warnings.Count), links, s.GeneratedUtc, s.SchemaVersion);
    }
    private static IReadOnlyList<DashboardStatusCard> Cards(FamilyCertificationSummary s)=>[Card("Structural",s.StructuralStatus.ToString(),Severity(s.StructuralStatus),s.BlockingIssues.Count,s.Warnings.Count),Card("Semantic",s.SemanticStatus.ToString(),Severity(s.SemanticStatus),s.BlockingIssues.Count,s.Warnings.Count),Card("Quality",s.QualityStatus.ToString(),Severity(s.QualityStatus),s.BlockingIssues.Count,s.Warnings.Count),Card("Certification Decision",s.CertificationDecision.ToString(),s.CertificationDecision==CertificationDecision.NotCertified?DashboardSeverity.Error:s.CertificationDecision==CertificationDecision.Certified?DashboardSeverity.Success:s.CertificationDecision==CertificationDecision.NotEvaluated?DashboardSeverity.Neutral:DashboardSeverity.Warning,s.BlockingIssues.Count,s.Warnings.Count),Card("Publication Decision",s.PublicationDecision.ToString(),s.PublicationDecision==PublicationDecision.DoNotPublish?DashboardSeverity.Error:s.PublicationDecision==PublicationDecision.Publish?DashboardSeverity.Success:s.PublicationDecision==PublicationDecision.NotEvaluated?DashboardSeverity.Neutral:DashboardSeverity.Warning,s.BlockingIssues.Count,s.Warnings.Count)];
    private static DashboardStatusCard Card(string label,string status,DashboardSeverity sev,int b,int w)=>new(status,label,sev,$"{label} is {status}.",b,w);
    private static DashboardSeverity Severity(CertificationStatus s)=>s switch{CertificationStatus.Passed=>DashboardSeverity.Success,CertificationStatus.PassedWithWarnings=>DashboardSeverity.Warning,CertificationStatus.Failed=>DashboardSeverity.Error,_=>DashboardSeverity.Neutral};
    private static string ResolveDisplayName(ISemanticFactCatalog catalog, string factId) { try { return catalog.ResolveFactId(factId).DisplayName; } catch (KeyNotFoundException) { return factId; } }
}

public sealed class CertificationReportWriter(ICertificationPathService paths, ICertificationDashboardMapper dashboardMapper, IOptions<CertificationOptions>? options=null) : ICertificationReportWriter
{
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly CertificationOptions _options = options?.Value ?? new CertificationOptions();
    public async Task WritePhaseResultAsync(FamilyCertificationContext context, PhaseCertificationResult result, CancellationToken cancellationToken) => await WriteJsonAtomicAsync(paths.GetPhasePath(context,result.PhaseNumber), result with { SchemaVersion = CertificationSummaryAggregator.SchemaVersion }, cancellationToken);
    public async Task WriteSummaryAsync(FamilyCertificationContext context, FamilyCertificationSummary summary, CancellationToken cancellationToken)
    {
        var reportPaths = new Dictionary<string,string?>(summary.ReportPaths) { ["summary"] = paths.GetSummaryPath(context) };
        if (_options.WriteDashboardJson) reportPaths["dashboard"] = paths.GetDashboardPath(context);
        if (_options.WriteMarkdownReport) reportPaths["markdown"] = paths.GetMarkdownReportPath(context);
        foreach (var p in summary.Phases) reportPaths[$"phase{p.PhaseNumber:00}"] = paths.GetPhasePath(context,p.PhaseNumber);
        var enriched = summary with { ReportPaths = reportPaths };
        await WriteJsonAtomicAsync(paths.GetSummaryPath(context), enriched, cancellationToken);
        if (_options.WriteDashboardJson) await WriteJsonAtomicAsync(paths.GetDashboardPath(context), dashboardMapper.Map(context,enriched), cancellationToken);
        if (_options.WriteMarkdownReport) await WriteTextAtomicAsync(paths.GetMarkdownReportPath(context), BuildMarkdown(enriched), cancellationToken);
    }
    private static JsonSerializerOptions CreateOptions(){ var o=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}; o.Converters.Add(new JsonStringEnumConverter()); return o; }
    private static async Task WriteJsonAtomicAsync<T>(string path,T value,CancellationToken ct)=> await WriteTextAtomicAsync(path, JsonSerializer.Serialize(value,JsonOptions), ct);
    private static async Task WriteTextAtomicAsync(string path,string content,CancellationToken ct){ Directory.CreateDirectory(Path.GetDirectoryName(path)!); var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp"; await File.WriteAllTextAsync(tmp,content,new UTF8Encoding(false),ct); File.Move(tmp,path,true); }
    private static string BuildMarkdown(FamilyCertificationSummary s){ var sb=new StringBuilder(); sb.AppendLine("# Astronomy Family Certification Report").AppendLine().AppendLine("## Run Identity").AppendLine($"- Plan: {s.PlanId}").AppendLine($"- Event: {s.EventTitle}").AppendLine($"- EventType: {s.EventType}").AppendLine($"- Family: {s.FamilyId}").AppendLine($"- Language: {s.Language}").AppendLine($"- Region: {s.RegionId}").AppendLine($"- Generated UTC: {s.GeneratedUtc:O}").AppendLine().AppendLine("## Overall Decision").AppendLine($"- Structural: {s.StructuralStatus}").AppendLine($"- Semantic: {s.SemanticStatus}").AppendLine($"- Quality: {s.QualityStatus}").AppendLine($"- Certification Decision: {s.CertificationDecision}").AppendLine($"- Publication Decision: {s.PublicationDecision}").AppendLine().AppendLine("## Phase Results").AppendLine("| Phase | Name | Structural | Semantic | Quality | Blocking | Warnings |").AppendLine("|---:|---|---|---|---|---:|---:|"); foreach(var p in s.Phases.OrderBy(p=>p.PhaseNumber)) sb.AppendLine($"| {p.PhaseNumber} | {p.PhaseName} | {p.StructuralStatus} | {p.SemanticStatus} | {p.QualityStatus} | {p.Issues.Count(i=>i.IsBlocking)} | {p.Warnings.Count} |"); sb.AppendLine().AppendLine("## Required Semantic Facts").AppendLine("| Fact | Resolved | Projected | Retained | Beat Assigned | Narration Evidence |").AppendLine("|---|---|---|---|---|---|"); foreach(var f in s.Phases.SelectMany(p=>p.SemanticFacts).OrderBy(f=>f.FactId)) sb.AppendLine($"| {f.FactId} | {f.Resolved} | {f.Projected} | {f.Retained} | {f.BeatAssigned} | {f.NarrationEvidenceFound} |"); sb.AppendLine().AppendLine("## Blocking Issues"); foreach(var i in s.BlockingIssues) sb.AppendLine($"- {i.Code}; {i.Category}; {i.Message}; {i.ArtifactPath ?? i.SemanticFactId}"); sb.AppendLine().AppendLine("## Warnings"); foreach(var w in s.Warnings) sb.AppendLine($"- {w}"); sb.AppendLine().AppendLine("## Generated Certification Artifacts"); foreach(var l in s.ReportPaths.OrderBy(x=>x.Key)) sb.AppendLine($"- {l.Key}: {l.Value}"); return sb.ToString(); }
}

public sealed class CertificationCoordinator(IEnumerable<IPhaseCertifier> certifiers, IFamilyCertificationProfileRegistry profiles, ICertificationReportWriter writer, ICertificationSummaryAggregator aggregator, ICertificationOutputLock outputLock, ILogger<CertificationCoordinator>? logger=null) : ICertificationCoordinator
{
    private readonly ILogger<CertificationCoordinator> _logger = logger ?? NullLogger<CertificationCoordinator>.Instance;
    public async Task<FamilyCertificationSummary> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context); await using var l=await outputLock.AcquireAsync(context.OutputRoot,cancellationToken);
        var profile=profiles.Resolve(context.EventType); var ordered=SelectCertifiers(context).ToArray(); var results=new List<PhaseCertificationResult>();
        foreach(var c in ordered){ cancellationToken.ThrowIfCancellationRequested(); try{ var r=await c.CertifyAsync(context,cancellationToken); results.Add(r); await writer.WritePhaseResultAsync(context,r,cancellationToken);} catch(OperationCanceledException){throw;} catch(Exception ex){ _logger.LogWarning(ex,"Certification phase {PhaseNumber} failed with unexpected exception",c.PhaseNumber); var r=ExceptionResult(c.PhaseNumber,ex); results.Add(r); await writer.WritePhaseResultAsync(context,r,cancellationToken);} }
        var summary=aggregator.Aggregate(context,profile.FamilyId,results,DateTimeOffset.UtcNow); await writer.WriteSummaryAsync(context,summary,cancellationToken); return summary;
    }
    private IEnumerable<IPhaseCertifier> SelectCertifiers(FamilyCertificationContext c){ var groups=certifiers.GroupBy(x=>x.PhaseNumber).ToArray(); var dup=groups.FirstOrDefault(g=>g.Count()>1); if(dup!=null) throw new InvalidOperationException($"Duplicate phase certifier registration for phase {dup.Key}."); var map=groups.ToDictionary(g=>g.Key,g=>g.Single()); for(var p=c.RequestedStartPhase;p<=c.RequestedEndPhase;p++){ if(!map.TryGetValue(p,out var cert)) throw new InvalidOperationException($"Missing phase certifier registration for requested phase {p}."); yield return cert; } }
    private static void ValidateContext(FamilyCertificationContext c){ ArgumentNullException.ThrowIfNull(c); if(string.IsNullOrWhiteSpace(c.OutputRoot)) throw new ArgumentException("OutputRoot is required."); if(string.IsNullOrWhiteSpace(c.ValidationRoot)) throw new ArgumentException("ValidationRoot is required."); if(c.RequestedStartPhase<1) throw new ArgumentException("RequestedStartPhase must be >= 1."); if(c.RequestedEndPhase>7) throw new ArgumentException("RequestedEndPhase must be <= 7."); if(c.RequestedStartPhase>c.RequestedEndPhase) throw new ArgumentException("RequestedStartPhase must be <= RequestedEndPhase."); }
    private static PhaseCertificationResult ExceptionResult(int phase,Exception ex)=>new(){PhaseNumber=phase,PhaseName=$"Phase {phase}",StructuralStatus=CertificationStatus.Failed,SemanticStatus=CertificationStatus.Failed,QualityStatus=CertificationStatus.Failed,Issues=[new CertificationIssue{Category=CertificationIssueCategory.ImplementationFailure,Code="CERT.PhaseExecutionException",Message=$"Phase {phase} threw {ex.GetType().Name}: {Safe(ex.Message)}",IsBlocking=true,Source="CertificationCoordinator"}],GeneratedUtc=DateTimeOffset.UtcNow};
    private static string Safe(string message) => string.IsNullOrWhiteSpace(message) ? "Unexpected certification phase failure." : message.Replace("secret", "[redacted]", StringComparison.OrdinalIgnoreCase).Replace("token", "[redacted]", StringComparison.OrdinalIgnoreCase);
}
