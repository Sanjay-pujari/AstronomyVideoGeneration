using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core.Certification;

public interface ICertificationArtifactVerifier
{
    Task<IReadOnlyList<ArtifactCertificationResult>> VerifyAsync(FamilyCertificationContext context, IReadOnlyList<PhaseArtifactDefinition> definitions, CancellationToken cancellationToken);
}

public interface ICertificationJsonReader
{
    Task<JsonDocument?> ReadOptionalDocumentAsync(string path, CancellationToken cancellationToken);
    Task<JsonDocument> ReadRequiredDocumentAsync(string path, CancellationToken cancellationToken);
}

public sealed class CertificationJsonReader : ICertificationJsonReader
{
    private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    public async Task<JsonDocument?> ReadOptionalDocumentAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream, Options, cancellationToken);
    }
    public async Task<JsonDocument> ReadRequiredDocumentAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Certification JSON artifact was not found.", path);
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream, Options, cancellationToken);
    }
}

public sealed class CertificationArtifactVerifier : ICertificationArtifactVerifier
{
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
            var nonEmpty = !definition.RequireNonEmpty || (exists && length > 0);
            var valid = exists || !definition.Required;
            string? message = null;
            if (!exists) message = definition.Required ? "Required artifact is missing." : "Optional artifact was not generated.";
            else if (!nonEmpty) { valid = false; message = "Artifact is empty."; }
            else if (definition.ValidateJson)
            {
                try { await using var s = File.OpenRead(expectedPath); await JsonDocument.ParseAsync(s, cancellationToken: cancellationToken); }
                catch (JsonException ex) { valid = false; message = "Artifact is not valid JSON: " + ex.Message; }
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
            7 => [Req("phase-07-validation",7,"narration/generator-preflight-diagnostics.json"), Req("narration-validation-diagnostics",7,"narration/narration-validation-diagnostics.json"), Opt("long-documentary-script",7,"narration/long/documentary-script.json"), Opt("short-documentary-script",7,"narration/short/documentary-script.json"), Opt("long-scene-fact-cards",7,"narration/long/scene-fact-cards.json"), Opt("short-scene-fact-cards",7,"narration/short/scene-fact-cards.json")],
            _ => []
        };
    }
    private static PhaseArtifactDefinition Req(string id,int p,string path)=>new(){ArtifactId=id,PhaseNumber=p,RelativePath=path,Required=true,ValidateJson=true,RequireNonEmpty=true};
    private static PhaseArtifactDefinition Opt(string id,int p,string path)=>new(){ArtifactId=id,PhaseNumber=p,RelativePath=path,Required=false,ValidateJson=true,RequireNonEmpty=true};
}

public static class CertificationPathHelpers
{
    public static string ResolveArtifactPath(FamilyCertificationContext context, string relativePath) => Path.GetFullPath(Path.Combine(context.OutputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
    public static CertificationStatus Combine(params CertificationStatus[] statuses) => statuses.Contains(CertificationStatus.Failed) ? CertificationStatus.Failed : statuses.Contains(CertificationStatus.PassedWithWarnings) ? CertificationStatus.PassedWithWarnings : statuses.Contains(CertificationStatus.Passed) ? CertificationStatus.Passed : statuses.Contains(CertificationStatus.NotEvaluated) ? CertificationStatus.NotEvaluated : CertificationStatus.NotApplicable;
}

public abstract class PhaseCertifierBase(IPhaseArtifactRegistry registry, ICertificationArtifactVerifier verifier) : IPhaseCertifier
{
    public abstract int PhaseNumber { get; }
    protected abstract string PhaseName { get; }
    public virtual async Task<PhaseCertificationResult> CertifyAsync(FamilyCertificationContext context, CancellationToken cancellationToken)
    {
        var artifacts = await verifier.VerifyAsync(context, registry.GetDefinitions(PhaseNumber, context), cancellationToken);
        var issues = artifacts.Where(a => a.Required && (!a.Exists || !a.IsNonEmpty || !a.IsValid)).Select(a => new CertificationIssue { Category = !a.Exists ? CertificationIssueCategory.MissingArtifact : !a.IsNonEmpty ? CertificationIssueCategory.EmptyArtifact : CertificationIssueCategory.InvalidArtifact, Code = $"CG1A_PHASE{PhaseNumber:00}_ARTIFACT_INVALID", Message = a.ValidationMessage ?? "Required artifact failed certification.", ArtifactPath = a.ExpectedPath, IsBlocking = true, Source = nameof(CertificationArtifactVerifier) }).ToArray();
        var structural = CertificationStatusAggregator.FromArtifacts(artifacts);
        return new PhaseCertificationResult { PhaseNumber = PhaseNumber, PhaseName = PhaseName, StructuralStatus = structural, SemanticStatus = CertificationStatus.NotEvaluated, QualityStatus = CertificationStatus.NotEvaluated, Artifacts = artifacts, Issues = issues, Warnings = artifacts.Where(a => !a.Required && !a.Exists).Select(a => $"Optional artifact not present: {a.ArtifactId}.").ToArray(), GeneratedUtc = DateTimeOffset.UtcNow };
    }
}
public sealed class Phase1Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 1; protected override string PhaseName => "Run Setup / Plan Selection"; }
public sealed class Phase2Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 2; protected override string PhaseName => "Domain Intelligence"; }
public sealed class Phase3Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 3; protected override string PhaseName => "Question / Story Planning"; }
public sealed class Phase4Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 4; protected override string PhaseName => "Story Intelligence"; }
public sealed class Phase5Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 5; protected override string PhaseName => "Editorial Intelligence"; }
public sealed class Phase6Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 6; protected override string PhaseName => "Creative Intelligence / Story Frames"; }
public sealed class Phase7Certifier(IPhaseArtifactRegistry r, ICertificationArtifactVerifier v) : PhaseCertifierBase(r,v) { public override int PhaseNumber => 7; protected override string PhaseName => "Narration Studio V5"; }

public static class CgA1CertificationTask2ServiceCollectionExtensions
{
    public static IServiceCollection AddCgA1PhaseCertification(this IServiceCollection services)
    {
        services.AddSingleton<ICertificationArtifactVerifier, CertificationArtifactVerifier>();
        services.AddSingleton<ICertificationJsonReader, CertificationJsonReader>();
        services.AddSingleton<IPhaseArtifactRegistry, PhaseArtifactRegistry>();
        services.AddSingleton<IPhaseCertifier, Phase1Certifier>(); services.AddSingleton<IPhaseCertifier, Phase2Certifier>(); services.AddSingleton<IPhaseCertifier, Phase3Certifier>(); services.AddSingleton<IPhaseCertifier, Phase4Certifier>(); services.AddSingleton<IPhaseCertifier, Phase5Certifier>(); services.AddSingleton<IPhaseCertifier, Phase6Certifier>(); services.AddSingleton<IPhaseCertifier, Phase7Certifier>();
        return services;
    }
}
