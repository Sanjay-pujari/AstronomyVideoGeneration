namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>
/// Thin adapter over profiles registered by the owning catalog/composition root. It deliberately
/// contains no fallback profile data and therefore cannot become a competing profile catalog.
/// </summary>
public sealed class DocumentaryBlueprintProfileResolver(IEnumerable<DocumentaryBlueprintProfile> profiles)
    : IDocumentaryBlueprintProfileResolver
{
    private readonly IReadOnlyList<DocumentaryBlueprintProfile> profiles = profiles.ToArray();

    public DocumentaryBlueprintProfile? Resolve(string profileId, string familyCode, string audienceCode)
    {
        var matches = profiles.Where(x =>
            string.Equals(x.ProfileId, profileId, StringComparison.Ordinal) &&
            string.Equals(x.FamilyCode, familyCode, StringComparison.Ordinal) &&
            string.Equals(x.AudienceCode, audienceCode, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new DocumentaryBlueprintProfileConfigurationException(profileId, familyCode, audienceCode, matches.Length)
        };
    }
}

public sealed class DocumentaryBlueprintProfileConfigurationException(string profileId, string familyCode,
    string audienceCode, int matchCount) : InvalidOperationException(
        $"Profile registration is ambiguous for '{profileId}/{familyCode}/{audienceCode}': {matchCount} matches.")
{
    public string ProfileId { get; } = profileId;
    public string FamilyCode { get; } = familyCode;
    public string AudienceCode { get; } = audienceCode;
    public int MatchCount { get; } = matchCount;
}
