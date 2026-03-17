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
            })
        };

        var sb = new StringBuilder();
        sb.AppendLine("You are an astronomy educator creating a beginner-friendly YouTube script.");
        sb.AppendLine("Use the provided structured astronomy input.");
        sb.AppendLine("Requirements:");
        sb.AppendLine("1) Write for beginners using clear, approachable language.");
        sb.AppendLine("2) Include practical observation guidance (when to look, where to look, and what tool to use).");
        sb.AppendLine("3) Do not invent or hallucinate numeric values.");
        sb.AppendLine("4) Return ONLY valid JSON with no markdown, no code fences, and no commentary.");

        if (feedbackContext is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Feedback context (bounded deterministic hints):");
            sb.AppendLine(JsonSerializer.Serialize(new
            {
                feedbackContext.ContentType,
                feedbackContext.RecommendedKeywords,
                feedbackContext.AvoidKeywords,
                feedbackContext.RecommendedHookPatterns,
                feedbackContext.AvoidHookPatterns,
                feedbackContext.RecommendedTitlePatterns,
                feedbackContext.AvoidTitlePatterns,
                feedbackContext.RecommendedToneNotes,
                feedbackContext.RecentWinningTopics,
                feedbackContext.RecentOverusedTopics,
                feedbackContext.AvoidObjectEmphasis,
                feedbackContext.MetadataOptimizationHints,
                feedbackContext.TopicSelectionRationale,
                feedbackContext.UsedFallbackDefaults
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        sb.AppendLine();
        sb.AppendLine("Output JSON schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"title\": \"string\",");
        sb.AppendLine("  \"description\": \"string\",");
        sb.AppendLine("  \"tags\": [\"string\", \"string\"],");
        sb.AppendLine("  \"estimatedDurationSeconds\": 900,");
        sb.AppendLine("  \"scriptBody\": \"string\"");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Structured astronomy input:");
        sb.AppendLine(JsonSerializer.Serialize(astronomyInput, new JsonSerializerOptions { WriteIndented = true }));

        return sb.ToString();
    }
}
