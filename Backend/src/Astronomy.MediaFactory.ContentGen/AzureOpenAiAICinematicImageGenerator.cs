using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AzureTokenRequestContext = Azure.Core.TokenRequestContext;

namespace Astronomy.MediaFactory.ContentGen;

public sealed class AzureOpenAiAICinematicImageGenerator : IAICinematicImageGenerator
{
    private const string ApiVersion = "2024-10-21";
    private const int AzureOpenAiImageTimeoutSeconds = 180;
    private static readonly string[] PreferredLandscapeSizes = ["1792x1024", "1536x1024"];
    private static readonly AzureTokenRequestContext AzureCognitiveServicesScope = new(["https://cognitiveservices.azure.com/.default"]);

    private readonly HttpClient _httpClient;
    private readonly AzureOpenAiOptions _options;
    private readonly ILogger<AzureOpenAiAICinematicImageGenerator> _logger;
    private readonly TokenCredential? _credential;

    public AzureOpenAiAICinematicImageGenerator(
        HttpClient httpClient,
        IOptions<AzureOpenAiOptions> options,
        ILogger<AzureOpenAiAICinematicImageGenerator> logger,
        TokenCredential? credential = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credential = _options.UseManagedIdentity
            ? credential ?? new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = string.IsNullOrWhiteSpace(_options.ManagedIdentityClientId) ? null : _options.ManagedIdentityClientId.Trim()
            })
            : credential;
    }

    public bool IsConfigured => GetConfigurationWarnings().Count == 0;

    public async Task<AICinematicProviderResult> GenerateAsync(AICinematicAssetRequest request, CancellationToken cancellationToken)
    {
        var configurationWarnings = GetConfigurationWarnings();
        if (configurationWarnings.Count > 0)
        {
            return new AICinematicProviderResult(
                "ProviderNotConfigured",
                null,
                ProviderConfigured: false,
                configurationWarnings);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.PlannedImagePath) ?? Directory.GetCurrentDirectory());
        var warnings = new List<string>();

        foreach (var size in PreferredLandscapeSizes)
        {
            try
            {
                var imageBytes = await RequestImageBytesAsync(request, size, cancellationToken);
                await File.WriteAllBytesAsync(request.PlannedImagePath, imageBytes, cancellationToken);
                return new AICinematicProviderResult(
                    "Generated",
                    request.PlannedImagePath,
                    ProviderConfigured: true,
                    warnings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (IsLikelyUnsupportedSize(ex))
            {
                _logger.LogWarning(ex, "Azure OpenAI image generation rejected size {Size} for deployment {Deployment}.", size, _options.ImageDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI image generation failed for assetId={AssetId} deployment={Deployment}.", request.AssetId, _options.ImageDeployment);
                warnings.Add($"Azure OpenAI image generation failed: {ex.Message}");
                return new AICinematicProviderResult(
                    "GenerationFailed",
                    null,
                    ProviderConfigured: true,
                    warnings);
            }
        }

        warnings.Add("Azure OpenAI image generation failed because no supported landscape image size was accepted by the configured deployment.");
        return new AICinematicProviderResult(
            "GenerationFailed",
            null,
            ProviderConfigured: true,
            warnings);
    }

    private IReadOnlyList<string> GetConfigurationWarnings()
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.Endpoint)) warnings.Add("AzureOpenAI:Endpoint is required for AI cinematic image generation.");
        if (!_options.UseManagedIdentity && string.IsNullOrWhiteSpace(_options.ApiKey)) warnings.Add("AzureOpenAI:ApiKey is required for AI cinematic image generation unless AzureOpenAI:UseManagedIdentity=true.");
        if (_options.UseManagedIdentity && _credential is null) warnings.Add("Azure OpenAI managed identity credential is not available for AI cinematic image generation.");
        if (string.IsNullOrWhiteSpace(_options.ImageDeployment)) warnings.Add("AzureOpenAI:ImageDeployment is required for AI cinematic image generation.");
        return warnings;
    }

    private async Task<byte[]> RequestImageBytesAsync(AICinematicAssetRequest assetRequest, string size, CancellationToken cancellationToken)
    {
        var endpoint = _options.Endpoint.TrimEnd('/');
        var deployment = Uri.EscapeDataString(_options.ImageDeployment.Trim());
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/images/generations?api-version={ApiVersion}";
        var prompt = string.Join('\n', new[]
        {
            assetRequest.Prompt,
            $"Style profile: {assetRequest.StyleProfile}.",
            $"Negative constraints: {assetRequest.NegativePrompt}",
            "Create one clean production still image only. Do not include text, labels, watermarks, logos, borders, or UI."
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new
            {
                prompt,
                n = 1,
                size
            })
        };

        await AddAuthorizationAsync(request, cancellationToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(AzureOpenAiImageTimeoutSeconds));
        _logger.LogInformation(
            "Azure OpenAI image call starting. imageDeployment={Deployment}; assetId={AssetId}; size={Size}; startedUtc={StartedUtc:o}; timeoutSeconds={TimeoutSeconds}",
            _options.ImageDeployment,
            assetRequest.AssetId,
            size,
            startedUtc,
            AzureOpenAiImageTimeoutSeconds);

        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure OpenAI image request failed with status {(int)response.StatusCode} ({response.StatusCode}) for size {size}. Body: {payload}",
                null,
                response.StatusCode);
        }

        _logger.LogInformation(
            "Azure OpenAI image call completed. imageDeployment={Deployment}; assetId={AssetId}; size={Size}; startedUtc={StartedUtc:o}; completedUtc={CompletedUtc:o}; elapsedMs={ElapsedMs}; responseLength={ResponseLength}",
            _options.ImageDeployment,
            assetRequest.AssetId,
            size,
            startedUtc,
            DateTimeOffset.UtcNow,
            stopwatch.ElapsedMilliseconds,
            payload.Length);

        return await ExtractImageBytesAsync(payload, cancellationToken);
    }

    private async Task AddAuthorizationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_options.UseManagedIdentity)
        {
            var accessToken = await (_credential ?? throw new InvalidOperationException("Azure OpenAI managed identity credential is not available."))
                .GetTokenAsync(AzureCognitiveServicesScope, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        }
        else
        {
            request.Headers.Add("api-key", _options.ApiKey);
        }
    }

    private async Task<byte[]> ExtractImageBytesAsync(string payload, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array || dataElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Azure OpenAI image response did not include image data.");
        }

        var imageElement = dataElement[0];
        if (imageElement.TryGetProperty("b64_json", out var b64Element) && b64Element.ValueKind == JsonValueKind.String)
        {
            var b64 = b64Element.GetString();
            if (!string.IsNullOrWhiteSpace(b64)) return Convert.FromBase64String(b64);
        }

        if (imageElement.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
        {
            var url = urlElement.GetString();
            if (!string.IsNullOrWhiteSpace(url)) return await _httpClient.GetByteArrayAsync(url, cancellationToken);
        }

        throw new InvalidOperationException("Azure OpenAI image response did not include b64_json or url output.");
    }

    private static bool IsLikelyUnsupportedSize(HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.BadRequest
        && ex.Message.Contains("size", StringComparison.OrdinalIgnoreCase);
}
