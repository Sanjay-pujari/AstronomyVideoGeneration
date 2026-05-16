using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

[Obsolete("Deprecated: active production thumbnails use LocalAssetCollageThumbnailService with curated local assets.")]
public sealed class CinematicThumbnailAiService : ICinematicThumbnailAiService
{
    private readonly IThumbnailMoodGradingService _moodGradingService;
    private readonly ThumbnailCinematicAIOptions _options;

    public CinematicThumbnailAiService(IThumbnailMoodGradingService moodGradingService, IOptions<ThumbnailCinematicAIOptions> options)
    {
        _moodGradingService = moodGradingService;
        _options = options.Value;
    }

    public Task<CinematicThumbnailAiRecommendation> RecommendAsync(CinematicThumbnailAiRequest request, CancellationToken cancellationToken)
    {
        var dominant = ResolveDominantObject(request.GenerationRequest, request.SelectedCandidate);
        var mood = _moodGradingService.SelectMood(new ThumbnailMoodGradingRequest
        {
            DominantObjectType = dominant.Type,
            EventType = request.GenerationRequest.Context.SpecialEvent?.EventType,
            IsShortForm = request.GenerationRequest.IsShortForm,
            AllowedMoodProfiles = _options.AllowedMoodProfiles
        });

        var objectVisibility = request.SelectedCandidate.ObjectVisibility;
        var focalSize = request.SelectedCandidate.CelestialFocalSize;
        var needsBoost = (_options.EnableObjectFocusEnhancement && objectVisibility < 0.50) || focalSize < 0.34;
        var analyticsBoost = CalculateAnalyticsInfluence(request.GenerationRequest.FeedbackSignals, dominant.Type);
        var requestedBoost = needsBoost
            ? 1.22 + Math.Max(0, 0.34 - focalSize) * 0.55 + analyticsBoost * 0.04
            : 1.08 + analyticsBoost * 0.02;
        var scaleBoost = Math.Round(Math.Clamp(requestedBoost, 1, Math.Max(1, _options.MaximumObjectScaleBoost)), 3);
        var focus = ResolveFocus(request.GenerationRequest, request.TargetWidth, request.TargetHeight);
        var cropStrategy = request.GenerationRequest.IsShortForm && _options.EnablePortraitSafeCropping
            ? "portrait-safe-focal-crop"
            : _options.EnableSmartCropping
                ? dominant.Type.Equals("conjunction", StringComparison.OrdinalIgnoreCase) ? "conjunction-balanced-crop" : "smart-focal-crop"
                : "center-crop";

        var recommendation = new CinematicThumbnailAiRecommendation
        {
            DominantObject = dominant.Name,
            DominantObjectType = dominant.Type,
            MoodProfile = mood.MoodProfile,
            CropStrategy = cropStrategy,
            FocusX = focus.X,
            FocusY = focus.Y,
            ScaleBoost = scaleBoost,
            EnhancementApplied = scaleBoost > 1.001 || _options.EnableColorMoodGrading,
            PortraitSafe = request.GenerationRequest.IsShortForm && _options.EnablePortraitSafeCropping,
            AnalyticsInfluence = analyticsBoost,
            Rationale = "AI-assisted recommendation only; real rendered astronomy frame remains the source of truth and no objects are generated."
        };

        return Task.FromResult(recommendation);
    }

    private static (string Name, string Type) ResolveDominantObject(ThumbnailGenerationRequest request, ThumbnailCandidateScore score)
    {
        var candidates = request.Context.SceneObservationContexts
            .Select(x => new { x.ObjectName, x.ObjectType, x.SceneId })
            .Concat(request.Scenes.Select(x => new { ObjectName = x.ObjectName ?? x.Caption, ObjectType = x.ObjectType ?? x.SceneType, x.SceneId }))
            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectName) || !string.IsNullOrWhiteSpace(x.ObjectType))
            .ToList();

        var matched = candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(score.SceneId) && string.Equals(x.SceneId, score.SceneId, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
        var name = FirstNonEmpty(matched?.ObjectName, request.Context.SpecialEvent?.EventTitle, request.Metadata.PrimaryTitle, "dominant celestial object");
        var haystack = string.Join(' ', name, matched?.ObjectType, request.Context.SpecialEvent?.EventType, request.Context.SpecialEvent?.EventTitle, request.Metadata.PrimaryTitle);
        var type = Classify(haystack);
        return (name, type);
    }

    private static string Classify(string value)
    {
        if (ContainsAny(value, "moon", "lunar", "चांद")) return "moon";
        if (ContainsAny(value, "conjunction", "alignment", "meets")) return "conjunction";
        if (ContainsAny(value, "meteor", "shower", "streak")) return "meteor streak";
        if (ContainsAny(value, "constellation", "orion", "ursa", "scorpius")) return "constellation";
        if (ContainsAny(value, "nebula", "galaxy", "cluster", "deep sky", "messier")) return "deep sky object";
        if (ContainsAny(value, "venus", "jupiter", "saturn", "mars", "mercury")) return "bright planet";
        return "bright planet";
    }

    private static (double X, double Y) ResolveFocus(ThumbnailGenerationRequest request, int width, int height)
    {
        var scene = request.Context.SceneObservationContexts.FirstOrDefault() ?? new SceneObservationContext();
        var azimuth = scene.AzimuthDegrees ?? 180;
        var altitude = scene.AltitudeDegrees ?? 45;
        var x = Math.Clamp(azimuth / 360d, 0.22, 0.78);
        var y = Math.Clamp(1 - (altitude / 90d), request.IsShortForm ? 0.30 : 0.22, request.IsShortForm ? 0.58 : 0.68);
        return (Math.Round(x, 3), Math.Round(y, 3));
    }

    private static double CalculateAnalyticsInfluence(FeedbackSignals? signals, string objectType)
    {
        if (signals is null) return 0;
        var hints = signals.TopKeywords.Concat(signals.BestHooks).ToArray();
        if (hints.Length == 0) return 0;
        var matches = hints.Count(h => h.Contains(objectType, StringComparison.OrdinalIgnoreCase)
            || h.Contains("close", StringComparison.OrdinalIgnoreCase)
            || h.Contains("large", StringComparison.OrdinalIgnoreCase)
            || h.Contains("poster", StringComparison.OrdinalIgnoreCase));
        return Math.Round(Math.Clamp(matches / 5d, 0, 1), 3);
    }

    private static bool ContainsAny(string? value, params string[] needles)
        => !string.IsNullOrWhiteSpace(value) && needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "dominant celestial object";
}
