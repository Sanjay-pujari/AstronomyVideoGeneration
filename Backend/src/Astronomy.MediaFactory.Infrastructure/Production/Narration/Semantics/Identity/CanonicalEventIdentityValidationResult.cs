namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

public sealed record CanonicalEventIdentityValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static CanonicalEventIdentityValidationResult Success { get; } = new(true, [], []);
}
