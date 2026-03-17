using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.ContentGen;

public sealed class AzureOpenAiContentGenerationService : IScriptGenerationService
{
    private const string ApiVersion = "2024-10-21";

    private readonly HttpClient _httpClient;
    private readonly AzureOpenAiOptions _options;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ILogger<AzureOpenAiContentGenerationService> _logger;

    public AzureOpenAiContentGenerationService(
        HttpClient httpClient,
        IOptions<AzureOpenAiOptions> options,
        IPromptBuilder promptBuilder,
        ILogger<AzureOpenAiContentGenerationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _promptBuilder = promptBuilder;
        _logger = logger;
    }

    public async Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
    {
        var prompt = _promptBuilder.Build(contentType, context);

        try
        {
            var completion = await RequestCompletionAsync(prompt, cancellationToken);
            var parsed = ParseContent(completion);

            if (parsed is null)
            {
                _logger.LogError("Unable to parse Azure OpenAI JSON output. Falling back to template content.");
                return BuildFallback(contentType, context, prompt);
            }

            return new ScriptResult
            {
                Prompt = prompt,
                Title = parsed.Title,
                Description = parsed.Description,
                Tags = parsed.Tags,
                EstimatedDurationSeconds = parsed.EstimatedDurationSeconds,
                ScriptBody = parsed.ScriptBody
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI content generation failed. Falling back to template content.");
            return BuildFallback(contentType, context, prompt);
        }
    }

    private async Task<string> RequestCompletionAsync(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ChatDeployment))
        {
            throw new InvalidOperationException("Azure OpenAI configuration is missing Endpoint, ApiKey, or ChatDeployment.");
        }

        var endpoint = _options.Endpoint.TrimEnd('/');
        var requestUri = $"{endpoint}/openai/deployments/{_options.ChatDeployment}/chat/completions?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new
            {
                messages = new object[]
                {
                    new { role = "system", content = "You are a precise assistant that always returns valid JSON." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.4,
                response_format = new { type = "json_object" }
            })
        };

        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<AzureChatResponse>(payload);
        return chatResponse?.Choices.FirstOrDefault()?.Message.Content
            ?? throw new InvalidOperationException("Azure OpenAI response did not include message content.");
    }

    private GeneratedContent? ParseContent(string rawContent)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<GeneratedContent>(rawContent, JsonSerializerOptions.Web);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ScriptBody) || string.IsNullOrWhiteSpace(parsed.Title))
            {
                return null;
            }

            parsed.Tags = parsed.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (parsed.Tags.Length == 0)
            {
                parsed.Tags = ["astronomy", "night sky"];
            }

            parsed.EstimatedDurationSeconds = parsed.EstimatedDurationSeconds <= 0 ? 900 : parsed.EstimatedDurationSeconds;
            return parsed;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing failed for Azure OpenAI content payload: {Payload}", rawContent);
            return null;
        }
    }

    private static ScriptResult BuildFallback(ContentType contentType, AstronomyContext context, string prompt)
    {
        var title = $"What to See in the Sky - {context.Date:MMMM dd, yyyy}";
        var description = $"Astronomy guide for {context.Date:MMMM dd, yyyy} in {context.LocationName}.";
        var eventLines = context.Events
            .OrderByDescending(x => x.Score)
            .Select(x => $"Look for {x.ObjectName} {x.VisibilityWindow} toward the {x.Direction} using {x.ObservationTool}. {x.Details}")
            .DefaultIfEmpty("Tonight offers a chance to step outside and observe the sky with the naked eye.");

        return new ScriptResult
        {
            Prompt = prompt,
            Title = title,
            Description = description,
            Tags = ["astronomy", "night sky", contentType.ToString()],
            EstimatedDurationSeconds = 900,
            ScriptBody = $"Welcome to your astronomy update for {context.Date:MMMM dd, yyyy}. {string.Join(" ", eventLines)}"
        };
    }

    private sealed class AzureChatResponse
    {
        public AzureChoice[] Choices { get; init; } = Array.Empty<AzureChoice>();
    }

    private sealed class AzureChoice
    {
        public AzureMessage Message { get; init; } = new();
    }

    private sealed class AzureMessage
    {
        public string Content { get; init; } = string.Empty;
    }

    private sealed class GeneratedContent
    {
        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("tags")]
        public string[] Tags { get; set; } = Array.Empty<string>();

        [JsonPropertyName("estimatedDurationSeconds")]
        public int EstimatedDurationSeconds { get; set; }

        [JsonPropertyName("scriptBody")]
        public string ScriptBody { get; init; } = string.Empty;
    }
}
