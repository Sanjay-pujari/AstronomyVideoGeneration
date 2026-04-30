using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AzureTokenRequestContext = Azure.Core.TokenRequestContext;
using ContractsContentType = Astronomy.MediaFactory.Contracts.ContentType;

namespace Astronomy.MediaFactory.ContentGen;

public sealed class AzureOpenAiContentGenerationService : IScriptGenerationService, IShortsScriptGenerationService, IMetadataOptimizationModelClient
{
    private const string ApiVersion = "2024-10-21";
    private const int MaxGenerationAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly AzureOpenAiOptions _options;
    private static readonly AzureTokenRequestContext AzureCognitiveServicesScope = new(["https://cognitiveservices.azure.com/.default"]);

    private readonly IPromptBuilder _promptBuilder;
    private readonly ILogger<AzureOpenAiContentGenerationService> _logger;
    private readonly TokenCredential? _credential;

    public AzureOpenAiContentGenerationService(
        HttpClient httpClient,
        IOptions<AzureOpenAiOptions> options,
        IPromptBuilder promptBuilder,
        ILogger<AzureOpenAiContentGenerationService> logger,
        TokenCredential? credential = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _promptBuilder = promptBuilder;
        _logger = logger;
        _credential = _options.UseManagedIdentity
            ? credential ?? new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = string.IsNullOrWhiteSpace(_options.ManagedIdentityClientId) ? null : _options.ManagedIdentityClientId.Trim()
            })
            : credential;
    }

    public async Task<ScriptResult> GenerateAsync(ContractsContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
    {
        var prompt = _promptBuilder.Build(contentType, context, context.PromptFeedbackContext);

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
                if (IsUnsupportedOperation(ex))
                {
                    _logger.LogError(
                        ex,
                        "Azure OpenAI request used unsupported operation for deployment '{Deployment}'. Check AzureOpenAI:ChatDeployment and ensure it targets a chat-capable model deployment (not embeddings).",
                        _options.ChatDeployment);
                    break;
                }

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



    public async Task<ShortScriptResult> GenerateShortAsync(ContractsContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
    {
        var prompt = BuildShortPrompt(contentType, context, context.PromptFeedbackContext);

        for (var attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
        {
            try
            {
                var completion = await RequestCompletionAsync(prompt, cancellationToken);
                if (TryParseShortContent(completion, out var parsed, out var failureReason))
                {
                    return parsed;
                }

                _logger.LogWarning(
                    "Azure OpenAI shorts response failed strict JSON validation on attempt {Attempt}/{MaxAttempts}: {FailureReason}",
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
                if (IsUnsupportedOperation(ex))
                {
                    _logger.LogError(
                        ex,
                        "Azure OpenAI shorts request used unsupported operation for deployment '{Deployment}'. Check AzureOpenAI:ChatDeployment and ensure it targets a chat-capable model deployment (not embeddings).",
                        _options.ChatDeployment);
                    break;
                }

                _logger.LogWarning(ex, "Azure OpenAI shorts generation attempt {Attempt}/{MaxAttempts} failed.", attempt, MaxGenerationAttempts);
            }

            if (attempt < MaxGenerationAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        _logger.LogError("Azure OpenAI shorts generation failed after {MaxAttempts} attempts. Falling back to template content.", MaxGenerationAttempts);
        return BuildShortFallback(contentType, context);
    }

    private async Task<string> RequestCompletionAsync(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ChatDeployment))
        {
            throw new InvalidOperationException("Azure OpenAI configuration is missing Endpoint and/or ChatDeployment.");
        }

        if (!_options.UseManagedIdentity && string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Azure OpenAI configuration is missing ApiKey while managed identity is disabled.");
        }

        var endpoint = _options.Endpoint.TrimEnd('/');
        if (LooksLikeEmbeddingDeployment(_options.ChatDeployment))
        {
            throw new InvalidOperationException(
                $"AzureOpenAI:ChatDeployment is configured as '{_options.ChatDeployment}', which appears to be an embeddings deployment. Configure a chat-capable deployment for /chat/completions.");
        }

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
        return ExtractAssistantContent(payload);
    }

    private static string ExtractAssistantContent(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Azure OpenAI response payload was empty.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("choices", out var choicesElement) || choicesElement.ValueKind != JsonValueKind.Array || choicesElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Azure OpenAI response did not include any choices.");
        }

        var firstChoice = choicesElement[0];
        if (!firstChoice.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Azure OpenAI response did not include a message object.");
        }

        if (messageElement.TryGetProperty("content", out var contentElement))
        {
            if (contentElement.ValueKind == JsonValueKind.String)
            {
                var directContent = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(directContent))
                {
                    return directContent;
                }
            }
            else if (contentElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in contentElement.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!part.TryGetProperty("type", out var typeElement)
                        || !string.Equals(typeElement.GetString(), "text", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (part.TryGetProperty("text", out var textElement))
                    {
                        var partText = textElement.GetString();
                        if (!string.IsNullOrWhiteSpace(partText))
                        {
                            return partText;
                        }
                    }
                }
            }
        }

        if (messageElement.TryGetProperty("refusal", out var refusalElement) && refusalElement.ValueKind == JsonValueKind.String)
        {
            throw new InvalidOperationException($"Azure OpenAI response was refused: {refusalElement.GetString()}");
        }

        throw new InvalidOperationException("Azure OpenAI response did not include message content.");
    }

    private static bool IsUnsupportedOperation(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.BadRequest } httpEx
           && httpEx.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeEmbeddingDeployment(string deploymentName)
        => deploymentName.Contains("embedding", StringComparison.OrdinalIgnoreCase);

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

    private static ScriptResult BuildFallback(ContractsContentType contentType, AstronomyContext context, string prompt)
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



    private static string BuildShortPrompt(ContractsContentType contentType, AstronomyContext context, PromptFeedbackContext? feedbackContext)
    {
        var topEvents = context.Events
            .OrderByDescending(e => e.Score)
            .Take(3)
            .Select(e => $"{e.ObjectName}: {e.VisibilityWindow}, {e.Direction}, {e.Details}")
            .ToArray();

        return "You are creating a YouTube Shorts script for astronomy audiences.\n" +
               PromptFeedbackComposer.BuildBoundaryRulesSection() + "\n" +
               "Return ONLY valid JSON. No markdown, no code fences.\n" +
               "The short must be 30-60 seconds, include a strong hook in first 3 seconds, and punchy narration with simple structure.\n\n" +
               "Output format:\n" +
               "{\n" +
               "  \"hook\": \"string\",\n" +
               "  \"shortScript\": \"string\",\n" +
               "  \"title\": \"string\",\n" +
               "  \"tags\": [\"shorts\", \"astronomy\"],\n" +
               "  \"sceneNarrationSegments\": [\n" +
               "    {\n" +
               "      \"sceneId\": \"sky-overview\",\n" +
               "      \"sceneTitle\": \"Sky overview\",\n" +
               "      \"visualTarget\": \"whole sky\",\n" +
               "      \"narrationText\": \"string\"\n" +
               "    }\n" +
               "  ]\n" +
               "}\n\n" +
               "Return exactly one sceneNarrationSegments item per visual scene in this order:\n" +
               "1) sky-overview, 2) moon, 3) planet-primary, 4) planet-secondary, 5) constellation.\n" +
               "Narration must semantically match each scene visual target.\n\n" +
               $"Context:\n- date: {context.Date:yyyy-MM-dd}\n- location: {context.LocationName}\n- contentType: {contentType}\n- topEvents: {string.Join(" | ", topEvents)}\n" +
               PromptFeedbackComposer.BuildFeedbackSection(feedbackContext, isShortForm: true);
    }

    private static bool TryParseShortContent(string rawContent, out ShortScriptResult parsed, out string failureReason)
    {
        parsed = new ShortScriptResult();
        failureReason = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(rawContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failureReason = "Payload root is not a JSON object.";
                return false;
            }

            string? hook = null;
            string? shortScript = null;
            string? title = null;
            List<string>? tags = null;
            var sceneNarrationSegments = new List<SceneNarrationSegment>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "hook": hook = property.Value.GetString()?.Trim(); break;
                    case "shortScript": shortScript = property.Value.GetString()?.Trim(); break;
                    case "title": title = property.Value.GetString()?.Trim(); break;
                    case "tags":
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            failureReason = "Property 'tags' must be an array of strings.";
                            return false;
                        }
                        tags = property.Value.EnumerateArray().Select(x => x.GetString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "sceneNarrationSegments":
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            failureReason = "Property 'sceneNarrationSegments' must be an array.";
                            return false;
                        }

                        foreach (var sceneSegment in property.Value.EnumerateArray())
                        {
                            if (sceneSegment.ValueKind != JsonValueKind.Object)
                            {
                                failureReason = "Each scene narration segment must be an object.";
                                return false;
                            }

                            var sceneId = sceneSegment.TryGetProperty("sceneId", out var sceneIdNode) ? sceneIdNode.GetString()?.Trim() : null;
                            var sceneTitle = sceneSegment.TryGetProperty("sceneTitle", out var sceneTitleNode) ? sceneTitleNode.GetString()?.Trim() : null;
                            var visualTarget = sceneSegment.TryGetProperty("visualTarget", out var visualTargetNode) ? visualTargetNode.GetString()?.Trim() : null;
                            var narrationText = sceneSegment.TryGetProperty("narrationText", out var narrationNode) ? narrationNode.GetString()?.Trim() : null;
                            if (string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(sceneTitle) || string.IsNullOrWhiteSpace(visualTarget) || string.IsNullOrWhiteSpace(narrationText))
                            {
                                failureReason = "Each scene narration segment must include non-empty sceneId, sceneTitle, visualTarget, and narrationText.";
                                return false;
                            }

                            sceneNarrationSegments.Add(new SceneNarrationSegment
                            {
                                SceneId = sceneId,
                                SceneTitle = sceneTitle,
                                VisualTarget = visualTarget,
                                NarrationText = narrationText
                            });
                        }
                        break;
                    default:
                        failureReason = $"Unexpected property '{property.Name}' detected in JSON payload.";
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(hook) || string.IsNullOrWhiteSpace(shortScript) || string.IsNullOrWhiteSpace(title))
            {
                failureReason = "Payload must include non-empty hook, shortScript, and title.";
                return false;
            }

            tags ??= ["shorts", "astronomy"];
            if (!tags.Any(static t => t.Equals("shorts", StringComparison.OrdinalIgnoreCase)))
            {
                tags.Insert(0, "shorts");
            }

            var estimatedDuration = Math.Clamp((int)Math.Ceiling(shortScript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 2.6), 30, 60);
            parsed = new ShortScriptResult
            {
                Hook = hook,
                ShortScript = shortScript,
                Title = title,
                Tags = tags.ToArray(),
                EstimatedDurationSeconds = estimatedDuration,
                SceneNarrationSegments = sceneNarrationSegments
            };

            return true;
        }
        catch (JsonException ex)
        {
            failureReason = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static ShortScriptResult BuildShortFallback(ContractsContentType contentType, AstronomyContext context)
    {
        var topEvent = context.Events.OrderByDescending(x => x.Score).FirstOrDefault();
        var hook = topEvent is null
            ? "Stop scrolling — tonight's sky has a quick surprise for you."
            : $"Stop scrolling — {topEvent.ObjectName} is putting on a show tonight.";

        var body = topEvent is null
            ? "Step outside after sunset, let your eyes adapt for five minutes, and scan the brightest region overhead."
            : $"In the next minute: look {topEvent.Direction} {topEvent.VisibilityWindow}. You can spot {topEvent.ObjectName} with {topEvent.ObservationTool}. {topEvent.Details}";

        return new ShortScriptResult
        {
            Hook = hook,
            ShortScript = body,
            Title = $"{contentType} in 60 Seconds",
            Tags = ["shorts", "astronomy", contentType.ToString()],
            EstimatedDurationSeconds = 45
        };
    }


    public async Task<OptimizedVideoMetadata?> TryOptimizeAsync(MetadataOptimizationInput input, bool isShort, CancellationToken cancellationToken)
    {
        var prompt = BuildMetadataPrompt(input, isShort);
        try
        {
            var completion = await RequestCompletionAsync(prompt, cancellationToken);
            return TryParseOptimizedMetadata(completion, isShort, out var metadata, out _) ? metadata : null;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildMetadataPrompt(MetadataOptimizationInput input, bool isShort)
    {
        return "You optimize YouTube astronomy metadata. Return ONLY strict JSON with these exact fields: " +
               "primaryTitle(string), alternateTitles(array), optimizedDescription(string), tags(array), hashtags(array), thumbnailTextSuggestions(array), hookLine(string|null). " +
               "No additional fields. Keep titles trustworthy and readable; no spammy clickbait. " +
               $"shortForm={isShort}. contentType={input.ContentType}. date={input.Context.Date:yyyy-MM-dd}. location={input.Context.LocationName}. " +
               $"sourceTitle={input.SourceTitle}. sourceDescription={input.SourceDescription}. sourceTags={string.Join(',', input.SourceTags)}. " +
               $"sourceHook={input.SourceHookLine}. " +
               PromptFeedbackComposer.BuildFeedbackSection(input.FeedbackContext, isShortForm: isShort);
    }

    private static bool TryParseOptimizedMetadata(string rawContent, bool isShort, out OptimizedVideoMetadata metadata, out string failureReason)
    {
        metadata = new OptimizedVideoMetadata();
        failureReason = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(rawContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failureReason = "Payload root is not a JSON object.";
                return false;
            }

            string? primaryTitle = null;
            string? optimizedDescription = null;
            string? hookLine = null;
            var alternateTitles = new List<string>();
            var tags = new List<string>();
            var hashtags = new List<string>();
            var thumbnailTexts = new List<string>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "primaryTitle": primaryTitle = property.Value.GetString()?.Trim(); break;
                    case "optimizedDescription": optimizedDescription = property.Value.GetString()?.Trim(); break;
                    case "hookLine": hookLine = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString()?.Trim(); break;
                    case "alternateTitles":
                        if (property.Value.ValueKind != JsonValueKind.Array) { failureReason = "alternateTitles must be array"; return false; }
                        alternateTitles.AddRange(property.Value.EnumerateArray().Select(x => x.GetString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>());
                        break;
                    case "tags":
                        if (property.Value.ValueKind != JsonValueKind.Array) { failureReason = "tags must be array"; return false; }
                        tags.AddRange(property.Value.EnumerateArray().Select(x => x.GetString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>());
                        break;
                    case "hashtags":
                        if (property.Value.ValueKind != JsonValueKind.Array) { failureReason = "hashtags must be array"; return false; }
                        hashtags.AddRange(property.Value.EnumerateArray().Select(x => x.GetString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>());
                        break;
                    case "thumbnailTextSuggestions":
                        if (property.Value.ValueKind != JsonValueKind.Array) { failureReason = "thumbnailTextSuggestions must be array"; return false; }
                        thumbnailTexts.AddRange(property.Value.EnumerateArray().Select(x => x.GetString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>());
                        break;
                    default:
                        failureReason = $"Unexpected property '{property.Name}' detected in metadata JSON payload.";
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(primaryTitle) || string.IsNullOrWhiteSpace(optimizedDescription))
            {
                failureReason = "Metadata payload must include non-empty primaryTitle and optimizedDescription.";
                return false;
            }

            metadata = new OptimizedVideoMetadata
            {
                PrimaryTitle = primaryTitle,
                AlternateTitles = alternateTitles.ToArray(),
                OptimizedDescription = optimizedDescription,
                Tags = tags.ToArray(),
                Hashtags = hashtags.ToArray(),
                ThumbnailTextSuggestions = thumbnailTexts.ToArray(),
                HookLine = isShort ? hookLine : null
            };
            return true;
        }
        catch (JsonException ex)
        {
            failureReason = $"Invalid JSON: {ex.Message}";
            return false;
        }
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
