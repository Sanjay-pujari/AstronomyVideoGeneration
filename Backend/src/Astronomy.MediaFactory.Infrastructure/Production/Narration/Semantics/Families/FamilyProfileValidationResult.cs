using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public sealed record FamilyProfileValidationResult
{
    public FamilyProfileValidationResult(bool isValid, IReadOnlyList<string> errors, IReadOnlyList<string> warnings) : this(isValid, errors.ToImmutableArray(), warnings.ToImmutableArray()) { }
    [JsonConstructor]
    public FamilyProfileValidationResult(bool isValid, ImmutableArray<string> errors, ImmutableArray<string> warnings) { IsValid = isValid; Errors = errors.IsDefault ? [] : errors; Warnings = warnings.IsDefault ? [] : warnings; }
    public bool IsValid { get; init; }
    public ImmutableArray<string> Errors { get; init; }
    public ImmutableArray<string> Warnings { get; init; }
}
