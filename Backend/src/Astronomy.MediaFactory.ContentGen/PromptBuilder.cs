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
        if (contentType == ContentType.SpecialEventGuide)
            return BuildSpecialEventPrompt(context, feedbackContext);
        var finalSceneIds = context.SceneObservationContexts
            .OrderBy(scene => scene.SceneIndex > 0 ? scene.SceneIndex : int.MaxValue)
            .Select(scene => scene.SceneId)
            .Where(sceneId => !string.IsNullOrWhiteSpace(sceneId))
            .ToArray();

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
        sb.AppendLine("You are an astronomy educator creating a production-quality cinematic YouTube sky guide.");
        sb.AppendLine("Use the provided structured astronomy input and turn it into rich, educational storytelling rather than a robotic object list.");
        AppendLocalizationRequirements(sb, context.Localization);
        sb.AppendLine(PromptFeedbackComposer.BuildBoundaryRulesSection());
        sb.AppendLine("Requirements:");
        sb.AppendLine("1) Write for beginners using clear, approachable language.");
        sb.AppendLine("2) Include practical observation guidance (when to look, where to look, and what tool to use).");
        sb.AppendLine("3) Do not invent or hallucinate numeric values.");
        sb.AppendLine("4) Return ONLY valid JSON with no markdown, no code fences, and no commentary.");
        sb.AppendLine("5) Each sceneScript section must map exactly to provided sceneObservationContext sceneId values and include: object, local time, direction, approximate altitude, tool needed, and one beginner tip.");
        sb.AppendLine($"   Required sceneScript order: {string.Join(" -> ", finalSceneIds)}.");
        sb.AppendLine("   Do not place the closing scene before any selected object or tips scene; closing must be the final sceneScript key when a closing scene exists.");
        sb.AppendLine("6) Keep narration practical and specific to structured observation context, not generic sky facts.");
        sb.AppendLine("7) Keep object-scene narration chronological with explicit time progression language.");
        sb.AppendLine("8) Use natural transitions such as 'Later in the night...', 'As midnight approaches...', and 'In the early morning hours...' when they fit the local times.");
        sb.AppendLine("9) If the gap between consecutive object scenes exceeds 2 hours, include the phrase 'Later in the night...'.");
        sb.AppendLine("10) DailySkyGuide target is about 5-6 minutes, naturally paced at 120-145 English words/minute or 110-130 Hindi words/minute.");
        sb.AppendLine("11) Use this adaptable cinematic structure when matching the provided scenes: opening overview, tonight's highlight/event, Moon if present, 3-6 ranked objects, constellation/bright star/deep-sky context when present, observation tips, and closing overview.");
        sb.AppendLine("12) Write natural transitions, educational context, and practical observing instructions. Avoid repetitive phrasing such as 'is visible' at the start of every scene.");

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
        if (finalSceneIds.Length > 0)
        {
            for (var i = 0; i < finalSceneIds.Length; i++)
            {
                var suffix = i == finalSceneIds.Length - 1 ? string.Empty : ",";
                sb.AppendLine($"    \"{finalSceneIds[i]}\": \"string\"{suffix}");
            }
        }
        else
        {
            sb.AppendLine("    \"scene-id\": \"string\"");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Structured astronomy input (JSON data block):");
        sb.AppendLine("<BEGIN_ASTRONOMY_INPUT_JSON>");
        sb.AppendLine(JsonSerializer.Serialize(astronomyInput, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("<END_ASTRONOMY_INPUT_JSON>");

        return sb.ToString();
    }


    private static void AppendLocalizationRequirements(StringBuilder sb, LocalizationContext localization)
    {
        var languageName = LocalizationResolver.LanguageDisplayName(localization.ResolvedLanguage);
        sb.AppendLine($"Localization: Generate all user-facing narration, title, description, tags, SEO-ready text, and sceneScript values in {languageName} (language code: {localization.ResolvedLanguage}).");
        sb.AppendLine("Do not translate internal JSON property names, scene IDs, technical IDs, file names, object keys, or structured input keys.");
        if (LocalizationResolver.IsHindi(localization.ResolvedLanguage))
        {
            sb.AppendLine("पूरी narration हिंदी में लिखें। astronomy object names को जरूरत हो तो English नाम brackets में रखें, जैसे बृहस्पति (Jupiter). Scene labels/internal IDs English रह सकते हैं, लेकिन spoken narration Hindi होनी चाहिए.");
        }
        else
        {
            sb.AppendLine("Keep astronomy object names readable; for non-English output, keep the English object name in brackets when helpful, for example: बृहस्पति (Jupiter).");
        }
    }

    private static string BuildSpecialEventPrompt(AstronomyContext context, PromptFeedbackContext? feedbackContext)
    {
        var finalSceneIds = context.SceneObservationContexts
            .OrderBy(scene => scene.SceneIndex > 0 ? scene.SceneIndex : int.MaxValue)
            .Select(scene => scene.SceneId)
            .Where(sceneId => !string.IsNullOrWhiteSpace(sceneId))
            .ToArray();

        var astronomyInput = new
        {
            date = context.Date.ToString("yyyy-MM-dd"),
            location = context.LocationName,
            timeZone = context.TimeZone,
            contentType = ContentType.SpecialEventGuide.ToString(),
            specialEvent = context.SpecialEvent,
            astronomyEvents = context.Events.OrderByDescending(e => e.Score).Select(e => new { e.Category, e.ObjectName, e.VisibilityWindow, e.Direction, e.ObservationTool, e.Details, e.Score }),
            sceneObservationContext = BuildSceneObservationContext(context)
        };

        var sb = new StringBuilder();
        sb.AppendLine("You are an astronomy educator creating a dedicated event-based YouTube guide.");
        sb.AppendLine("This is a SpecialEventGuide, not a DailySkyGuide. Keep the entire narration focused on the named event.");
        AppendLocalizationRequirements(sb, context.Localization);
        sb.AppendLine(PromptFeedbackComposer.BuildBoundaryRulesSection());
        sb.AppendLine("Requirements:");
        sb.AppendLine("1) Explain why the event matters, rarity/urgency, and what viewers should look for.");
        sb.AppendLine("2) Include best viewing time, direction, beginner tips, and safe-observing advice when relevant.");
        sb.AppendLine("3) Prioritize event objects, alignment, moon phase, meteor radiant, or eclipse phases over generic sky overview.");
        sb.AppendLine("4) Use cinematic pacing: strong hook, why the event matters, event visualization, best viewing time, best direction, observation tips, extra context/storytelling, and closing CTA.");
        sb.AppendLine("5) Do not invent numeric values beyond supplied context.");
        sb.AppendLine("6) Return ONLY valid JSON with no markdown, no code fences, and no commentary.");
        sb.AppendLine("7) Each sceneScript section must map exactly to provided sceneObservationContext sceneId values.");
        sb.AppendLine($"   Required sceneScript order: {string.Join(" -> ", finalSceneIds)}.");
        sb.AppendLine("   Keep the closing scene as the final sceneScript key when a closing scene exists.");
        sb.AppendLine("8) SpecialEventGuide target is 7-9 minutes, naturally paced at 120-145 English words/minute or 110-130 Hindi words/minute. Do not pad with silence cues.");
        sb.AppendLine();
        sb.AppendLine(PromptFeedbackComposer.BuildFeedbackSection(feedbackContext, isShortForm: false));
        sb.AppendLine();
        sb.AppendLine("Output JSON schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"title\": \"string\",");
        sb.AppendLine("  \"description\": \"string\",");
        sb.AppendLine("  \"tags\": [\"string\", \"string\"],");
        sb.AppendLine("  \"estimatedDurationSeconds\": 600,");
        sb.AppendLine("  \"scriptBody\": \"string\",");
        sb.AppendLine("  \"sceneScript\": {");
        if (finalSceneIds.Length > 0)
        {
            for (var i = 0; i < finalSceneIds.Length; i++)
            {
                var suffix = i == finalSceneIds.Length - 1 ? string.Empty : ",";
                sb.AppendLine($"    \"{finalSceneIds[i]}\": \"string\"{suffix}");
            }
        }
        else
        {
            sb.AppendLine("    \"scene-id\": \"string\"");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Structured special event input (JSON data block):");
        sb.AppendLine("<BEGIN_SPECIAL_EVENT_INPUT_JSON>");
        sb.AppendLine(JsonSerializer.Serialize(astronomyInput, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("<END_SPECIAL_EVENT_INPUT_JSON>");
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
                sceneIndex = scene.SceneIndex,
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
