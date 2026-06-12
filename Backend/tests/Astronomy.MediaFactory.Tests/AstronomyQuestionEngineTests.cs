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

    [Fact]
    public async Task GenerateQuestionAnswersAsync_UsesViewerFriendlyOverlayAnswersForClosePlanetPairing()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "VENUS_JUPITER_CLOSE_PAIRING",
            eventType: "PlanetConjunction",
            objectName: "Venus",
            metadataJson: """
                {
                  "direction": "west",
                  "altitudeDegrees": 30,
                  "angularSeparationDegrees": 1.63
                }
                """);
        evt.LocationName = "Udaipur";
        evt.TimeZone = "Asia/Kolkata";
        evt.PeakUtc = DateTimeOffset.Parse("2026-06-07T13:53:00Z");
        evt.Objects.Add(new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet", Magnitude = -2.1m });
        await db.SaveChangesAsync();

        var service = CreateService(db, workingDirectory);
        var result = await service.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventIds: [evt.EventCode],
            DryRun: true), CancellationToken.None);

        var answers = result.QuestionSets.Single().Answers.ToDictionary(a => a.QuestionType, a => a.AnswerText);

        Assert.Contains("Venus", answers[AstronomyQuestionTypes.What]);
        Assert.Contains("Jupiter", answers[AstronomyQuestionTypes.What]);
        Assert.Contains("western", answers[AstronomyQuestionTypes.Where], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7:23 PM IST", answers[AstronomyQuestionTypes.When]);
        Assert.Contains("Venus", answers[AstronomyQuestionTypes.How]);
        Assert.Contains("Jupiter", answers[AstronomyQuestionTypes.How]);
        Assert.Contains("1.63°", answers[AstronomyQuestionTypes.Why]);
        Assert.Contains("clear", answers[AstronomyQuestionTypes.Action], StringComparison.OrdinalIgnoreCase);

        var combinedAnswers = string.Join(" ", answers.Values);
        Assert.DoesNotContain("Overview:", combinedAnswers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Closing mark:", combinedAnswers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("local time", combinedAnswers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sky window", combinedAnswers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("magnitude", combinedAnswers, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateQuestionAnswersAsync_UsesBrightPlanetPairingWhyWhenConjunctionSeparationIsMissing()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "VENUS_JUPITER_NO_SEPARATION",
            eventType: "PlanetConjunction",
            objectName: "Venus",
            metadataJson: """
                {
                  "direction": "west",
                  "altitudeDegrees": 30
                }
                """);
        evt.Objects.Add(new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet", Magnitude = -2.1m });
        await db.SaveChangesAsync();

        var service = CreateService(db, workingDirectory);
        var result = await service.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventIds: [evt.EventCode],
            DryRun: true), CancellationToken.None);

        var whyAnswer = result.QuestionSets.Single().Answers.Single(a => a.QuestionType == AstronomyQuestionTypes.Why).AnswerText;

        Assert.Contains("Venus", whyAnswer);
        Assert.Contains("Jupiter", whyAnswer);
        Assert.DoesNotContain("easy to spot", whyAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("viewer-friendly", whyAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("easy to explain", whyAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bright", whyAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("close", whyAnswer, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task ValidateQuestionAnswerSetAsync_ApprovesGoldenRareEventPlanetConjunctionPilot()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "GOLDEN_RARE_EVENT_ALERT",
            eventType: "PlanetConjunction",
            objectName: "Venus",
            metadataJson: """
                {
                  "direction": "west",
                  "altitudeDegrees": 30,
                  "angularSeparationDegrees": 1.63
                }
                """);
        evt.LocationName = "Udaipur";
        evt.TimeZone = "Asia/Kolkata";
        evt.PeakUtc = DateTimeOffset.Parse("2026-06-07T13:53:00Z");
        evt.Objects.Add(new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet", Magnitude = -2.1m });
        await db.SaveChangesAsync();

        var service = CreateService(db, workingDirectory);
        var result = await service.ValidateQuestionAnswerSetAsync(new QuestionAnswerValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventId: evt.Id.ToString("D"),
            Language: "en"), CancellationToken.None);

        Assert.True(result.IsApproved);
        Assert.Equal(100, result.Score);
        Assert.Empty(result.Warnings);
        Assert.Equal(evt.Id.ToString("D"), result.EventId);
        Assert.Equal(
            [AstronomyQuestionTypes.What, AstronomyQuestionTypes.Where, AstronomyQuestionTypes.When, AstronomyQuestionTypes.How, AstronomyQuestionTypes.Why, AstronomyQuestionTypes.Action],
            result.Checks.Select(c => c.QuestionType).ToArray());
        Assert.All(result.Checks, check =>
        {
            Assert.True(check.Approved);
            Assert.Empty(check.Issues);
            Assert.Empty(check.Recommendations);
        });
        Assert.Empty(Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(0, await db.AstronomyQuestionAnswerSets.CountAsync());
    }

    [Fact]
    public async Task ValidateQuestionAnswerSetAsync_RejectsInternalViewerLanguage()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "INVALID_VALIDATION_INTERNAL_WORD",
            eventType: "PlanetConjunction",
            objectName: "sourcePath",
            metadataJson: """
                {
                  "direction": "east",
                  "altitudeDegrees": 22
                }
                """);

        var service = CreateService(db, workingDirectory);
        var result = await service.ValidateQuestionAnswerSetAsync(new QuestionAnswerValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventId: evt.EventCode,
            Language: "en"), CancellationToken.None);

        Assert.False(result.IsApproved);
        Assert.True(result.Score < 100);
        Assert.Contains(result.Checks, c => !c.Approved && c.Issues.Any(i => i.Contains("sourcePath", StringComparison.OrdinalIgnoreCase)));
        Assert.Empty(Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(0, await db.AstronomyQuestionAnswerSets.CountAsync());
    }


    [Theory]
    [InlineData("open the file", true, "file")]
    [InlineData("profile", false, "")]
    [InlineData("wildlife", false, "")]
    [InlineData("filed", false, "")]
    [InlineData("lifestyle", false, "")]
    [InlineData("Set a reminder for the night of Dec 12/13, check weather, and pick a dark open location.", false, "")]
    public void ForbiddenFileTokenValidation_UsesStandaloneTokenBoundaries(string answerText, bool expectedMatch, string expectedForbiddenTerm)
    {
        var method = typeof(AstronomyQuestionEngine).GetMethod("TryMatchForbiddenTerm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("TryMatchForbiddenTerm helper was not found.");
        var parameters = new object[] { answerText, string.Empty, string.Empty };

        var matched = (bool)method.Invoke(null, parameters)!;

        Assert.Equal(expectedMatch, matched);
        if (expectedMatch)
            Assert.Equal(expectedForbiddenTerm, Assert.IsType<string>(parameters[1]));
    }

    [Fact]
    public async Task GenerateQuestionAnswersAsync_UsesMeteorShowerViewingWindowAndDarkSkyGuidance()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "GEMINIDS_2026",
            eventType: "MeteorShower",
            objectName: "Geminids",
            metadataJson: """
                {
                  "skyDirectionHint": "East to overhead after 10 PM",
                  "bestViewingWindowLocal": "2026-12-14 00:00–05:00 IST",
                  "moonInterference": "Low",
                  "moonIlluminationPercent": 8,
                  "radiantVisibilityNote": "The Gemini radiant climbs higher late evening, but meteors can appear anywhere."
                }
                """);
        evt.Title = "Geminids Meteor Shower Peak";
        evt.TimeZone = "Asia/Kolkata";
        evt.PeakUtc = DateTimeOffset.Parse("2026-12-14T06:00:00Z");
        await db.SaveChangesAsync();

        var service = CreateService(db, workingDirectory);
        var result = await service.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventIds: [evt.EventCode],
            DryRun: true), CancellationToken.None);

        var answers = result.QuestionSets.Single().Answers.ToDictionary(a => a.QuestionType, a => a.AnswerText);
        Assert.Contains("Geminids", answers[AstronomyQuestionTypes.What]);
        Assert.Contains("meteor", answers[AstronomyQuestionTypes.What], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("East to overhead after 10 PM", answers[AstronomyQuestionTypes.Where]);
        Assert.Contains("meteors can appear anywhere", answers[AstronomyQuestionTypes.Where], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-12-14 00:00–05:00 IST", answers[AstronomyQuestionTypes.When]);
        Assert.DoesNotContain("11:30", answers[AstronomyQuestionTypes.When]);
        Assert.Contains("No telescope", answers[AstronomyQuestionTypes.How]);
        Assert.Contains("20 minutes", answers[AstronomyQuestionTypes.How]);
        Assert.Contains("strongest annual meteor showers", answers[AstronomyQuestionTypes.Why]);
        Assert.Contains("low moon interference", answers[AstronomyQuestionTypes.Why], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dec 13/14", answers[AstronomyQuestionTypes.Action]);

        var combinedAnswers = string.Join(" ", answers.Values);
        Assert.DoesNotContain("conjunction", combinedAnswers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Venus", combinedAnswers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jupiter", combinedAnswers, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task GenerateQuestionAnswersAsync_PlanetGroupingWithoutDirectionUsesHorizonArcGuidanceAndPassesValidation()
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(
            db,
            eventCode: "PLANET_GROUPING_NO_DIRECTION",
            eventType: "PLANET_GROUPING",
            objectName: "Venus",
            metadataJson: """
                {
                  "bestViewingWindowLocal": "2026-06-07 19:30–21:00 IST"
                }
                """);
        evt.Title = "Planet grouping over Udaipur";
        evt.TimeZone = "Asia/Kolkata";
        evt.LocationName = "Udaipur";
        evt.PeakUtc = DateTimeOffset.Parse("2026-06-07T14:00:00Z");
        evt.Objects.Add(new AstronomyEventObject { ObjectName = "Mars", ObjectType = "Planet", Magnitude = -1m });
        evt.Objects.Add(new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet", Magnitude = -2m });
        await db.SaveChangesAsync();

        var service = CreateService(db, workingDirectory);
        var generated = await service.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventIds: [evt.EventCode],
            DryRun: true), CancellationToken.None);
        var approved = await service.ValidateQuestionAnswerSetAsync(new QuestionAnswerValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventId: evt.EventCode,
            Language: "en"), CancellationToken.None);

        var whereAnswer = generated.QuestionSets.Single().Answers.Single(a => a.QuestionType == AstronomyQuestionTypes.Where).AnswerText;

        Assert.Equal("Look along the clearest horizon and scan the arc above the horizon where the grouped planets appear.", whereAnswer);
        Assert.Contains("horizon", whereAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arc", whereAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.True(approved.IsApproved, string.Join(" | ", approved.Checks.SelectMany(c => c.Issues)));
    }



    [Theory]
    [MemberData(nameof(EventQuestionCases))]
    public async Task GenerateQuestionAnswersAsync_StrategyDrivenEventTypesPassValidation(
        string eventCode,
        string eventType,
        string title,
        string[] objects,
        string metadataJson,
        string expectedTiming,
        string[] requiredTerms,
        string[] forbiddenTerms)
    {
        await using var db = CreateDb();
        var workingDirectory = CreateWorkingDirectory();
        var evt = SeedEvent(db, eventCode, eventType, objects[0], metadataJson);
        evt.Title = title;
        evt.TimeZone = "Asia/Kolkata";
        evt.LocationName = "Udaipur";
        evt.PeakUtc = DateTimeOffset.Parse("2026-06-07T13:53:00Z");
        foreach (var obj in objects.Skip(1))
            evt.Objects.Add(new AstronomyEventObject { ObjectName = obj, ObjectType = "Planet", Magnitude = -1m });
        await db.SaveChangesAsync();

        var service = CreateService(db, workingDirectory);
        var generated = await service.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventIds: [evt.EventCode],
            DryRun: true), CancellationToken.None);
        var approved = await service.ValidateQuestionAnswerSetAsync(new QuestionAnswerValidationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            EventId: evt.EventCode,
            Language: "en"), CancellationToken.None);

        var set = generated.QuestionSets.Single();
        var combined = string.Join(" ", set.Answers.Select(a => a.AnswerText));
        Assert.Equal(6, set.Answers.Count);
        Assert.True(approved.Score >= 90, string.Join(" | ", approved.Checks.SelectMany(c => c.Issues)));
        Assert.True(approved.IsApproved, string.Join(" | ", approved.Checks.SelectMany(c => c.Issues)));
        Assert.Contains(expectedTiming, set.Answers.Single(a => a.QuestionType == AstronomyQuestionTypes.When).AnswerText, StringComparison.OrdinalIgnoreCase);
        var howAnswer = set.Answers.Single(a => a.QuestionType == AstronomyQuestionTypes.How).AnswerText;
        Assert.Contains(new[] { "find", "look", "use", "scan", "avoid", "eyes", "certified", "binoculars", "follow" }, term => howAnswer.Contains(term, StringComparison.OrdinalIgnoreCase));
        var actionAnswer = set.Answers.Single(a => a.QuestionType == AstronomyQuestionTypes.Action).AnswerText;
        Assert.Contains(new[] { "reminder", "save", "check", "prepare", "choose", "plan", "pick", "watch", "enjoy" }, term => actionAnswer.Contains(term, StringComparison.OrdinalIgnoreCase));
        foreach (var term in requiredTerms)
            Assert.Contains(term, combined, StringComparison.OrdinalIgnoreCase);
        foreach (var term in forbiddenTerms)
            Assert.DoesNotContain(term, combined, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> EventQuestionCases()
    {
        yield return ["PERSEIDS_2026", "MeteorShower", "Perseids Meteor Shower Peak", new[] { "Perseids" }, """{ "skyDirectionHint": "northeast after midnight", "bestViewingWindowLocal": "2026-08-13 00:30–04:30 IST", "moonInterference": "Moderate", "moonIlluminationPercent": 42 }""", "2026-08-13 00:30–04:30 IST", new[] { "Perseids", "meteor", "No telescope", "dark" }, new[] { "Venus", "Jupiter", "conjunction" }];
        yield return ["MARS_JUPITER_2026", "PlanetPairing", "Mars Jupiter Pairing", new[] { "Mars", "Jupiter" }, """{ "skyDirectionHint": "east", "localPeakTime": "5:20 AM IST", "altitudeDegrees": 28, "angularSeparationDegrees": 0.9 }""", "5:20 AM IST", new[] { "Mars", "Jupiter", "pairing" }, new[] { "Venus", "meteor", "radiant" }];
        yield return ["MOON_SATURN_CONJUNCTION_2026", "Conjunction", "Moon Saturn Conjunction", new[] { "Moon", "Saturn" }, """{ "skyDirectionHint": "south", "localPeakTime": "9:15 PM IST", "altitudeDegrees": 35, "angularSeparationDegrees": 1.2 }""", "9:15 PM IST", new[] { "Moon", "Saturn", "conjunction", "alignment" }, new[] { "Venus", "Jupiter", "meteor", "radiant" }];
        yield return ["FULL_MOON_2026", "NamedFullMoon", "Strawberry Full Moon", new[] { "Moon" }, """{ "skyDirectionHint": "east", "localPeakTime": "7:10 PM IST" }""", "7:10 PM IST", new[] { "Moon", "full moon", "moonrise" }, new[] { "meteor", "radiant", "dark-sky" }];
        yield return ["NEW_MOON_2026", "NewMoon", "New Moon", new[] { "Moon" }, """{ "skyDirectionHint": "south", "bestViewingWindowLocal": "2026-06-15 21:00–04:30 IST" }""", "2026-06-15 21:00–04:30 IST", new[] { "New Moon", "dark", "stargazing" }, new[] { "fully illuminated", "look for the Moon", "meteor" }];
        yield return ["LUNAR_ECLIPSE_2026", "LunarEclipse", "Total Lunar Eclipse", new[] { "Moon" }, """{ "skyDirectionHint": "southwest", "bestViewingWindowLocal": "2026-09-07 22:30–01:30 IST" }""", "2026-09-07 22:30–01:30 IST", new[] { "eclipse", "Moon", "phase" }, new[] { "certified eclipse glasses", "meteor", "radiant" }];
        yield return ["SOLAR_ECLIPSE_2026", "SolarEclipse", "Partial Solar Eclipse", new[] { "Sun", "Moon" }, """{ "localVisibility": "Udaipur", "bestViewingWindowLocal": "2026-08-12 16:10–17:20 IST" }""", "2026-08-12 16:10–17:20 IST", new[] { "solar eclipse", "certified eclipse glasses", "Sun" }, new[] { "look directly at the Sun", "meteor", "radiant" }];
    }

    private static AstronomyQuestionEngine CreateService(MediaFactoryDbContext db, string workingDirectory)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }), CreateStrategyResolver(), NullLogger<AstronomyQuestionEngine>.Instance);

    private static IMediaEventStrategyResolver CreateStrategyResolver()
        => new MediaEventStrategyResolver([new MeteorShowerStrategy(), new PlanetPairingStrategy(), new PlanetGroupingStrategy(), new ConjunctionStrategy(), new NamedFullMoonStrategy(), new NewMoonStrategy(), new LunarEclipseStrategy(), new SolarEclipseStrategy(), new GenericAstronomyEventStrategy()]);

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
