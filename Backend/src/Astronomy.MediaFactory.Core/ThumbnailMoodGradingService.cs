using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class ThumbnailMoodGradingService : IThumbnailMoodGradingService
{
    private readonly ThumbnailCinematicAIOptions _options;

    public ThumbnailMoodGradingService(IOptions<ThumbnailCinematicAIOptions> options) => _options = options.Value;

    public ThumbnailMoodGradingResult SelectMood(ThumbnailMoodGradingRequest request)
    {
        var allowed = request.AllowedMoodProfiles.Count > 0 ? request.AllowedMoodProfiles : _options.AllowedMoodProfiles;
        var requested = ResolveMood(request.DominantObjectType, request.EventType);
        var profile = allowed.Contains(requested, StringComparer.OrdinalIgnoreCase)
            ? requested
            : allowed.FirstOrDefault() ?? "dramatic";

        return profile switch
        {
            "warmGlow" => new ThumbnailMoodGradingResult { MoodProfile = profile, Contrast = 1.10, Saturation = 1.08, Brightness = 1.02, HighlightColor = "warm" },
            "deepSpace" => new ThumbnailMoodGradingResult { MoodProfile = profile, Contrast = 1.16, Saturation = 1.12, Brightness = 0.98, HighlightColor = "blue" },
            "cinematicBlue" => new ThumbnailMoodGradingResult { MoodProfile = profile, Contrast = 1.13, Saturation = 1.09, Brightness = 1.0, HighlightColor = "blue" },
            "moonlight" => new ThumbnailMoodGradingResult { MoodProfile = profile, Contrast = 1.08, Saturation = 1.02, Brightness = 1.03, HighlightColor = "silver" },
            "sunset" => new ThumbnailMoodGradingResult { MoodProfile = profile, Contrast = 1.09, Saturation = 1.10, Brightness = 1.01, HighlightColor = "amber" },
            _ => new ThumbnailMoodGradingResult { MoodProfile = profile, Contrast = 1.15, Saturation = 1.08, Brightness = 1.0, HighlightColor = "dramatic" }
        };
    }

    private static string ResolveMood(string objectType, string? eventType)
    {
        if (ContainsAny(eventType, "meteor", "shower")) return "dramatic";
        if (ContainsAny(eventType, "conjunction", "alignment") || ContainsAny(objectType, "conjunction")) return "cinematicBlue";
        if (ContainsAny(objectType, "moon", "lunar")) return "warmGlow";
        if (ContainsAny(objectType, "deep", "nebula", "galaxy", "cluster")) return "deepSpace";
        if (ContainsAny(eventType, "sunset", "twilight")) return "sunset";
        return "dramatic";
    }

    private static bool ContainsAny(string? value, params string[] needles)
        => !string.IsNullOrWhiteSpace(value) && needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
}
