namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionValidationComparer
{
    public DocumentaryNarrativeRevisionValidationComparison Compare(DocumentaryNarrativeDraftValidationResult source, DocumentaryNarrativeDraftValidationResult revised)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(revised);
        var revisedAvailable = Counts(revised.Findings);
        var remaining = new List<DocumentaryNarrativeDraftValidationFinding>(); var resolved = new List<DocumentaryNarrativeDraftValidationFinding>();
        foreach (var finding in source.Findings) { var key = Identity.From(finding); if (Take(revisedAvailable, key)) remaining.Add(finding); else resolved.Add(finding); }
        var sourceAvailable = Counts(source.Findings);
        var introduced = new List<DocumentaryNarrativeDraftValidationFinding>();
        foreach (var finding in revised.Findings) if (!Take(sourceAvailable, Identity.From(finding))) introduced.Add(finding);
        return new(source.Findings.Count, revised.Findings.Count, resolved.Count, remaining.Count, introduced.Count,
            source.Findings.Select(x => x.RuleCode).ToArray(), revised.Findings.Select(x => x.RuleCode).ToArray(),
            resolved.Select(x => x.RuleCode).ToArray(), remaining.Select(x => x.RuleCode).ToArray(), introduced.Select(x => x.RuleCode).ToArray(),
            revised.Findings.Count < source.Findings.Count && introduced.Count == 0,
            revised.Findings.Count > source.Findings.Count || introduced.Count > 0, revised.Findings.Count == 0);
    }
    private static Dictionary<Identity, int> Counts(IEnumerable<DocumentaryNarrativeDraftValidationFinding> findings) => findings.GroupBy(Identity.From).ToDictionary(x => x.Key, x => x.Count());
    private static bool Take(Dictionary<Identity, int> counts, Identity identity) { if (!counts.TryGetValue(identity, out var count) || count == 0) return false; counts[identity] = count - 1; return true; }
    private readonly record struct Identity(string RuleCode, bool IsDraftScoped, string? SectionId, int? SectionNumber,
        string? PassageId, int? PassageNumber, string? FieldName, string Message, DocumentaryNarrativeDraftValidationSeverity Severity)
    {
        public static Identity From(DocumentaryNarrativeDraftValidationFinding finding) => new(finding.RuleCode,
            finding.SectionId is null && finding.PassageId is null, finding.SectionId, finding.SectionNumber,
            finding.PassageId, finding.PassageNumber, finding.FieldName, finding.Message, finding.Severity);
    }
}
