using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Severity of a deterministic narrative-draft quality finding.</summary>
public enum DocumentaryNarrativeDraftValidationSeverity { Error, Warning }

/// <summary>An immutable, machine-addressable narrative-draft quality finding.</summary>
public sealed record DocumentaryNarrativeDraftValidationFinding
{
    public DocumentaryNarrativeDraftValidationFinding(string ruleCode, DocumentaryNarrativeDraftValidationSeverity severity,
        string message, string draftId, string? sectionId = null, int? sectionNumber = null,
        string? passageId = null, int? passageNumber = null, string? fieldName = null)
    {
        RuleCode = Guard.Required(ruleCode, nameof(ruleCode)); Guard.Enum(severity, nameof(severity)); Severity = severity;
        Message = Guard.Required(message, nameof(message)); DraftId = Guard.Required(draftId, nameof(draftId));
        SectionId = Guard.OptionalIdentifier(sectionId, nameof(sectionId)); PassageId = Guard.OptionalIdentifier(passageId, nameof(passageId));
        FieldName = Guard.OptionalIdentifier(fieldName, nameof(fieldName)); SectionNumber = sectionNumber; PassageNumber = passageNumber;
    }
    public string RuleCode { get; }
    public DocumentaryNarrativeDraftValidationSeverity Severity { get; }
    public string Message { get; }
    public string DraftId { get; }
    public string? SectionId { get; }
    public int? SectionNumber { get; }
    public string? PassageId { get; }
    public int? PassageNumber { get; }
    public string? FieldName { get; }
}

/// <summary>An immutable ordered result of validating one narrative draft.</summary>
public sealed class DocumentaryNarrativeDraftValidationResult
{
    public DocumentaryNarrativeDraftValidationResult(string draftId, IReadOnlyList<DocumentaryNarrativeDraftValidationFinding> findings)
    {
        DraftId = Guard.Required(draftId, nameof(draftId)); Findings = Guard.Copy(findings, nameof(findings));
        if (Findings.Any(f => !string.Equals(f.DraftId, DraftId, StringComparison.Ordinal)))
            throw new ArgumentException("Every finding must identify the result draft.", nameof(findings));
    }
    public string DraftId { get; }
    public IReadOnlyList<DocumentaryNarrativeDraftValidationFinding> Findings { get; }
    public bool IsValid => ErrorCount == 0;
    public int ErrorCount => Findings.Count(f => f.Severity == DocumentaryNarrativeDraftValidationSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == DocumentaryNarrativeDraftValidationSeverity.Warning);
}

/// <summary>Stable identifiers for the complete approved O2.6 rule inventory.</summary>
public static class DocumentaryNarrativeDraftRuleCodes
{
    public const string SectionsRequired="DND-QUALITY-001", PassagesRequired="DND-QUALITY-002", PositivePassageNumbers="DND-QUALITY-003";
    public const string PassageNumberMatchesBeat="DND-QUALITY-004", UniquePassageIds="DND-QUALITY-005", UniqueSourceBeatIds="DND-QUALITY-006";
    public const string SourceSceneIdsRequired="DND-QUALITY-007", MinimumThreeWords="DND-QUALITY-008", RecommendedEightWords="DND-QUALITY-009";
    public const string Maximum120Words="DND-QUALITY-010", UppercaseOpening="DND-QUALITY-011", TerminalPunctuation="DND-QUALITY-012";
    public const string UniquePassageText="DND-QUALITY-013", ConsecutiveTitles="DND-QUALITY-014", OpeningType="DND-QUALITY-015";
    public const string ClosingType="DND-QUALITY-016", PositiveTotalDuration="DND-QUALITY-017", PositivePassageDuration="DND-QUALITY-018";
    public static IReadOnlyList<(string Code, DocumentaryNarrativeDraftValidationSeverity Severity)> Inventory { get; } =
        new ReadOnlyCollection<(string, DocumentaryNarrativeDraftValidationSeverity)>(new[] {
            (SectionsRequired,E),(PassagesRequired,E),(PositivePassageNumbers,E),(PassageNumberMatchesBeat,E),(UniquePassageIds,E),(UniqueSourceBeatIds,E),
            (SourceSceneIdsRequired,E),(MinimumThreeWords,E),(RecommendedEightWords,W),(Maximum120Words,E),(UppercaseOpening,W),(TerminalPunctuation,W),
            (UniquePassageText,E),(ConsecutiveTitles,W),(OpeningType,E),(ClosingType,E),(PositiveTotalDuration,E),(PositivePassageDuration,W) });
    private const DocumentaryNarrativeDraftValidationSeverity E=DocumentaryNarrativeDraftValidationSeverity.Error, W=DocumentaryNarrativeDraftValidationSeverity.Warning;
}
