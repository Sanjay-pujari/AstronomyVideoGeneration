namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryCertificationSummarizer
{
    public DocumentaryCertificationSummary Summarize(DocumentaryCertificationEvaluationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);var provenance=result.ContextProvenance??throw new ArgumentException("Evaluation context is required.",nameof(result));var metadata=result.ContextMetadata??throw new ArgumentException("Evaluation metadata is required.",nameof(result));
        var d=result.Decision;var domains=d.RuleResults.Select(x=>x.Domain).Distinct().ToArray();return new($"{provenance.ProvenanceId}.certification",provenance.PackageId,provenance.ProvenanceId,provenance.ReleaseCandidateId,provenance.ConvergenceId,result.Status,d.TotalRuleCount,d.PassedRuleCount,d.FailedRuleCount,d.Findings.Count,domains,d.RuleResults.Select(x=>x.Rule).ToArray(),metadata.EvaluatedUtc,metadata.EvaluatedBy,result.IsCertified);
    }
}
