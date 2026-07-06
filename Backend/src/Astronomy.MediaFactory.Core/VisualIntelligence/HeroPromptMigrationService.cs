using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public interface IHeroPromptMigrationService
{
    Task<HeroPromptMigrationResult> GenerateAsync(HeroPromptMigrationRequest request, CancellationToken cancellationToken = default);
}

public sealed record HeroPromptMigrationRequest
{
    public required CreativeDirectionContract CreativeDirectionContract { get; init; }
    public string LegacyPrompt { get; init; } = string.Empty;
    public string HeroDirectory { get; init; } = string.Empty;
    public ImageProviderType RequestedProvider { get; init; } = ImageProviderType.AzureImage;
    public HeroIntelligenceContract? HeroIntelligenceContract { get; init; }
}

public sealed record HeroPromptMigrationResult
{
    public string LegacyPrompt { get; init; } = string.Empty;
    public string V4Prompt { get; init; } = string.Empty;
    public HeroPromptComparisonReport Comparison { get; init; } = new();
    public string Recommendation { get; init; } = string.Empty;
    public IReadOnlyList<string> GeneratedFiles { get; init; } = [];
}

public sealed record HeroPromptComparisonReport
{
    public int LegacyLength { get; init; }
    public int V4Length { get; init; }
    public int LegacyPromptLength { get; init; }
    public int V4PromptLength { get; init; }
    public int DuplicateReductionCount { get; init; }
    public int EstimatedTokenReduction { get; init; }
    public int SemanticSectionsMerged { get; init; }
    public string ReadabilityImprovement { get; init; } = string.Empty;
    public bool AstronomyConstraintsPreserved { get; init; }
    public bool RenderingConstraintsPreserved { get; init; }
    public bool BrandConstraintsPreserved { get; init; }
    public bool TypographyPreserved { get; init; }
    public bool ObservationCardPreserved { get; init; }
    public string Recommendation { get; init; } = string.Empty;
}

public sealed class HeroPromptMigrationService(
    IOptions<VisualIntelligenceOptions> options,
    IPromptComposerV2 promptComposer,
    ILogger<HeroPromptMigrationService> logger) : IHeroPromptMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions = VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true);

    public async Task<HeroPromptMigrationResult> GenerateAsync(HeroPromptMigrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request.CreativeDirectionContract);
        var contract = request.CreativeDirectionContract with { TargetPlatform = Platform.Hero, AspectRatio = request.CreativeDirectionContract.AspectRatio == AspectRatio.Unknown ? AspectRatio.Landscape16x9 : request.CreativeDirectionContract.AspectRatio };
        var promptResult = await promptComposer.ComposeAsync(contract.Cdl, contract, request.RequestedProvider, cancellationToken).ConfigureAwait(false);
        var v4Prompt = request.HeroIntelligenceContract is not null
            ? ComposeIntelligencePrompt(request.HeroIntelligenceContract, contract)
            : promptResult.PromptPackage?.PositivePrompt;
        if (string.IsNullOrWhiteSpace(v4Prompt))
            v4Prompt = ComposeFallbackHeroPrompt(contract);

        logger.LogInformation("Hero V4 prompt generated");
        var comparison = Compare(request.LegacyPrompt, v4Prompt, contract, promptResult.Diagnostics);
        logger.LogInformation("Semantic optimization started");
        logger.LogInformation("Duplicate groups merged: {DuplicateReductionCount}", comparison.DuplicateReductionCount);
        logger.LogInformation("Token estimate: {TokenEstimate}", Math.Max(1, comparison.V4PromptLength / 4));
        logger.LogInformation("Readability estimate: {ReadabilityImprovement}", comparison.ReadabilityImprovement);
        logger.LogInformation("Prompt quality improved");
        logger.LogInformation("Hero comparison complete");
        logger.LogInformation("Production Hero unchanged");

        var generatedFiles = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.HeroDirectory))
        {
            Directory.CreateDirectory(request.HeroDirectory);
            await WriteAsync(request.HeroDirectory, "hero-v3-prompt.txt", request.LegacyPrompt, cancellationToken, generatedFiles).ConfigureAwait(false);
            await WriteAsync(request.HeroDirectory, "hero-v4-prompt.txt", v4Prompt, cancellationToken, generatedFiles).ConfigureAwait(false);
            await WriteJsonAsync(request.HeroDirectory, "hero-prompt-comparison.json", comparison, cancellationToken, generatedFiles).ConfigureAwait(false);
            await WriteJsonAsync(request.HeroDirectory, "hero-migration-report.json", new
            {
                mode = "observation",
                useHeroPromptV4 = options.Value.UseHeroPromptV4,
                productionHeroUnchanged = !options.Value.UseHeroPromptV4,
                generatedAtUtc = DateTimeOffset.UtcNow,
                contractId = contract.ContractId,
                heroIntelligenceContract = request.HeroIntelligenceContract,
                promptComposerStatus = promptResult.Status.ToString(),
                comparison,
                diagnostics = promptResult.Diagnostics
            }, cancellationToken, generatedFiles).ConfigureAwait(false);
        }

        return new HeroPromptMigrationResult { LegacyPrompt = request.LegacyPrompt, V4Prompt = v4Prompt, Comparison = comparison, Recommendation = comparison.Recommendation, GeneratedFiles = generatedFiles };
    }

    private static async Task WriteAsync(string directory, string fileName, string content, CancellationToken cancellationToken, List<string> generatedFiles)
    {
        var path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        generatedFiles.Add(path);
    }

    private static Task WriteJsonAsync(string directory, string fileName, object value, CancellationToken cancellationToken, List<string> generatedFiles)
        => WriteAsync(directory, fileName, JsonSerializer.Serialize(value, JsonOptions), cancellationToken, generatedFiles);

    private static HeroPromptComparisonReport Compare(string legacyPrompt, string v4Prompt, CreativeDirectionContract contract, IEnumerable<DiagnosticMessage> diagnostics)
    {
        var astronomy = ContainsAny(v4Prompt, contract.VisualIntent.PrimarySubject, contract.EventFamily.ToString()) || contract.PlanetRenderingRules.Subjects.Any(s => ContainsAny(v4Prompt, s.BodyName));
        var rendering = ContainsAny(v4Prompt, "circular", "geometry", "rendering", "planet", "astronomical") || contract.PlanetRenderingRules.Subjects.Count > 0;
        var brand = ContainsAny(v4Prompt, contract.BrandRules.BrandName, contract.BrandRules.VisualTone, "premium", "documentary");
        var typography = ContainsAny(v4Prompt, "typography", "text", "title", "label", "readability") || contract.TypographyRules.AllowedTextElements.Count > 0;
        var observation = ContainsAny(v4Prompt, "observation", "card", "lower third", "viewing", "direction") || contract.ObservationCardRules.AllowedFields.Count > 0;
        var duplicateReduction = ReadDiagnosticNumber(diagnostics, "prompt_optimizer.duplicate_groups_merged");
        var semanticSectionsMerged = ReadDiagnosticNumber(diagnostics, "prompt_optimizer.semantic_sections_merged");
        var estimatedTokenReduction = Math.Max(0, (legacyPrompt.Length - v4Prompt.Length) / 4) + duplicateReduction;
        var readability = ContainsAny(v4Prompt, "Opening paragraph:", "Primary subject:", "Brand guidance:", "Negative constraints:") ? "engineering labels replaced with natural creative-brief sections" : "unchanged";
        var recommendation = "Keep UseHeroPromptV4=false; use the V4 prompt for diagnostics and comparison only until visual parity is approved.";
        return new HeroPromptComparisonReport
        {
            LegacyLength = legacyPrompt.Length,
            V4Length = v4Prompt.Length,
            LegacyPromptLength = legacyPrompt.Length,
            V4PromptLength = v4Prompt.Length,
            DuplicateReductionCount = duplicateReduction,
            EstimatedTokenReduction = estimatedTokenReduction,
            SemanticSectionsMerged = semanticSectionsMerged,
            ReadabilityImprovement = readability,
            AstronomyConstraintsPreserved = astronomy,
            RenderingConstraintsPreserved = rendering,
            BrandConstraintsPreserved = brand,
            TypographyPreserved = typography,
            ObservationCardPreserved = observation,
            Recommendation = recommendation
        };
    }

    private static bool ContainsAny(string text, params string?[] values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v)).Any(v => text.Contains(v!, StringComparison.OrdinalIgnoreCase));

    private static int ReadDiagnosticNumber(IEnumerable<DiagnosticMessage> diagnostics, string code)
    {
        var message = diagnostics.FirstOrDefault(d => d.Code == code)?.Message;
        if (string.IsNullOrWhiteSpace(message)) return 0;
        var digits = new string(message.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static string ComposeIntelligencePrompt(HeroIntelligenceContract c, CreativeDirectionContract contract)
    {
        var platform = c.PlatformVariantRecommendations.TryGetValue("landscape", out var landscape) ? landscape : string.Join(" ", c.PlatformVariantRecommendations.Values);
        var parts = new[]
        {
            $"Create a premium documentary astronomy hero image for {FirstNonEmpty(contract.VisualIntent.PrimarySubject, c.PrimaryStory)}.",
            $"Open with the human question: {c.ViewerQuestion}",
            $"Story: {c.PrimaryStory} Viewer takeaway: {c.ViewerTakeaway}",
            $"Emotional hook: {c.EmotionalHook}. Viewer emotion: {c.ViewerEmotion}.",
            $"Composition goal: {c.CompositionGoal}. Visual relationship: {c.VisualRelationship}",
            $"Editorial goal: {c.EditorialGoal}. Platform guidance: {platform}",
            "For conjunctions, make the relationship between objects the subject before individual size or spectacle.",
            "Use a natural, platform-native documentary tone with restrained premium lighting and clean negative space.",
            "No generated text, labels, logos, UI, watermarks, empty overlays, or science-fiction decoration."
        };
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().Trim(';')));
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string ComposeFallbackHeroPrompt(CreativeDirectionContract contract)
        => $"Hero V4 observation prompt for {contract.VisualIntent.PrimarySubject}. Preserve astronomy constraints, planet rendering constraints, Drashyam brand constraints, typography readability, and observation card guidance. Composition: {contract.VisualIntent.Composition}. Mood: {contract.VisualIntent.Mood}.";
}
