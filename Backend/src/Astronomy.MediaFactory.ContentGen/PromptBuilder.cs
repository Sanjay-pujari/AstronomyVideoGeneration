using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.ContentGen;

public interface IPromptBuilder
{
    string Build(ContentType contentType, AstronomyContext context, PromptFeedbackContext? feedbackContext = null);
}

public sealed class PromptBuilder : IPromptBuilder
{
    public string Build(ContentType contentType, AstronomyContext context, PromptFeedbackContext? feedbackContext = null)
    {
        var astronomyInput = new
        {
            date = context.Date.ToString("yyyy-MM-dd"),
            location = context.LocationName,
            timeZone = context.TimeZone,
            contentType = contentType.ToString(),
            astronomyEvents = SelectAstronomyEvents(context)
                .Select(e => new
                {
                    e.Category,
                    e.ObjectName,
                    e.VisibilityWindow,
                    e.Direction,
                    e.ObservationTool,
                    e.Details,
                    e.Score
                }),
            newsItems = context.NewsItems.Select(n => new
            {
                n.Headline,
                n.Summary,
                n.SourceName,
                publishedDate = n.PublishedDate.ToString("yyyy-MM-dd"),
                n.SourceUrl
            }),
            sceneObservationContext = BuildSceneObservationContext(context)
        };

        var sb = new StringBuilder();
        sb.AppendLine("You are an astronomy educator creating a beginner-friendly YouTube script.");
        sb.AppendLine("Use the provided structured astronomy input.");
        sb.AppendLine(PromptFeedbackComposer.BuildBoundaryRulesSection());
        sb.AppendLine("Requirements:");
        sb.AppendLine("1) Write for beginners using clear, approachable language.");
        sb.AppendLine("2) Include practical observation guidance (when to look, where to look, and what tool to use).");
        sb.AppendLine("3) Do not invent or hallucinate numeric values.");
        sb.AppendLine("4) Return ONLY valid JSON with no markdown, no code fences, and no commentary.");
        sb.AppendLine("5) Each sceneScript section must map exactly to provided sceneObservationContext sceneId values and include: object, local time, direction, approximate altitude, tool needed, and one beginner tip.");
        sb.AppendLine("6) Keep narration practical and specific to structured observation context, not generic sky facts.");
        sb.AppendLine("7) Keep object-scene narration chronological with explicit time progression language.");
        sb.AppendLine("8) Use natural transitions such as 'Later in the night...', 'As midnight approaches...', and 'In the early morning hours...' when they fit the local times.");
        sb.AppendLine("9) If the gap between consecutive object scenes exceeds 2 hours, include the phrase 'Later in the night...'.");

        sb.AppendLine();
        sb.AppendLine(PromptFeedbackComposer.BuildFeedbackSection(feedbackContext, isShortForm: false));

        sb.AppendLine();
        sb.AppendLine("Output JSON schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"title\": \"string\",");
        sb.AppendLine("  \"description\": \"string\",");
        sb.AppendLine("  \"tags\": [\"string\", \"string\"],");
        sb.AppendLine("  \"estimatedDurationSeconds\": 900,");
        sb.AppendLine("  \"scriptBody\": \"string\",");
        sb.AppendLine("  \"sceneScript\": {");
        sb.AppendLine("    \"sky-overview\": \"string\",");
        sb.AppendLine("    \"object-1\": \"string\",");
        sb.AppendLine("    \"object-2\": \"string\",");
        sb.AppendLine("    \"object-3\": \"string\",");
        sb.AppendLine("    \"closing\": \"string\"");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Structured astronomy input (JSON data block):");
        sb.AppendLine("<BEGIN_ASTRONOMY_INPUT_JSON>");
        sb.AppendLine(JsonSerializer.Serialize(astronomyInput, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("<END_ASTRONOMY_INPUT_JSON>");

        return sb.ToString();
    }


    private static IEnumerable<AstronomyEventModel> SelectAstronomyEvents(AstronomyContext context)
    {
        if (context.SceneObservationContexts.Count == 0)
        {
            return context.Events.OrderByDescending(e => e.Score);
        }

        var selectedSceneObjects = context.SceneObservationContexts
            .Select(scene => scene.ObjectName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Equals("Sky", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedSceneObjects.Count == 0)
        {
            return Enumerable.Empty<AstronomyEventModel>();
        }

        return context.Events
            .Where(e => selectedSceneObjects.Contains(e.ObjectName))
            .OrderByDescending(e => e.Score);
    }

    private static object BuildSceneObservationContext(AstronomyContext context)
    {
        if (context.SceneObservationContexts.Count > 0)
        {
            return context.SceneObservationContexts.Select(scene => new
            {
                sceneId = scene.SceneId,
                sceneTitle = scene.SceneTitle,
                sceneType = scene.SceneType,
                objectName = scene.ObjectName,
                objectType = scene.ObjectType,
                bestViewingLocalTime = scene.LocalObservationTime.ToString("yyyy-MM-dd HH:mm"),
                directionLabel = scene.DirectionLabel ?? "Not specified",
                altitudeDegrees = scene.AltitudeDegrees,
                azimuthDegrees = scene.AzimuthDegrees,
                visibilityLevel = scene.IsVisible ? "Visible" : "NotVisible",
                recommendedTool = scene.RecommendedTool,
                observingTip = scene.NarrationFocus,
                whyInteresting = scene.VisibilityReason
            });
        }

        return Array.Empty<object>();
    }

}
