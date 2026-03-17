using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.ContentGen;

public sealed class AzureOpenAiContentGenerationService : IScriptGenerationService
{
    private const string ApiVersion = "2024-10-21";
    private const int MaxGenerationAttempts = 3;

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

        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            try
            {
                var completion = await RequestCompletionAsync(prompt, cancellationToken);
                if (TryParseContent(completion, out var parsed, out var failureReason))
                {
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

                _logger.LogWarning(
                    "Azure OpenAI response failed strict JSON validation on attempt {Attempt}/{MaxAttempts}: {FailureReason}",
                    attempt,
                    MaxGenerationAttempts,
                    failureReason);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Azure OpenAI generation attempt {Attempt}/{MaxAttempts} failed.",
                    attempt,
                    MaxGenerationAttempts);
            }

            if (attempt < MaxGenerationAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        _logger.LogError("Azure OpenAI content generation failed after {MaxAttempts} attempts. Falling back to template content.", MaxGenerationAttempts);
        return BuildFallback(contentType, context, prompt);
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
                    new { role = "system", content = "You are a precise assistant that always returns valid JSON. Do not include markdown code fences." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                response_format = new { type = "json_object" }
            })
        };

        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorPayload = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Azure OpenAI request failed with status {(int)response.StatusCode} ({response.StatusCode}). Body: {errorPayload}",
                null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<AzureChatResponse>(payload);
        return chatResponse?.Choices.FirstOrDefault()?.Message.Content
            ?? throw new InvalidOperationException("Azure OpenAI response did not include message content.");
    }

    private static bool TryParseContent(string rawContent, out GeneratedContent parsed, out string failureReason)
    {
        parsed = new GeneratedContent();
        failureReason = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(rawContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failureReason = "Payload root is not a JSON object.";
                return false;
            }

            var seenProperties = new HashSet<string>(StringComparer.Ordinal);
            string? title = null;
            string? description = null;
            string? scriptBody = null;
            List<string>? tags = null;
            int? estimatedDurationSeconds = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seenProperties.Add(property.Name))
                {
                    failureReason = $"Duplicate property '{property.Name}' is not allowed.";
                    return false;
                }

                switch (property.Name)
                {
                    case "title":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            failureReason = "Property 'title' must be a string.";
                            return false;
                        }

                        title = property.Value.GetString()?.Trim();
                        break;
                    case "description":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            failureReason = "Property 'description' must be a string.";
                            return false;
                        }

                        description = property.Value.GetString()?.Trim();
                        break;
                    case "tags":
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            failureReason = "Property 'tags' must be an array of strings.";
                            return false;
                        }

                        tags = property.Value
                            .EnumerateArray()
                            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null)
                            .Where(tag => !string.IsNullOrWhiteSpace(tag))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (tags.Count == 0)
                        {
                            failureReason = "Property 'tags' must contain at least one non-empty string value.";
                            return false;
                        }

                        break;
                    case "estimatedDurationSeconds":
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var duration) || duration <= 0)
                        {
                            failureReason = "Property 'estimatedDurationSeconds' must be a positive integer.";
                            return false;
                        }

                        estimatedDurationSeconds = duration;
                        break;
                    case "scriptBody":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            failureReason = "Property 'scriptBody' must be a string.";
                            return false;
                        }

                        scriptBody = property.Value.GetString()?.Trim();
                        break;
                    default:
                        failureReason = $"Unexpected property '{property.Name}' detected in JSON payload.";
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(scriptBody) || tags is null || estimatedDurationSeconds is null)
            {
                failureReason = "Payload must include non-empty title, description, scriptBody, tags, and estimatedDurationSeconds.";
                return false;
            }

            parsed = new GeneratedContent
            {
                Title = title,
                Description = description,
                Tags = tags.ToArray(),
                EstimatedDurationSeconds = estimatedDurationSeconds.Value,
                ScriptBody = scriptBody
            };

            return true;
        }
        catch (JsonException ex)
        {
            failureReason = $"Invalid JSON: {ex.Message}";
            return false;
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
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string[] Tags { get; init; } = Array.Empty<string>();
        public int EstimatedDurationSeconds { get; init; }
        public string ScriptBody { get; init; } = string.Empty;
    }
}
