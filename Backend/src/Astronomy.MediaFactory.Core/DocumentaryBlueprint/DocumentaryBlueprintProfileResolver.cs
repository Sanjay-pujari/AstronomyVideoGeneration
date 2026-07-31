namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>
/// Thin adapter over profiles registered by the owning catalog/composition root. It deliberately
/// contains no fallback profile data and therefore cannot become a competing profile catalog.
/// </summary>
public sealed class DocumentaryBlueprintProfileResolver(IEnumerable<DocumentaryBlueprintProfile> profiles)
    : IDocumentaryBlueprintProfileResolver
{
    private readonly IReadOnlyList<DocumentaryBlueprintProfile> profiles = profiles.ToArray();

    public DocumentaryBlueprintProfile? Resolve(string profileId, string familyCode, string audienceCode) =>
        profiles.SingleOrDefault(x =>
            string.Equals(x.ProfileId, profileId, StringComparison.Ordinal) &&
            string.Equals(x.FamilyCode, familyCode, StringComparison.Ordinal) &&
            string.Equals(x.AudienceCode, audienceCode, StringComparison.Ordinal));
}
