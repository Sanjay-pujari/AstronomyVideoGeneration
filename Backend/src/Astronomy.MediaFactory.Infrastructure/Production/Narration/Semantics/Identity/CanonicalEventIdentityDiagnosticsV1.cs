namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

public sealed record CanonicalEventIdentityDiagnosticsV1(
    string? InputEventType,
    string? CanonicalEventType,
    string? CanonicalFamily,
    string? CanonicalProfile,
    string ResolutionSource,
    IReadOnlyList<string> AppliedAliases,
    bool Supported,
    IReadOnlyList<string> DiagnosticMessages)
{
    public static CanonicalEventIdentityDiagnosticsV1 FromIdentity(CanonicalAstronomyEventIdentity identity) => new(
        identity.InputEventType,
        identity.CanonicalEventType,
        identity.CanonicalFamily,
        identity.CanonicalProfile,
        identity.ResolutionSource,
        identity.AppliedAliases,
        identity.Supported,
        identity.DiagnosticMessages);
}
