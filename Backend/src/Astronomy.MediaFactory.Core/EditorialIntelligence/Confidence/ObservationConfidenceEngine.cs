using Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Confidence;
public sealed class ObservationConfidenceEngine : IObservationConfidenceEngine
{
    public IReadOnlyList<ObservationConfidenceCue> BuildCues(ObservationMetadata m)
    {
        var cues = new List<ObservationConfidenceCue>();
        var text = $"{m.EventType} {m.Brightness} {m.RelativePositions}";
        if (text.Contains("Venus", StringComparison.OrdinalIgnoreCase) && text.Contains("bright", StringComparison.OrdinalIgnoreCase)) cues.Add(new("If one object catches your eye first because it is much brighter, that is likely Venus.", "brightness"));
        if (text.Contains("Jupiter", StringComparison.OrdinalIgnoreCase)) cues.Add(new("Jupiter usually appears steadier and softer than Venus.", "relativePositions"));
        if (m.NakedEyeVisible == true || m.TelescopeRecommended == false) cues.Add(new("Do not expect telescope-style detail with the naked eye; the planets will look like bright points of light.", "nakedEyeVisible"));
        return cues;
    }
}
