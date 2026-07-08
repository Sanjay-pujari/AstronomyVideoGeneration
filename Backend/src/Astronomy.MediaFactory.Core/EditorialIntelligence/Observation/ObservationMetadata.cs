using System.Text.Json;
namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;

public sealed record ObservationMetadata(string? EventType, string? BestViewingTime, string? Direction, string? Altitude, string? Constellation, string? Brightness, string? MoonInterference, string? RelativePositions, string? LocalViewingWindow, bool? NakedEyeVisible, bool? BinocularRecommended, bool? TelescopeRecommended)
{
    public static ObservationMetadata From(string? eventType, JsonElement? element)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return new(eventType, null, null, null, null, null, null, null, null, null, null, null);
        var e = element.Value;
        string? Get(params string[] names) => names.Select(n => e.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        bool? GetBool(params string[] names)
        {
            var v = Get(names); if (bool.TryParse(v, out var b)) return b; return null;
        }
        return new(eventType, Get("bestViewingTime", "bestViewingWindowLocal", "viewingWindow", "localViewingWindow"), Get("direction", "skyDirectionHint", "observationDirection"), Get("altitude"), Get("constellation"), Get("brightness"), Get("moonInterference"), Get("relativePositions", "relativePosition"), Get("localViewingWindow", "visibilityWindow", "observationWindow"), GetBool("nakedEyeVisible"), GetBool("binocularRecommended"), GetBool("telescopeRecommended"));
    }
}
