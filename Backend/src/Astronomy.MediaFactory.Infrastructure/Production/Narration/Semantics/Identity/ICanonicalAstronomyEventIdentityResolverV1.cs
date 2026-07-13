namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

public interface ICanonicalAstronomyEventIdentityResolverV1
{
    CanonicalAstronomyEventIdentity Resolve(string? eventType, string resolutionSource = "ExplicitEventType");
}
