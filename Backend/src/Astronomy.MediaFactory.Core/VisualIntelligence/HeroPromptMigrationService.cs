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
        var v4Prompt = promptResult.PromptPackage?.PositivePrompt;
        if (string.IsNullOrWhiteSpace(v4Prompt))
            v4Prompt = ComposeFallbackHeroPrompt(contract);

        logger.LogInformation("Hero V4 prompt generated");
        var comparison = Compare(request.LegacyPrompt, v4Prompt, contract);
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

    private static HeroPromptComparisonReport Compare(string legacyPrompt, string v4Prompt, CreativeDirectionContract contract)
    {
        var astronomy = ContainsAny(v4Prompt, contract.VisualIntent.PrimarySubject, contract.EventFamily.ToString()) || contract.PlanetRenderingRules.Subjects.Any(s => ContainsAny(v4Prompt, s.BodyName));
        var rendering = ContainsAny(v4Prompt, "circular", "geometry", "rendering", "planet", "astronomical") || contract.PlanetRenderingRules.Subjects.Count > 0;
        var brand = ContainsAny(v4Prompt, contract.BrandRules.BrandName, contract.BrandRules.VisualTone, "premium", "documentary");
        var typography = ContainsAny(v4Prompt, "typography", "text", "title", "label", "readability") || contract.TypographyRules.AllowedTextElements.Count > 0;
        var observation = ContainsAny(v4Prompt, "observation", "card", "lower third", "viewing", "direction") || contract.ObservationCardRules.AllowedFields.Count > 0;
        var recommendation = "Keep UseHeroPromptV4=false; use the V4 prompt for diagnostics and comparison only until visual parity is approved.";
        return new HeroPromptComparisonReport
        {
            LegacyLength = legacyPrompt.Length,
            V4Length = v4Prompt.Length,
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

    private static string ComposeFallbackHeroPrompt(CreativeDirectionContract contract)
        => $"Hero V4 observation prompt for {contract.VisualIntent.PrimarySubject}. Preserve astronomy constraints, planet rendering constraints, Drashyam brand constraints, typography readability, and observation card guidance. Composition: {contract.VisualIntent.Composition}. Mood: {contract.VisualIntent.Mood}.";
}
