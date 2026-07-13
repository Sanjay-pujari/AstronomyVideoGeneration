namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

public sealed record EventIdentityNormalizationResult(
    string? InputEventType,
    string? CanonicalEventType,
    IReadOnlyList<string> AppliedAliases,
    bool Supported,
    IReadOnlyList<string> DiagnosticMessages);

public sealed record CanonicalAstronomyEventIdentity(
    string? InputEventType,
    string? CanonicalEventType,
    string? CanonicalFamily,
    string? CanonicalProfile,
    string ResolutionSource,
    IReadOnlyList<string> AppliedAliases,
    bool Supported,
    IReadOnlyList<string> DiagnosticMessages);
