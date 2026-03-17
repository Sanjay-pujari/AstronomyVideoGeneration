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
