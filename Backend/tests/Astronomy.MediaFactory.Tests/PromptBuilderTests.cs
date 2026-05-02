using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PromptBuilderTests
{
    [Fact]
    public void Build_ShouldContainEventAndLocation()
    {
        var context = new AstronomyContext { Date = new DateOnly(2026, 3, 16), LocationName = "Udaipur, India", TimeZone = "Asia/Kolkata" };
        context.Events.Add(new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", VisibilityWindow = "Evening", Direction = "South-west", ObservationTool = "Telescope", Details = "Bands visible", Score = 0.95 });
        var prompt = AstronomyPromptBuilder.Build(ContentType.DailySkyGuide, context);
        Assert.Contains("Jupiter", prompt);
        Assert.Contains("Udaipur, India", prompt);
        Assert.Contains("bestViewingLocalTime", prompt);
        Assert.Contains("directionLabel", prompt);
        Assert.Contains("altitudeDegrees", prompt);
    }

    [Fact]
    public void Build_ShouldUseProvidedSceneObservationContext_AsSingleSource()
    {
        var context = new AstronomyContext { Date = new DateOnly(2026, 3, 16), LocationName = "Seattle, USA", TimeZone = "America/Los_Angeles" };
        context.Events.Add(new AstronomyEventModel { Category = "Moon", ObjectName = "Moon", VisibilityWindow = "Around 8:45 PM", Direction = "West", ObservationTool = "Naked eye", Details = "Bright and easy to find.", Score = 0.91 });
        context.Events.Add(new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", VisibilityWindow = "Around 9:00 PM", Direction = "South-west", ObservationTool = "Binoculars", Details = "Look for Galilean moons.", Score = 0.95 });
        context.Events.Add(new AstronomyEventModel { Category = "Planet", ObjectName = "Mars", VisibilityWindow = "Around 9:30 PM", Direction = "South", ObservationTool = "Binoculars", Details = "Not selected", Score = 0.90 });
        context.Events.Add(new AstronomyEventModel { Category = "Constellation", ObjectName = "Orion", VisibilityWindow = "Around 10:00 PM", Direction = "East", ObservationTool = "Naked eye", Details = "Not selected", Score = 0.89 });

        context.SceneObservationContexts =
        [
            new SceneObservationContext { SceneId = "sky-overview", SceneTitle = "Sky overview", SceneType = "Overview", ObjectName = "Sky", ObjectType = "Overview", LocalObservationTime = new DateTime(2026,3,16,20,0,0), UtcObservationTime = DateTimeOffset.UtcNow },
            new SceneObservationContext { SceneId = "object-1", SceneTitle = "Moon focus", SceneType = "Object", ObjectName = "Moon", ObjectType = "Moon", LocalObservationTime = new DateTime(2026,3,16,20,45,0), UtcObservationTime = DateTimeOffset.UtcNow },
            new SceneObservationContext { SceneId = "object-2", SceneTitle = "Jupiter focus", SceneType = "Object", ObjectName = "Jupiter", ObjectType = "Planet", LocalObservationTime = new DateTime(2026,3,16,21,0,0), UtcObservationTime = DateTimeOffset.UtcNow },
            new SceneObservationContext { SceneId = "object-3", SceneTitle = "Venus focus", SceneType = "Object", ObjectName = "Venus", ObjectType = "Planet", LocalObservationTime = new DateTime(2026,3,16,21,15,0), UtcObservationTime = DateTimeOffset.UtcNow },
            new SceneObservationContext { SceneId = "closing", SceneTitle = "Closing sky", SceneType = "Tips", ObjectName = "Sky", ObjectType = "Overview", LocalObservationTime = new DateTime(2026,3,16,21,45,0), UtcObservationTime = DateTimeOffset.UtcNow }
        ];

        var prompt = new PromptBuilder().Build(ContentType.DailySkyGuide, context);

        Assert.Contains("\"sceneId\": \"object-1\"", prompt);
        Assert.Contains("\"objectName\": \"Moon\"", prompt);
        Assert.Contains("\"sceneId\": \"object-2\"", prompt);
        Assert.Contains("\"objectName\": \"Jupiter\"", prompt);
        Assert.Contains("\"objectName\": \"Venus\"", prompt);
        Assert.DoesNotContain("\"objectName\": \"Mars\"", prompt);
        Assert.DoesNotContain("\"objectName\": \"Orion\"", prompt);
        Assert.Contains("not generic sky facts", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Later in the night...", prompt, StringComparison.Ordinal);
        Assert.Contains("As midnight approaches...", prompt, StringComparison.Ordinal);
        Assert.Contains("In the early morning hours...", prompt, StringComparison.Ordinal);
        Assert.Contains("gap between consecutive object scenes exceeds 2 hours", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_IncludesBoundedFeedbackContext_WhenProvided()
    {
        var builder = new PromptBuilder();
        var context = new AstronomyContext { Date = new DateOnly(2026, 3, 16), LocationName = "Udaipur, India", TimeZone = "Asia/Kolkata" };
        var feedback = new PromptFeedbackContext
        {
            ContentType = ContentType.DailySkyGuide,
            RecommendedKeywords = ["jupiter", "tonight"],
            AvoidKeywords = ["saturn"],
            RecommendedToneNotes = ["Emphasize what is visible tonight."],
            RecentOverusedTopics = ["Jupiter"],
            TopicSelectionRationale = "Selected because score=0.92"
        };

        var prompt = builder.Build(ContentType.DailySkyGuide, context, feedback);

        Assert.Contains("Prompt-boundary rules", prompt);
        Assert.Contains("<BEGIN_FEEDBACK_CONTEXT_JSON>", prompt);
        Assert.Contains("<BEGIN_ASTRONOMY_INPUT_JSON>", prompt);
        Assert.Contains("Selected because score=0.92", prompt);
    }
}
