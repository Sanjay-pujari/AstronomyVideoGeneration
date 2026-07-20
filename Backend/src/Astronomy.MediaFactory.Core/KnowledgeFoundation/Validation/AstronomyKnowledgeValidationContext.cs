using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;

/// <summary>Immutable context supplied to typed knowledge validation rules.</summary>
public sealed class AstronomyKnowledgeValidationContext
{
    public AstronomyKnowledgeValidationContext(AstronomyKnowledgeValidationRunId validationRunId, DateTimeOffset validatedAtUtc, AstronomyKnowledgeValidationMode mode = AstronomyKnowledgeValidationMode.Standard, AstronomyKnowledgeValidationSeverity minimumSeverity = AstronomyKnowledgeValidationSeverity.Information, IEnumerable<string>? tags = null, IReadOnlyDictionary<string, object?>? items = null)
    {
        if (!validationRunId.IsValid) throw new ArgumentException("Validation run ID is required.", nameof(validationRunId));
        if (validatedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Validation timestamp must be UTC.", nameof(validatedAtUtc));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(minimumSeverity)) throw new ArgumentOutOfRangeException(nameof(minimumSeverity));
        ValidationRunId = validationRunId; ValidatedAtUtc = validatedAtUtc; Mode = mode; MinimumSeverity = minimumSeverity;
        Tags = Array.AsReadOnly((tags ?? Array.Empty<string>()).Select(t => (t ?? throw new ArgumentException("Tags cannot contain null entries.", nameof(tags))).Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToArray());
        var dict = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        if (items is not null) foreach (var kv in items) { if (kv.Key is null) throw new ArgumentException("Item keys are required.", nameof(items)); var k=kv.Key.Trim(); if (k.Length==0) throw new ArgumentException("Item keys are required.", nameof(items)); if (!dict.TryAdd(k, kv.Value)) throw new ArgumentException($"Duplicate validation item key '{k}'.", nameof(items)); }
        Items = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(dict);
    }
    public AstronomyKnowledgeValidationRunId ValidationRunId { get; }
    public DateTimeOffset ValidatedAtUtc { get; }
    public AstronomyKnowledgeValidationMode Mode { get; }
    public AstronomyKnowledgeValidationSeverity MinimumSeverity { get; }
    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyDictionary<string, object?> Items { get; }
}
