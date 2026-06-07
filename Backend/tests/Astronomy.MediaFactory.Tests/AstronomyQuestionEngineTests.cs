using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyQuestionEngineTests
{
    [Fact]
    public async Task GenerateQuestionAnswersAsync_DoesNotFlagAstronomyWordsAsInternalWording()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "VALID_ASTRONOMY_WORDS",
            eventType: "PlanetVisibilityGuide",
            objectName: "sky object",
            metadataJson: """
                {
                  "direction": "southwest direction",
                  "referenceObject": "reference star near a reference point",
                  "altitudeDegrees": 18,
                  "angularSeparationDegrees": 4.2
                }
                """);

        var service = CreateService(db, workingDirectory);
        var result = await service.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventIds: [evt.EventCode],
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.EventCount);
        Assert.Equal(1, result.QuestionSetCount);
        Assert.Empty(result.Warnings.Where(w => w.Contains("internal wording", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(result.GeneratedFiles);
        Assert.Equal(0, await db.AstronomyQuestionAnswerSets.CountAsync());
        Assert.Contains(result.QuestionSets.Single().Answers, a => a.QuestionType == AstronomyQuestionTypes.How && a.AnswerText.Contains("reference star near a reference point", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateQuestionAnswersAsync_InvalidQuestionSetReturnsWarningsAndDoesNotPersist()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "INVALID_INTERNAL_WORD",
            eventType: "PlanetConjunction",
            objectName: "sourcePath",
            metadataJson: """
                {
                  "direction": "east",
                  "referenceObject": "reference star",
                  "altitudeDegrees": 22
                }
                """);

        var service = CreateService(db, workingDirectory);
        var result = await service.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventIds: [evt.EventCode],
            DryRun: false), CancellationToken.None);

        Assert.Equal(1, result.EventCount);
        Assert.Equal(1, result.QuestionSetCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.Contains(result.Warnings, w => w.Contains("matched forbidden term 'sourcePath'", StringComparison.OrdinalIgnoreCase)
            && w.Contains("sourcePath", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("failed validation", StringComparison.OrdinalIgnoreCase)
            && w.Contains("not persisted", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, await db.AstronomyQuestionAnswerSets.CountAsync());
        Assert.False(Directory.Exists(Path.Combine(workingDirectory, "assets")));
    }

    private static AstronomyQuestionEngine CreateService(MediaFactoryDbContext db, string workingDirectory)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }), NullLogger<AstronomyQuestionEngine>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "question-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static AstronomyEventIntelligence SeedEvent(
        MediaFactoryDbContext db,
        string eventCode,
        string eventType,
        string objectName,
        string metadataJson)
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = eventCode,
            EventType = eventType,
            Title = $"{eventCode} title",
            StartUtc = DateTimeOffset.Parse("2026-06-07T00:00:00Z"),
            PeakUtc = DateTimeOffset.Parse("2026-06-07T12:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-06-07T14:00:00Z"),
            RegionId = "IN-RJ-UDAIPUR",
            LocationName = "Udaipur observation field",
            TimeZone = "UTC",
            RecommendedCategory = eventType,
            Status = "Candidate",
            ConfidenceScore = 8m,
            RarityScore = 8m,
            VisibilityScore = 8m,
            AudienceInterestScore = 8m,
            TimingUrgencyScore = 8m,
            ContentOpportunityScore = 8m,
            MetadataJson = metadataJson,
            Objects = [new AstronomyEventObject { ObjectName = objectName, ObjectType = "Planet", Magnitude = -4.1m }]
        };

        db.AstronomyEventIntelligences.Add(evt);
        db.SaveChanges();
        return evt;
    }
}
