using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class QuestionScenePlannerTests
{
    [Fact]
    public async Task GenerateQuestionScenePlanAsync_ConvertsApprovedQuestionSetIntoSixScenePlan()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(db);
        SeedApprovedQuestionSet(db, evt.Id);

        var planner = CreatePlanner(db, workingDirectory);
        var result = await planner.GenerateQuestionScenePlanAsync(new QuestionScenePlanRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventId: evt.Id.ToString("D"),
            Language: "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(evt.Id.ToString("D"), result.EventId);
        Assert.Equal(6, result.SceneCount);
        Assert.Empty(result.Warnings);
        Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(result.GeneratedFiles.Single()));
        Assert.Equal(
            ["OpeningOverview", "LocationGuide", "TimingGuide", "ObservationGuide", "Significance", "ClosingAction"],
            result.ScenePlan.Scenes.Select(s => s.ScenePurpose).ToArray());
        Assert.Equal(AstronomyQuestionTypes.What, result.ScenePlan.Scenes.First().QuestionType);
        Assert.Equal(AstronomyQuestionTypes.Action, result.ScenePlan.Scenes.Last().QuestionType);
        Assert.All(result.ScenePlan.Scenes, scene =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scene.ViewerQuestion));
            Assert.False(string.IsNullOrWhiteSpace(scene.ViewerTakeaway));
            Assert.False(string.IsNullOrWhiteSpace(scene.VisualIntent));
            Assert.False(string.IsNullOrWhiteSpace(scene.NarrationIntent));
            Assert.True(scene.IsRequired);
        });

        var savedJson = await File.ReadAllTextAsync(result.GeneratedFiles.Single());
        using var document = JsonDocument.Parse(savedJson);
        Assert.Equal("IN-RJ-UDAIPUR", document.RootElement.GetProperty("regionId").GetString());
        Assert.Equal(6, document.RootElement.GetProperty("scenes").GetArrayLength());
    }

    [Fact]
    public async Task GenerateQuestionScenePlanAsync_AllowsGeneratedQuestionSetWhenValidationPasses()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(db);
        SeedGeneratedQuestionSet(db, evt.Id);

        var planner = CreatePlanner(db, workingDirectory);
        var result = await planner.GenerateQuestionScenePlanAsync(new QuestionScenePlanRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventId: evt.Id.ToString("D"),
            Language: "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(6, result.SceneCount);
        Assert.Contains("QuestionAnswerSet status is Generated but passed validation.", result.Warnings);
        Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(result.GeneratedFiles.Single()));
    }

    [Fact]
    public async Task GenerateQuestionScenePlanAsync_RejectsLatestGeneratedQuestionSetWhenValidationFails()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(db);
        SeedQuestionSet(db, evt.Id, AstronomyQuestionSetStatus.Generated, "Use MetadataJson path /tmp/question-answer-set.json for this scene.");

        var planner = CreatePlanner(db, workingDirectory);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => planner.GenerateQuestionScenePlanAsync(new QuestionScenePlanRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventId: evt.Id.ToString("D")), CancellationToken.None));

        Assert.Contains("Latest generated question answer set did not pass validation", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories));
    }

    private static QuestionScenePlanner CreatePlanner(MediaFactoryDbContext db, string workingDirectory)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }), NullLogger<QuestionScenePlanner>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "question-scene-planner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static AstronomyEventIntelligence SeedEvent(MediaFactoryDbContext db)
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = "GOLDEN_RARE_EVENT_ALERT",
            EventType = "PlanetConjunction",
            Title = "Golden rare event alert",
            StartUtc = DateTimeOffset.Parse("2026-06-07T00:00:00Z"),
            PeakUtc = DateTimeOffset.Parse("2026-06-07T13:53:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-06-07T14:00:00Z"),
            RegionId = "IN-RJ-UDAIPUR",
            LocationName = "Udaipur",
            TimeZone = "Asia/Kolkata",
            RecommendedCategory = "PlanetConjunction",
            Status = "Candidate",
            ConfidenceScore = 8m,
            RarityScore = 8m,
            VisibilityScore = 8m,
            AudienceInterestScore = 8m,
            TimingUrgencyScore = 8m,
            ContentOpportunityScore = 8m
        };

        db.AstronomyEventIntelligences.Add(evt);
        db.SaveChanges();
        return evt;
    }

    private static void SeedApprovedQuestionSet(MediaFactoryDbContext db, Guid eventId)
        => SeedQuestionSet(db, eventId, AstronomyQuestionSetStatus.Approved);

    private static void SeedGeneratedQuestionSet(MediaFactoryDbContext db, Guid eventId)
        => SeedQuestionSet(db, eventId, AstronomyQuestionSetStatus.Generated);

    private static void SeedQuestionSet(MediaFactoryDbContext db, Guid eventId, string status, string? whatAnswerOverride = null)
    {
        db.AstronomyQuestionAnswerSets.Add(new AstronomyQuestionAnswerSet
        {
            AstronomyEventIntelligenceId = eventId,
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            Version = "v1",
            Status = status,
            GeneratedUtc = DateTimeOffset.Parse("2026-06-07T14:00:00Z"),
            Answers =
            [
                Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", whatAnswerOverride ?? "Venus and Jupiter will appear close together in Udaipur’s evening sky.", 1),
                Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", "Look toward the western sky, about one-third above the horizon.", 2),
                Answer(AstronomyQuestionTypes.When, "When is the best time?", "Best viewing time", "Best viewing is around 7:23 PM IST, shortly after sunset.", 3),
                Answer(AstronomyQuestionTypes.How, "How can I find it?", "How to observe", "Find bright Venus first, then look slightly nearby for Jupiter.", 4),
                Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", "Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing.", 5),
                Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Step outside", "If skies are clear, step outside after sunset and enjoy the view.", 6)
            ]
        });
        db.SaveChanges();
    }

    private static AstronomyQuestionAnswer Answer(string questionType, string questionText, string title, string answerText, int displayOrder) => new()
    {
        QuestionType = questionType,
        QuestionText = questionText,
        Title = title,
        AnswerText = answerText,
        DisplayOrder = displayOrder
    };
}
