using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;

public sealed record SemanticCapabilityCatalogValidationResult
{
    public SemanticCapabilityCatalogValidationResult(bool isValid, IReadOnlyList<string> errors) : this(isValid, [.. errors]) { }
    [JsonConstructor]
    public SemanticCapabilityCatalogValidationResult(bool isValid, ImmutableArray<string> errors) { IsValid = isValid; Errors = errors.IsDefault ? [] : errors; }
    public bool IsValid { get; init; }
    public ImmutableArray<string> Errors { get; init; }
}
