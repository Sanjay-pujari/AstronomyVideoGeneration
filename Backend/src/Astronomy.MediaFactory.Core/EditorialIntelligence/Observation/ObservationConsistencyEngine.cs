namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
public sealed class ObservationConsistencyEngine : IObservationConsistencyEngine
{
    public const string MissingMetadataFallback = "Exact viewing details may vary by location, but this event is best checked close to the local viewing window.";
    public ObservationGuidance BuildGuidance(ObservationMetadata m)
    {
        var parts = new List<string>(); var fields = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.BestViewingTime)) { parts.Add(m.BestViewingTime.TrimEnd('.') + "."); fields.Add(nameof(m.BestViewingTime)); }
        else if (!string.IsNullOrWhiteSpace(m.LocalViewingWindow)) { parts.Add($"Use the local viewing window: {m.LocalViewingWindow.TrimEnd('.')}."); fields.Add(nameof(m.LocalViewingWindow)); }
        if (!string.IsNullOrWhiteSpace(m.Direction)) { parts.Add($"Look toward {NormalizeDirection(m.Direction)}."); fields.Add(nameof(m.Direction)); }
        if (!string.IsNullOrWhiteSpace(m.Brightness)) { parts.Add(m.Brightness.TrimEnd('.') + "."); fields.Add(nameof(m.Brightness)); }
        if (!string.IsNullOrWhiteSpace(m.RelativePositions)) { parts.Add(m.RelativePositions.TrimEnd('.') + "."); fields.Add(nameof(m.RelativePositions)); }
        if (!string.IsNullOrWhiteSpace(m.MoonInterference)) { parts.Add($"Moonlight may affect the view: {m.MoonInterference.TrimEnd('.')}."); fields.Add(nameof(m.MoonInterference)); }
        if (parts.Count == 0) return new(MissingMetadataFallback, [], true);
        return new(string.Join(" ", parts), fields, false);
    }
    private static string NormalizeDirection(string value)
    {
        var v = value.Trim().TrimEnd('.');
        return v.StartsWith("toward ", StringComparison.OrdinalIgnoreCase) ? v[7..] : v;
    }
}
