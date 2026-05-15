using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ThumbnailAiOptimizationTests
{
    [Fact]
    public async Task OptimizeAsync_GeneratesEnglishHooksWithFiveWordsOrLess()
    {
        var outputDir = CreateTempDirectory();
        var service = CreateService();

        var result = await service.OptimizeAsync(new ThumbnailAiOptimizationRequest
        {
            GenerationRequest = BuildRequest(outputDir, "en"),
            SeoTitle = "Moon and Jupiter visible after sunset",
            TopPerformingHooks = ["Look West Tonight"]
        }, CancellationToken.None);

        Assert.NotEmpty(result.CandidateHooks);
        Assert.InRange(result.CandidateHooks.Count, 3, 5);
        Assert.All(result.CandidateHooks, hook => Assert.InRange(CountWords(hook), 1, 5));
        Assert.Contains(result.CandidateHooks, hook => hook.Contains("Tonight", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task OptimizeAsync_GeneratesNaturalHindiHooks()
    {
        var outputDir = CreateTempDirectory();
        var service = CreateService();

        var result = await service.OptimizeAsync(new ThumbnailAiOptimizationRequest
        {
            GenerationRequest = BuildRequest(outputDir, "hi"),
            SeoTitle = "आज रात चांद और बृहस्पति",
            Language = "hi"
        }, CancellationToken.None);

        Assert.NotEmpty(result.CandidateHooks);
        Assert.Contains(result.CandidateHooks, hook => hook.Contains("आज", StringComparison.OrdinalIgnoreCase) || hook.Contains("चांद", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.CandidateHooks, hook => hook.Contains("आज रात खगोल विज्ञान घटना", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("hi", result.Language);
    }

    [Theory]
    [InlineData("UFO Tonight")]
    [InlineData("Alien Planet")]
    [InlineData("Fake Apocalypse Sky")]
    public void Score_RejectsHallucinatedAndDisallowedHooks(string hook)
    {
        var scorer = new ThumbnailCtrScoringService(Options.Create(new ThumbnailAIOptimizationOptions()));

        var score = scorer.Score(hook, new ThumbnailAiOptimizationRequest
        {
            GenerationRequest = BuildRequest(CreateTempDirectory(), "en")
        });

        Assert.True(score.IsRejected);
        Assert.True(score.AstronomyAccuracy < 0.55 || score.RejectionReason!.Contains("disallowed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OptimizeAsync_SelectsHighestSafeScore()
    {
        var outputDir = CreateTempDirectory();
        var service = CreateService();

        var result = await service.OptimizeAsync(new ThumbnailAiOptimizationRequest
        {
            GenerationRequest = BuildRequest(outputDir, "en", metadata: new OptimizedVideoMetadata
            {
                HookLine = "UFO Tonight",
                ThumbnailTextSuggestions = ["Look West Tonight", "Moon Meets Jupiter"]
            }),
            TopPerformingHooks = ["Look West Tonight"]
        }, CancellationToken.None);

        Assert.Equal("Look West Tonight", result.SelectedHook);
        Assert.Contains("UFO Tonight", result.RejectedHooks);
        Assert.DoesNotContain("UFO", result.SelectedHook, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OptimizeAsync_WritesDiagnosticsFile()
    {
        var outputDir = CreateTempDirectory();
        var service = CreateService();

        var result = await service.OptimizeAsync(new ThumbnailAiOptimizationRequest
        {
            GenerationRequest = BuildRequest(outputDir, "en"),
            TopPerformingHooks = ["Moon Tonight"]
        }, CancellationToken.None);

        var path = Path.Combine(outputDir, "thumbnails", "thumbnail-ai-optimization.json");
        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.True(document.RootElement.TryGetProperty("candidateHooks", out _));
        Assert.Equal(result.SelectedHook, document.RootElement.GetProperty("selectedHook").GetString());
        Assert.True(document.RootElement.TryGetProperty("analyticsInfluence", out _));
        Assert.True(document.RootElement.TryGetProperty("hallucinationDetected", out _));
    }

    [Fact]
    public void ThumbnailStrategy_CompositionPlanStillBuildsVariants()
    {
        var plan = new ThumbnailStrategyService().BuildPlan(BuildRequest(CreateTempDirectory(), "en"));

        Assert.NotEmpty(plan.PrimaryThumbnailText);
        Assert.NotEmpty(plan.LayoutCandidates);
        Assert.NotEmpty(plan.Variants);
    }

    private static ThumbnailAiOptimizationService CreateService(ThumbnailAIOptimizationOptions? options = null)
    {
        var wrapped = Options.Create(options ?? new ThumbnailAIOptimizationOptions());
        return new ThumbnailAiOptimizationService(new ThumbnailCtrScoringService(wrapped), wrapped);
    }

    private static ThumbnailGenerationRequest BuildRequest(string outputDir, string language, OptimizedVideoMetadata? metadata = null)
        => new()
        {
            ContentType = ContentType.SpecialEventGuide,
            Context = new AstronomyContext
            {
                Date = new DateOnly(2026, 5, 15),
                LocationName = "Udaipur, India",
                Localization = new LocalizationContext(language, string.Empty, language, false),
                SpecialEvent = new SpecialEventContext
                {
                    EventId = "moon-jupiter-20260515",
                    EventType = "conjunction",
                    EventTitle = language == "hi" ? "चांद और बृहस्पति" : "Moon Meets Jupiter",
                    EventDescription = "The Moon appears near Jupiter after sunset."
                },
                SceneObservationContexts =
                [
                    new SceneObservationContext
                    {
                        SceneId = "moon-jupiter",
                        ObjectName = language == "hi" ? "चांद" : "Moon",
                        ObjectType = "Moon",
                        DirectionLabel = "West",
                        AltitudeDegrees = 42,
                        LocationName = "Udaipur, India"
                    }
                ]
            },
            Metadata = metadata ?? new OptimizedVideoMetadata { PrimaryTitle = "Moon and Jupiter visible after sunset" },
            AvailableVisuals = [],
            OutputDirectory = outputDir,
            FeedbackSignals = new FeedbackSignals { BestHooks = ["Look West Tonight"], TopKeywords = ["moon", "tonight"] }
        };

    private static int CountWords(string hook)
        => hook.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"thumb-ai-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
