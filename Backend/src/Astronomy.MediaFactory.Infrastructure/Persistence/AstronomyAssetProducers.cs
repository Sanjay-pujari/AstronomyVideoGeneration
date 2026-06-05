using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public abstract partial class AstronomyAssetProducerBase : IAstronomyAssetProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public abstract string ProducerName { get; }
    protected abstract IReadOnlySet<string> SupportedAssetTypes { get; }
    protected abstract IReadOnlyList<string> RequiredMetadataKeys { get; }
    protected abstract string RequestType { get; }
    protected abstract string SafetyNote { get; }
    protected abstract AssetProducerEstimateResult Estimate { get; }

    public bool CanHandle(AstronomyAssetProductionJob job)
        => SupportedAssetTypes.Contains(NormalizeAssetType(job.AssetType));

    public virtual Task<AssetProducerValidationResult> ValidateAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = ParseMetadata(job);
        var messages = new List<string>();

        foreach (var key in RequiredMetadataKeys)
        {
            if (!HasValue(metadata, key))
                messages.Add($"Missing required metadata '{key}'.");
        }

        if (messages.Count == 0)
            messages.Add("Preview validation passed; no production execution will occur.");

        return Task.FromResult(messages.Count == 1 && messages[0].StartsWith("Preview validation", StringComparison.Ordinal)
            ? AssetProducerValidationResult.Valid(messages)
            : AssetProducerValidationResult.Invalid(messages));
    }

    public Task<AssetProducerEstimateResult> EstimateAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Estimate);
    }

    public Task<AssetProductionRequestPreview> CreateProductionRequestAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = ParseMetadata(job);
        var parameters = BuildCommonParameters(job, metadata);
        AddProducerParameters(parameters, job, metadata);

        return Task.FromResult(new AssetProductionRequestPreview(
            RequestType,
            job.PlannedProvider,
            false,
            parameters,
            [SafetyNote, "Preview only: database status, output paths, and production timestamps are not modified."]));
    }

    protected virtual void AddProducerParameters(JsonObject parameters, AstronomyAssetProductionJob job, JsonObject metadata)
    {
        foreach (var property in metadata)
            parameters[property.Key] = property.Value?.DeepClone();
    }

    protected static JsonObject ParseMetadata(AstronomyAssetProductionJob job)
    {
        if (string.IsNullOrWhiteSpace(job.MetadataJson))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(job.MetadataJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    protected static JsonArray ObjectNames(AstronomyAssetProductionJob job)
    {
        if (string.IsNullOrWhiteSpace(job.ObjectNamesJson))
            return new JsonArray();

        try
        {
            var names = JsonSerializer.Deserialize<IReadOnlyList<string>>(job.ObjectNamesJson, JsonOptions) ?? Array.Empty<string>();
            return new JsonArray(names.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        }
        catch (JsonException)
        {
            return new JsonArray();
        }
    }

    protected static string NormalizeAssetType(string? assetType)
        => (assetType ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static JsonObject BuildCommonParameters(AstronomyAssetProductionJob job, JsonObject metadata) => new()
    {
        ["jobId"] = job.Id.ToString(),
        ["contentGenerationPlanId"] = job.ContentGenerationPlanId.ToString(),
        ["sceneNumber"] = job.SceneNumber,
        ["sceneName"] = job.SceneName,
        ["assetType"] = job.AssetType,
        ["assetPurpose"] = job.AssetPurpose,
        ["promptOrInstruction"] = job.PromptOrInstruction,
        ["expectedOutputType"] = job.ExpectedOutputType,
        ["objectNames"] = ObjectNames(job),
        ["metadata"] = metadata.DeepClone()
    };

    protected static bool HasMetadataValue(JsonObject metadata, string key) => HasValue(metadata, key);

    private static bool HasValue(JsonObject metadata, string key)
    {
        if (!metadata.TryGetPropertyValue(key, out var value) || value is null)
            return false;

        return value switch
        {
            JsonValue jsonValue => HasJsonValue(jsonValue),
            JsonArray array => array.Count > 0,
            JsonObject obj => obj.Count > 0,
            _ => true
        };
    }

    private static bool HasJsonValue(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text))
            return !string.IsNullOrWhiteSpace(text);
        if (value.TryGetValue<bool>(out _))
            return true;
        if (value.TryGetValue<DateTimeOffset>(out var dateTimeOffset))
            return dateTimeOffset != default;
        return true;
    }
}

public sealed class TextOverlayAssetProducer : AstronomyAssetProducerBase
{
    public override string ProducerName => nameof(TextOverlayAssetProducer);
    protected override IReadOnlySet<string> SupportedAssetTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "textoverlaycard" };
    protected override IReadOnlyList<string> RequiredMetadataKeys { get; } = ["titleText", "subtitleText", "dataPoints"];
    protected override string RequestType => "TextOverlayCardPreview";
    protected override string SafetyNote => "No image generation is performed for text overlay previews.";
    protected override AssetProducerEstimateResult Estimate { get; } = new(3, "Low", "Low");
}

public sealed class ThumbnailConceptAssetProducer : AstronomyAssetProducerBase
{
    public override string ProducerName => nameof(ThumbnailConceptAssetProducer);
    protected override IReadOnlySet<string> SupportedAssetTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thumbnailconcept" };
    protected override IReadOnlyList<string> RequiredMetadataKeys { get; } = ["thumbnailText", "keyObjects", "composition"];
    protected override string RequestType => "ThumbnailConceptPreview";
    protected override string SafetyNote => "No thumbnail image generation is performed.";
    protected override AssetProducerEstimateResult Estimate { get; } = new(3, "Low", "Low");
}

public sealed class StellariumScreenshotAssetProducer : AstronomyAssetProducerBase
{
    public override string ProducerName => nameof(StellariumScreenshotAssetProducer);
    protected override IReadOnlySet<string> SupportedAssetTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "stellariumscreenshot" };
    protected override IReadOnlyList<string> RequiredMetadataKeys { get; } = ["targetObjects", "regionId", "locationName", "suggestedOrientation"];
    protected override string RequestType => "FutureStellariumSscCapturePreview";
    protected override string SafetyNote => "No SSC is generated and Stellarium is not executed.";
    protected override AssetProducerEstimateResult Estimate { get; } = new(60, "Local compute", "High");

    public override async Task<AssetProducerValidationResult> ValidateAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken)
    {
        var baseResult = await base.ValidateAsync(job, cancellationToken);
        var messages = baseResult.Messages.Where(m => !m.StartsWith("Preview validation", StringComparison.Ordinal)).ToList();
        var metadata = ParseMetadata(job);
        if (!HasMetadataValue(metadata, "scheduledUtc") && !HasMetadataValue(metadata, "peakUtc"))
            messages.Add("Missing required metadata 'scheduledUtc' or 'peakUtc'.");
        return messages.Count == 0
            ? AssetProducerValidationResult.Valid(["Preview validation passed; future Stellarium SSC/capture request can be described without execution."])
            : AssetProducerValidationResult.Invalid(messages);
    }
}

public sealed class ConstellationGuideAssetProducer : AstronomyAssetProducerBase
{
    public override string ProducerName => nameof(ConstellationGuideAssetProducer);
    protected override IReadOnlySet<string> SupportedAssetTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "constellationguide" };
    protected override IReadOnlyList<string> RequiredMetadataKeys { get; } = ["instruction"];
    protected override string RequestType => "ConstellationGuidePreview";
    protected override string SafetyNote => "No constellation guide asset is generated.";
    protected override AssetProducerEstimateResult Estimate { get; } = new(10, "Low", "Medium");

    public override async Task<AssetProducerValidationResult> ValidateAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken)
    {
        var baseResult = await base.ValidateAsync(job, cancellationToken);
        var messages = baseResult.Messages.Where(m => !m.StartsWith("Preview validation", StringComparison.Ordinal)).ToList();
        if (ObjectNames(job).Count == 0)
            messages.Add("Missing required objectNames.");
        return messages.Count == 0 ? AssetProducerValidationResult.Valid(["Preview validation passed; guide request only."]) : AssetProducerValidationResult.Invalid(messages);
    }
}

public sealed class SkyMapCardAssetProducer : AstronomyAssetProducerBase
{
    public override string ProducerName => nameof(SkyMapCardAssetProducer);
    protected override IReadOnlySet<string> SupportedAssetTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "skymapcard" };
    protected override IReadOnlyList<string> RequiredMetadataKeys { get; } = ["instruction"];
    protected override string RequestType => "InternalSkyMapCardPreview";
    protected override string SafetyNote => "No internal sky map card file is generated.";
    protected override AssetProducerEstimateResult Estimate { get; } = new(8, "Low", "Low");

    public override async Task<AssetProducerValidationResult> ValidateAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken)
    {
        var baseResult = await base.ValidateAsync(job, cancellationToken);
        var messages = baseResult.Messages.Where(m => !m.StartsWith("Preview validation", StringComparison.Ordinal)).ToList();
        if (ObjectNames(job).Count == 0)
            messages.Add("Missing required objectNames.");
        return messages.Count == 0 ? AssetProducerValidationResult.Valid(["Preview validation passed; internal sky map card request only."]) : AssetProducerValidationResult.Invalid(messages);
    }
}

public sealed class NasaAssetProducer : AstronomyAssetProducerBase
{
    public override string ProducerName => nameof(NasaAssetProducer);
    protected override IReadOnlySet<string> SupportedAssetTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nasaasset" };
    protected override IReadOnlyList<string> RequiredMetadataKeys { get; } = ["searchTerms", "fallbackToAiImage"];
    protected override string RequestType => "NasaSearchPreview";
    protected override string SafetyNote => "NASA APIs are not called.";
    protected override AssetProducerEstimateResult Estimate { get; } = new(20, "Low/External", "Medium");
}

public sealed class AiImageAssetProducer : AstronomyAssetProducerBase
{
    public override string ProducerName => nameof(AiImageAssetProducer);
    protected override IReadOnlySet<string> SupportedAssetTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aiheroimage", "aicinematicimage" };
    protected override IReadOnlyList<string> RequiredMetadataKeys { get; } = ["imagePrompt", "aspectRatio", "style"];
    protected override string RequestType => "AiImageGenerationPreview";
    protected override string SafetyNote => "AI image generation is not called.";
    protected override AssetProducerEstimateResult Estimate { get; } = new(40, "AI", "Medium/High");
}
