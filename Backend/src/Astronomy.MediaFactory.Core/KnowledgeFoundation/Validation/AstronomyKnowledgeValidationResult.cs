using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

/// <summary>Immutable aggregate result for typed knowledge validation.</summary>
public sealed class AstronomyKnowledgeValidationResult
{
    public static AstronomyKnowledgeValidationResult Success { get; } = new(Array.Empty<AstronomyKnowledgeValidationIssue>());
    public AstronomyKnowledgeValidationResult(IEnumerable<AstronomyKnowledgeValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var ordered = issues.Select(i => i ?? throw new ArgumentException("Validation issues cannot contain null entries.", nameof(issues)))
            .Distinct()
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Code, StringComparer.Ordinal)
            .ThenBy(i => i.Path, StringComparer.Ordinal)
            .ThenBy(i => i.RuleId, StringComparer.Ordinal)
            .ThenBy(i => i.Message, StringComparer.Ordinal)
            .ToArray();
        Issues = Array.AsReadOnly(ordered);
        InformationCount = ordered.Count(i => i.Severity == AstronomyKnowledgeValidationSeverity.Information);
        WarningCount = ordered.Count(i => i.Severity == AstronomyKnowledgeValidationSeverity.Warning);
        ErrorCount = ordered.Count(i => i.Severity == AstronomyKnowledgeValidationSeverity.Error);
        CriticalCount = ordered.Count(i => i.Severity == AstronomyKnowledgeValidationSeverity.Critical);
    }
    public IReadOnlyList<AstronomyKnowledgeValidationIssue> Issues { get; }
    public bool IsValid => ErrorCount == 0 && CriticalCount == 0;
    public bool HasWarnings => WarningCount > 0;
    public bool HasErrors => ErrorCount > 0;
    public bool HasCriticalIssues => CriticalCount > 0;
    public int InformationCount { get; }
    public int WarningCount { get; }
    public int ErrorCount { get; }
    public int CriticalCount { get; }
    public AstronomyKnowledgeValidationResult Merge(AstronomyKnowledgeValidationResult other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new AstronomyKnowledgeValidationResult(Issues.Concat(other.Issues));
    }
}
