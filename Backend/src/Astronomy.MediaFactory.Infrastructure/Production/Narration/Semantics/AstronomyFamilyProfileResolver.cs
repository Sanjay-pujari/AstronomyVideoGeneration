using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public interface IAstronomyFamilyProfileResolver
{
    AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(AstronomyFamilyProfileResolutionInput input);
    AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalEventIdentity identity);
}

public sealed class AstronomyFamilyProfileResolver : IAstronomyFamilyProfileResolver
{
    public AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(AstronomyFamilyProfileResolutionInput input) => AstronomyFamilyProfileCatalog.ResolveFamilyProfile(input);
    public AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalEventIdentity identity) => AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity);
}
