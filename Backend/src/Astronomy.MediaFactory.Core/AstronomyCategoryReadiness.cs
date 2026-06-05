namespace Astronomy.MediaFactory.Core;

public static class AstronomyOpportunityCategoryCodes
{
    public static readonly IReadOnlyList<string> Phase7CategoryCodes =
    [
        "RareEventAlert",
        "PlanetConjunction",
        "PlanetGrouping",
        "MoonSpecials",
        "PlanetVisibilityGuide",
        "AstroPhotographyGuide",
        "AstroExplainer",
        "WeeklySkyForecast"
    ];
}

public sealed record AstronomyCategoryReadinessDto(
    string CategoryCode,
    bool Exists,
    bool IsActive,
    string? DisplayName,
    bool CanPlan,
    string? Warning);

public sealed record AstronomyCategoryReadinessResult(
    IReadOnlyList<AstronomyCategoryReadinessDto> Categories);

public interface IAstronomyCategoryReadinessService
{
    Task<AstronomyCategoryReadinessResult> GetCategoryReadinessAsync(IReadOnlyList<string>? categoryCodes, CancellationToken cancellationToken);
}
