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
    public void Build_ShouldIncludeMoonAndJupiterSceneObservationContext()
    {
        var context = new AstronomyContext { Date = new DateOnly(2026, 3, 16), LocationName = "Seattle, USA", TimeZone = "America/Los_Angeles" };
        context.Events.Add(new AstronomyEventModel { Category = "Moon", ObjectName = "Waxing Gibbous Moon", VisibilityWindow = "Around 8:45 PM", Direction = "West", ObservationTool = "Naked eye", Details = "Bright and easy to find.", Score = 0.91 });
        context.Events.Add(new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", VisibilityWindow = "Around 9:00 PM", Direction = "South-west", ObservationTool = "Binoculars", Details = "Look for Galilean moons.", Score = 0.95 });

        var prompt = new PromptBuilder().Build(ContentType.DailySkyGuide, context);

        Assert.Contains("\"sceneId\": \"moon\"", prompt);
        Assert.Contains("Waxing Gibbous Moon", prompt);
        Assert.Contains("\"sceneId\": \"jupiter\"", prompt);
        Assert.Contains("\"objectName\": \"Jupiter\"", prompt);
        Assert.Contains("not generic sky facts", prompt, StringComparison.OrdinalIgnoreCase);
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
