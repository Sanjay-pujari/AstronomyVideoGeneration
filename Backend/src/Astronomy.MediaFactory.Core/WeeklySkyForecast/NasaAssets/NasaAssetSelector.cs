using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;

public interface INasaAssetSelector
{
    NasaImageCandidate? SelectBest(NasaAssetRequirement requirement, IReadOnlyList<NasaImageCandidate> candidates);
    IReadOnlyList<NasaImageCandidate> SelectCandidates(NasaAssetRequirement requirement, IReadOnlyList<NasaImageCandidate> candidates, int minimumCandidatesToTry);
}

public sealed class NasaAssetSelector(ILogger<NasaAssetSelector> logger) : INasaAssetSelector
{
    private static readonly string[] UnrelatedTerms = ["logo", "patch", "portrait", "headshot", "insignia", "diagram", "chart", "graph", "poster", "artist concept", "illustration"];

    public NasaImageCandidate? SelectBest(NasaAssetRequirement requirement, IReadOnlyList<NasaImageCandidate> candidates) =>
        SelectCandidates(requirement, candidates, 1).FirstOrDefault();

    public IReadOnlyList<NasaImageCandidate> SelectCandidates(NasaAssetRequirement requirement, IReadOnlyList<NasaImageCandidate> candidates, int minimumCandidatesToTry)
    {
        var ranked = candidates
            .Select(candidate => candidate with { Score = Score(requirement, candidate) })
            .Where(candidate => candidate.Score > -20)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.PixelHint)
            .ToList();
        var selected = ranked.Take(Math.Max(1, minimumCandidatesToTry)).ToList();
        foreach (var candidate in selected)
        {
            logger.LogInformation("NASA_IMAGE_CANDIDATE_SELECTED assetCode={AssetCode} nasaId={NasaId} score={Score} title={Title}", requirement.AssetCode, candidate.NasaId, candidate.Score, candidate.Title);
        }
        return selected;
    }

    private static int Score(NasaAssetRequirement requirement, NasaImageCandidate candidate)
    {
        var title = candidate.Title ?? string.Empty;
        var description = candidate.Description ?? string.Empty;
        var keywordText = string.Join(' ', candidate.Keywords ?? []);
        var primary = requirement.PrimaryKeyword;
        var score = 0;
        if (!candidate.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)) score -= 50;
        if (ContainsAny(title, primary, requirement.AssignedObjects)) score += 40;
        if (ContainsAny(description, primary, requirement.AssignedObjects)) score += 25;
        if (!string.IsNullOrWhiteSpace(candidate.Center)) score += 20;
        if (candidate.PixelHint >= 1_000_000 || candidate.PreviewLinks.Any(link => link.Contains("~orig", StringComparison.OrdinalIgnoreCase) || link.Contains("~large", StringComparison.OrdinalIgnoreCase))) score += 10;
        if (ContainsAny(keywordText, primary, requirement.AssignedObjects)) score += 10;
        if (UnrelatedTerms.Any(term => title.Contains(term, StringComparison.OrdinalIgnoreCase) || description.Contains(term, StringComparison.OrdinalIgnoreCase))) score -= 30;
        return score;
    }

    private static bool ContainsAny(string value, string primary, IReadOnlyList<string> objects)
    {
        if (!string.IsNullOrWhiteSpace(primary) && value.Contains(primary, StringComparison.OrdinalIgnoreCase)) return true;
        return objects.Any(obj => !string.IsNullOrWhiteSpace(obj) && value.Contains(Normalize(obj), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => value.Replace('_', ' ').Replace('-', ' ');
}
