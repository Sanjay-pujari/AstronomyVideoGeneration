namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

/// <summary>Strong identifier for a validation run.</summary>
public readonly record struct AstronomyKnowledgeValidationRunId
{
    public AstronomyKnowledgeValidationRunId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Validation run ID is required.", nameof(value));
        Value = value.Trim();
    }
    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value ?? string.Empty;
}
