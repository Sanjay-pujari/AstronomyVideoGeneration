using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Severity of a deterministic documentary-blueprint editorial finding.</summary>
public enum DocumentaryBlueprintValidationSeverity { Error, Warning }

/// <summary>An immutable, machine-addressable editorial validation finding.</summary>
public sealed record DocumentaryBlueprintValidationFinding
{
    public DocumentaryBlueprintValidationFinding(string ruleCode, DocumentaryBlueprintValidationSeverity severity,
        string message, string blueprintId, string? sceneId = null, int? sceneNumber = null, string? fieldName = null)
    {
        RuleCode = Guard.Required(ruleCode, nameof(ruleCode));
        Guard.Enum(severity, nameof(severity));
        Severity = severity;
        Message = Guard.Required(message, nameof(message));
        BlueprintId = Guard.Required(blueprintId, nameof(blueprintId));
        SceneId = Guard.OptionalIdentifier(sceneId, nameof(sceneId));
        FieldName = Guard.OptionalIdentifier(fieldName, nameof(fieldName));
        SceneNumber = sceneNumber;
    }

    public string RuleCode { get; }
    public DocumentaryBlueprintValidationSeverity Severity { get; }
    public string Message { get; }
    public string BlueprintId { get; }
    public string? SceneId { get; }
    public int? SceneNumber { get; }
    public string? FieldName { get; }
}

/// <summary>An immutable ordered result of editorially validating one blueprint.</summary>
public sealed class DocumentaryBlueprintValidationResult
{
    public DocumentaryBlueprintValidationResult(string blueprintId, IReadOnlyList<DocumentaryBlueprintValidationFinding> findings)
    {
        BlueprintId = Guard.Required(blueprintId, nameof(blueprintId));
        Findings = Guard.Copy(findings, nameof(findings));
        if (Findings.Any(f => !string.Equals(f.BlueprintId, BlueprintId, StringComparison.Ordinal)))
            throw new ArgumentException("Every finding must identify the result blueprint.", nameof(findings));
    }

    public string BlueprintId { get; }
    public IReadOnlyList<DocumentaryBlueprintValidationFinding> Findings { get; }
    public bool IsValid => ErrorCount == 0;
    public int ErrorCount => Findings.Count(f => f.Severity == DocumentaryBlueprintValidationSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == DocumentaryBlueprintValidationSeverity.Warning);
}

/// <summary>Stable identifiers for the complete approved O2.3 rule inventory.</summary>
public static class DocumentaryBlueprintEditorialRuleCodes
{
    public const string ScenesRequired = "DBP-EDITORIAL-001";
    public const string PositiveSceneNumbers = "DBP-EDITORIAL-002";
    public const string ContinuousSceneNumbers = "DBP-EDITORIAL-003";
    public const string SceneCollectionOrder = "DBP-EDITORIAL-004";
    public const string KnowledgeRequired = "DBP-EDITORIAL-005";
    public const string ExactlyOnePrimaryKnowledge = "DBP-EDITORIAL-006";
    public const string UniqueKnowledgeIds = "DBP-EDITORIAL-007";
    public const string UniqueViewerQuestions = "DBP-EDITORIAL-008";
    public const string OpeningRole = "DBP-EDITORIAL-009";
    public const string ClosingRole = "DBP-EDITORIAL-010";
    public const string ProductiveCriticalScene = "DBP-EDITORIAL-011";
    public const string PracticalGuidance = "DBP-EDITORIAL-012";
    public const string ClosingEmotionalPayoff = "DBP-EDITORIAL-013";
    public const string ScientificVisualKnowledge = "DBP-EDITORIAL-014";
    public const string PositiveTotalDuration = "DBP-EDITORIAL-015";
    public const string ZeroDurationScene = "DBP-EDITORIAL-016";

    public static IReadOnlyList<(string Code, DocumentaryBlueprintValidationSeverity Severity)> Inventory { get; } =
        new ReadOnlyCollection<(string, DocumentaryBlueprintValidationSeverity)>(new[] {
            (ScenesRequired, E), (PositiveSceneNumbers, E), (ContinuousSceneNumbers, E), (SceneCollectionOrder, E),
            (KnowledgeRequired, E), (ExactlyOnePrimaryKnowledge, E), (UniqueKnowledgeIds, E), (UniqueViewerQuestions, W),
            (OpeningRole, W), (ClosingRole, W), (ProductiveCriticalScene, W), (PracticalGuidance, E),
            (ClosingEmotionalPayoff, W), (ScientificVisualKnowledge, E), (PositiveTotalDuration, E), (ZeroDurationScene, W) });
    private const DocumentaryBlueprintValidationSeverity E = DocumentaryBlueprintValidationSeverity.Error;
    private const DocumentaryBlueprintValidationSeverity W = DocumentaryBlueprintValidationSeverity.Warning;
}

/// <summary>Stateless deterministic inspection of the approved O2.3 editorial rules.</summary>
public sealed class DocumentaryBlueprintEditorialValidator
{
    public DocumentaryBlueprintValidationResult Validate(DocumentaryBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var findings = new List<DocumentaryBlueprintValidationFinding>();
        var scenes = blueprint.Scenes;
        var ordered = scenes.OrderBy(s => s.SceneNumber).ThenBy(s => s.SceneId, StringComparer.Ordinal).ToArray();
        void Add(string code, DocumentaryBlueprintValidationSeverity severity, string message,
            DocumentarySceneBlueprint? scene = null, string? field = null) => findings.Add(new(code, severity, message,
                blueprint.BlueprintId, scene?.SceneId, scene?.SceneNumber, field));

        // Explicit calls below are the certified rule execution order.
        if (scenes.Count == 0) Add(DocumentaryBlueprintEditorialRuleCodes.ScenesRequired, E, "Blueprint must contain at least one scene.");
        foreach (var s in ordered.Where(s => s.SceneNumber <= 0)) Add(DocumentaryBlueprintEditorialRuleCodes.PositiveSceneNumbers, E, "Scene number must be positive.", s, nameof(s.SceneNumber));
        if (scenes.Count > 0 && !ordered.Select(s => s.SceneNumber).SequenceEqual(Enumerable.Range(1, scenes.Count))) Add(DocumentaryBlueprintEditorialRuleCodes.ContinuousSceneNumbers, E, "Scene numbers must form a continuous sequence from 1.");
        if (!scenes.Select(s => s.SceneNumber).SequenceEqual(scenes.Select(s => s.SceneNumber).OrderBy(n => n))) Add(DocumentaryBlueprintEditorialRuleCodes.SceneCollectionOrder, E, "Scene collection order must match ascending scene-number order.");
        foreach (var s in ordered.Where(s => s.KnowledgeReferences.Count == 0)) Add(DocumentaryBlueprintEditorialRuleCodes.KnowledgeRequired, E, "Scene must reference knowledge.", s, nameof(s.KnowledgeReferences));
        foreach (var s in ordered.Where(s => s.KnowledgeReferences.Count(k => k.IsPrimary) != 1)) Add(DocumentaryBlueprintEditorialRuleCodes.ExactlyOnePrimaryKnowledge, E, "Scene must have exactly one primary knowledge reference.", s, nameof(s.KnowledgeReferences));
        foreach (var s in ordered.Where(s => s.KnowledgeReferences.Select(k => k.KnowledgeEntryId).Distinct(StringComparer.Ordinal).Count() != s.KnowledgeReferences.Count)) Add(DocumentaryBlueprintEditorialRuleCodes.UniqueKnowledgeIds, E, "Knowledge reference IDs must be unique within a scene.", s, nameof(s.KnowledgeReferences));
        foreach (var group in ordered.GroupBy(s => s.ViewerQuestion.Text, StringComparer.Ordinal).Where(g => g.Count() > 1).OrderBy(g => g.Key, StringComparer.Ordinal)) Add(DocumentaryBlueprintEditorialRuleCodes.UniqueViewerQuestions, W, $"Viewer question is repeated across scenes: '{group.Key}'.");
        var opening = ordered.FirstOrDefault(s => s.SceneNumber == 1);
        if (opening is not null && opening.SceneRole is not (DocumentarySceneRole.OpeningHook or DocumentarySceneRole.Orientation)) Add(DocumentaryBlueprintEditorialRuleCodes.OpeningRole, W, "Opening scene should use an opening role.", opening, nameof(opening.SceneRole));
        var closing = ordered.LastOrDefault();
        if (closing is not null && closing.SceneRole is not (DocumentarySceneRole.ReflectiveClosing or DocumentarySceneRole.PracticalObservation)) Add(DocumentaryBlueprintEditorialRuleCodes.ClosingRole, W, "Closing scene should provide closure.", closing, nameof(closing.SceneRole));
        foreach (var s in ordered.Where(s => s.EditorialPriority == EditorialPriority.Critical && !s.EditorialOutcome.IntroducesNewKnowledge && !s.EditorialOutcome.DeepensUnderstanding)) Add(DocumentaryBlueprintEditorialRuleCodes.ProductiveCriticalScene, W, "Critical scene should introduce or deepen understanding.", s, nameof(s.EditorialOutcome));
        foreach (var s in ordered.Where(s => s.SceneRole == DocumentarySceneRole.PracticalObservation && !s.EditorialOutcome.ProvidesPracticalGuidance)) Add(DocumentaryBlueprintEditorialRuleCodes.PracticalGuidance, E, "Practical-observation scene must provide practical guidance.", s, nameof(s.EditorialOutcome));
        foreach (var s in ordered.Where(s => s.SceneRole == DocumentarySceneRole.ReflectiveClosing && !s.EditorialOutcome.DeliversEmotionalPayoff)) Add(DocumentaryBlueprintEditorialRuleCodes.ClosingEmotionalPayoff, W, "Reflective closing should deliver emotional payoff.", s, nameof(s.EditorialOutcome));
        foreach (var s in ordered) foreach (var visual in s.VisualOpportunities.Where(v => v.IsScientificallyRequired && v.KnowledgeEntryId is null)) Add(DocumentaryBlueprintEditorialRuleCodes.ScientificVisualKnowledge, E, "Scientifically required visual must reference knowledge.", s, nameof(s.VisualOpportunities));
        long duration = scenes.Sum(s => (long)s.EstimatedDurationSeconds);
        if (duration <= 0) Add(DocumentaryBlueprintEditorialRuleCodes.PositiveTotalDuration, E, "Estimated blueprint duration must be positive.");
        foreach (var s in ordered.Where(s => s.EstimatedDurationSeconds == 0)) Add(DocumentaryBlueprintEditorialRuleCodes.ZeroDurationScene, W, "Zero-duration scene should be reviewed.", s, nameof(s.EstimatedDurationSeconds));
        return new DocumentaryBlueprintValidationResult(blueprint.BlueprintId, findings);
    }

    private const DocumentaryBlueprintValidationSeverity E = DocumentaryBlueprintValidationSeverity.Error;
    private const DocumentaryBlueprintValidationSeverity W = DocumentaryBlueprintValidationSeverity.Warning;
}
