using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklyStoryboardComposer : IWeeklyStoryboardComposer
{
    private static readonly HashSet<string> AllowedMotions = ["slow_zoom_in","slow_zoom_out","cinematic_pan","orbital_drift","parallax_float","atmospheric_glow","fade_reveal","constellation_trace"];
    public WeeklyStoryboard Compose(WeeklyAstronomyEventExtractionResult extractionResult, string region, string language, string forecastSummary, string narrationStyle, string? workingDirectoryRoot)
    {
        var warnings = new List<string>();
        var ordered = new List<WeeklyStoryboardSegment>();
        var primary = extractionResult.SelectedPrimaryEvent ?? extractionResult.ExtractedEvents.OrderByDescending(e => e.ImportanceScore + e.RarityScore + e.VisibilityScore).FirstOrDefault();
        if (primary is null) warnings.Add("No primary event identified.");

        ordered.Add(Segment("S1", WeeklyStoryboardSegmentType.OpeningHook, "Opening Hook", "Hook immediately with cinematic reveal", [], 12, "Hook user immediately", "dramatic/cinematic", $"This week over {region} opens with a dramatic night-sky reveal.", 100, "Stellarium", "wide_night_sky", "wide establishing", "fade_reveal", "fade"));
        ordered.Add(Segment("S2", WeeklyStoryboardSegmentType.WeeklyOverview, "Weekly Overview", "Explain the week journey", [], 18, "Set expectations", "invitational", forecastSummary, 90, "Hybrid", "timeline_montage", "montage", "cinematic_pan", "cross dissolve"));
        if (primary is not null)
        {
            ordered.Add(Segment("S3", WeeklyStoryboardSegmentType.MainAstronomyEvent, "Main Event Focus", "Hero moment and climax", primary.Objects.Select(o=>o.ObjectCode).Distinct().ToList(), 32, "Deliver hero story beat", "cinematic", primary.Summary, 95, primary.RecommendedVisualSource, primary.RecommendedSceneType ?? "multi-object", "cinematic zoom", "slow_zoom_in", "glow transition"));
            if (primary.EventType == WeeklyAstronomyEventType.Grouping && primary.Objects.Count > 0)
            {
                var hero = primary.Objects.OrderByDescending(o=>o.VisibilityScore).First();
                ordered.Add(Segment("S4", WeeklyStoryboardSegmentType.HeroObjectFocus, $"{hero.ObjectName} Hero Exploration", "Detailed object exploration", [hero.ObjectCode], 18, "Deepen emotional connection", "awe", $"Closer exploration of {hero.ObjectName} after the grouping climax.", 80, "Stellarium", "hero_closeup", "close-up", "orbital_drift", "cinematic dip"));
            }
        }
        ordered.Add(Segment("S5", WeeklyStoryboardSegmentType.ViewingDirectionGuide, "Viewing Guidance", "Enable practical observation", [], 18, "Help audience observe tonight", "clear/instructional", "Direction, altitude, and timing guidance for best visibility.", 85, "Overlay", "directional_guide", "map-like", "constellation_trace", "cross dissolve"));
        if (extractionResult.ExtractedEvents.Any(e => e.EventType is WeeklyAstronomyEventType.TelescopeOpportunity or WeeklyAstronomyEventType.DeepSkyHighlight))
            ordered.Add(Segment("S6", WeeklyStoryboardSegmentType.TelescopeRecommendation, "Astrophotography / Telescope", "Increase practical value", [], 16, "Offer equipment guidance", "practical", "Best telescope and photo window from this week\'s sky conditions.", 70, "Hybrid", "equipment_tip", "medium", "parallax_float", "star flash"));
        ordered.Add(Segment("S7", WeeklyStoryboardSegmentType.ClosingSequence, "Emotional Closing", "Leave emotional astronomy feeling", [], 16, "Close with wonder and invitation", "emotional", "Fade into a calm horizon and lingering stars.", 88, "Stellarium", "cinematic_horizon", "wide", "slow_zoom_out", "atmospheric fade"));

        var transitions = ordered.Zip(ordered.Skip(1)).Select((p,i)=> new WeeklyStoryboardTransition(p.First.SegmentCode,p.Second.SegmentCode,p.Second.VisualPlan.RecommendedTransition,$"Beat {i+1} to beat {i+2} pacing")).ToList();
        var duration = ordered.Sum(x=>x.EstimatedDurationSeconds);
        var duplicateHeroes = ordered.Count(s => s.SegmentType == WeeklyStoryboardSegmentType.HeroObjectFocus) > 1;
        var isValid = ordered.Any(s=>s.SegmentType==WeeklyStoryboardSegmentType.OpeningHook)
                      && ordered.Any(s=>s.SegmentType==WeeklyStoryboardSegmentType.ClosingSequence)
                      && primary is not null && !duplicateHeroes && duration >= 60;
        if (duration < 120 || duration > 180) warnings.Add($"Long-form duration target is 120-180s; planned={duration}s.");
        if (duplicateHeroes) warnings.Add("Duplicate hero object scenes detected.");

        var storyboard = new WeeklyStoryboard(isValid, "Curiosity → discovery → climax → practical confidence → emotional closure", primary, ordered, transitions, "Escalation is preserved with overview-to-hero-to-guidance rhythm.", "Narration avoids repeated hooks and shifts tone from cinematic to practical to emotional.", "Visuals escalate from wide reveal to hero zoom then resolve with atmospheric fade.", duration, warnings);

        if (!string.IsNullOrWhiteSpace(workingDirectoryRoot))
        {
            var debug = Path.Combine(workingDirectoryRoot, "debug");
            Directory.CreateDirectory(debug);
            var payload = new { orderedSegments = ordered, selectedPrimaryEvent = primary, pacingAnalysis = storyboard.PacingAnalysis, estimatedVideoDuration = duration, transitionPlan = transitions, narrationFlowAnalysis = storyboard.NarrationFlowAnalysis, visualEscalationAnalysis = storyboard.VisualEscalationAnalysis, warnings };
            File.WriteAllText(Path.Combine(debug, "weekly-storyboard.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        return storyboard;
    }

    private static WeeklyStoryboardSegment Segment(string code, WeeklyStoryboardSegmentType type, string title, string purpose, IReadOnlyList<string> targets, int seconds, string narrPurpose, string narrTone, string narrSummary, int narrPriority, string visualSource, string sceneType, string camera, string motion, string transition)
        => new(code, type, title, purpose, targets, seconds, new WeeklyStoryboardNarrationSection(narrPurpose, narrTone, narrSummary, seconds, narrPriority), new WeeklyStoryboardVisualPlan(visualSource, sceneType, camera, AllowedMotions.Contains(motion) ? motion : "cinematic_pan", transition));
}
