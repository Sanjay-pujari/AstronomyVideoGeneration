namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

public interface IAstronomyFamilyProfileCatalogV1
{
    IReadOnlyCollection<AstronomyFamilyProfileV1> Profiles { get; }
    bool TryGet(string familyId, out AstronomyFamilyProfileV1 profile);
    AstronomyFamilyProfileV1 GetRequired(string familyId);
    AstronomyFamilyResolutionV1 ResolveEventType(string eventType);
    FamilyProfileValidationResult Validate();
    bool IsActiveV1Family(string familyId);
    bool IsFutureFamily(string familyId);
}
