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
            astronomyEvents = context.Events
                .OrderByDescending(e => e.Score)
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
        sb.AppendLine("5) Each sceneScript section (overview, moon, jupiter, deepSky, closing) must include: object, local time, direction, approximate altitude, tool needed, and one beginner tip.");
        sb.AppendLine("6) Keep narration practical and specific to structured observation context, not generic sky facts.");

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
        sb.AppendLine("    \"overview\": \"string\",");
        sb.AppendLine("    \"moon\": \"string\",");
        sb.AppendLine("    \"jupiter\": \"string\",");
        sb.AppendLine("    \"deepSky\": \"string\",");
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

    private static object BuildSceneObservationContext(AstronomyContext context)
    {
        var moonEvent = context.Events.FirstOrDefault(e => e.ObjectName.Contains("moon", StringComparison.OrdinalIgnoreCase) || e.Category.Contains("moon", StringComparison.OrdinalIgnoreCase));
        var jupiterEvent = context.Events.FirstOrDefault(e => e.ObjectName.Contains("jupiter", StringComparison.OrdinalIgnoreCase));
        var topEvent = context.Events.OrderByDescending(e => e.Score).FirstOrDefault();

        return new[]
        {
            BuildSceneContext("overview", topEvent),
            BuildSceneContext("moon", moonEvent),
            BuildSceneContext("jupiter", jupiterEvent)
        };
    }

    private static object BuildSceneContext(string sceneId, AstronomyEventModel? astronomyEvent) => new
    {
        sceneId,
        objectName = astronomyEvent?.ObjectName ?? "Unknown",
        objectType = astronomyEvent?.Category ?? "Unknown",
        bestViewingLocalTime = astronomyEvent?.VisibilityWindow ?? "Not specified",
        directionLabel = astronomyEvent?.Direction ?? "Not specified",
        altitudeDegrees = (double?)null,
        azimuthDegrees = (double?)null,
        magnitude = (double?)null,
        visibilityLevel = "Unknown",
        recommendedTool = astronomyEvent?.ObservationTool ?? "Naked eye",
        observingTip = astronomyEvent?.Details ?? "Let your eyes adapt to darkness for 15-20 minutes.",
        whyInteresting = astronomyEvent?.Details ?? "A beginner-friendly target to practice sky navigation."
    };
}
