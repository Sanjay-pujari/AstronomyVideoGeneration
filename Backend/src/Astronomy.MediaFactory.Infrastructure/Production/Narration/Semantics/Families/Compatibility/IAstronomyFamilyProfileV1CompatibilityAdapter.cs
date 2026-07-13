using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;

public interface IAstronomyFamilyProfileV1CompatibilityAdapter
{
    string AdapterId { get; }
    FamilyProfileCompatibilityResult Convert(AstronomyFamilyProfileV1 profile, FamilyProfileCompatibilityContext context);
}

public sealed record FamilyProfileCompatibilityContext(string? InputEventType, string? CanonicalEventType, string? CanonicalFamilyId, bool AliasApplied);
