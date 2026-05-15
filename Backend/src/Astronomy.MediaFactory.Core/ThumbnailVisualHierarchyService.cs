namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailVisualHierarchyService : IThumbnailVisualHierarchyService
{
    public ThumbnailVisualHierarchyResult Evaluate(ThumbnailVisualHierarchyRequest request)
    {
        var recommendations = new List<string>();
        var objectScore = Math.Clamp(request.SelectedCandidate.ObjectVisibility * 0.58 + request.SelectedCandidate.CelestialFocalSize * 0.42, 0, 1);
        if (objectScore < 0.38) recommendations.Add("Increase safe focal-object emphasis to avoid empty-sky composition.");

        var words = request.HookText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        var wordScore = words == 0 ? 1 : Math.Clamp(1 - Math.Max(0, words - (request.GenerationRequest.IsShortForm ? 4 : 5)) * 0.18, 0.25, 1);
        var textSafeScore = Math.Clamp(request.SelectedCandidate.TextSafeCompositionArea, 0, 1);
        var readability = Math.Round((wordScore * 0.55) + (textSafeScore * 0.45), 3);
        if (readability < 0.68) recommendations.Add("Protect mobile hook readability with darker gradient and reduced text density.");

        var portraitScore = request.PortraitSafe ? 1 : request.GenerationRequest.IsShortForm ? 0.62 : 0.9;
        var hierarchy = Math.Round((objectScore * 0.44) + (readability * 0.34) + (portraitScore * 0.12) + (request.SelectedCandidate.Contrast * 0.10), 3);

        return new ThumbnailVisualHierarchyResult
        {
            VisualHierarchyScore = hierarchy,
            ReadabilityScore = readability,
            Recommendations = recommendations
        };
    }
}
