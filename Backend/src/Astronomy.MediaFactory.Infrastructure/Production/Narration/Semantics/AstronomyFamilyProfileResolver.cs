using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public interface IAstronomyFamilyProfileResolver
{
    AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(AstronomyFamilyProfileResolutionInput input);
}

public sealed class AstronomyFamilyProfileResolver : IAstronomyFamilyProfileResolver
{
    public AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(AstronomyFamilyProfileResolutionInput input) => AstronomyFamilyProfileCatalog.ResolveFamilyProfile(input);
}
