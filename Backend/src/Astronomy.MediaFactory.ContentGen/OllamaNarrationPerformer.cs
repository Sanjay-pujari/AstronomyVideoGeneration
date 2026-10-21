using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.ContentGen;

// Shared across factory-created clients and request scopes, not one semaphore per invocation.
public sealed class OllamaNarrationConcurrencyLimiter : IDisposable
{
    internal SemaphoreSlim Semaphore { get; }

    public OllamaNarrationConcurrencyLimiter(IOptions<NarrationAIOptions> options)
    {
        var limit = options.Value.Ollama.MaxConcurrentRequests;
        Semaphore = new SemaphoreSlim(limit, limit);
    }

    public void Dispose() => Semaphore.Dispose();
}

public sealed class OllamaNarrationPerformer(
    HttpClient httpClient,
    IOptions<NarrationAIOptions> options,
    OllamaNarrationConcurrencyLimiter limiter,
    ILogger<OllamaNarrationPerformer> logger) : INarrationPerformer
{
    // Names the existing scene JSON/prompt contract; it does not change RC2 artifacts.
    public const string PromptTemplateVersion = "rc2-scene-narration/1.0";
    private readonly OllamaNarrationOptions _options = Snapshot(options.Value.Ollama);

    public string ProviderName => NarrationAIOptions.OllamaProvider;
    public string ModelOrDeployment => _options.Model;

    public async Task<string> PerformAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userPrompt))).ToLowerInvariant();
        var request = new NarrationProviderCall(Guid.NewGuid().ToString("N"), "direct", "", systemPrompt, userPrompt, checksum);
        return (await InvokeAsync(request, cancellationToken)).Response;
    }

    public async Task<NarrationProviderCallResult> InvokeAsync(NarrationProviderCall request, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var watch = Stopwatch.StartNew();
        var invocationStarted = false;
        var acquired = false;
        string? digest = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputTokens = request.MaxOutputTokens > 0 ? request.MaxOutputTokens : _options.MaxOutputTokens;
            var estimatedInput = request.EstimatedInputTokens > 0 ? request.EstimatedInputTokens
                : ((long)request.SystemPrompt.Length + request.UserPrompt.Length + 3) / 4;
            if (request.SceneCount < 0 || request.MaxOutputTokens < 0 || request.EstimatedInputTokens < 0
                || outputTokens > _options.MaxOutputTokens || estimatedInput + outputTokens > _options.ContextWindow
                || request.ResponseFormat != "json_object"
                || request.Variant.Length > 0 && !IsVariant(request.Variant))
                throw Failure("P7_PROVIDER_REQUEST_REJECTED", "RequestRejected", "Narration request exceeds the local budget or uses an unsupported contract.", false);

            await limiter.Semaphore.WaitAsync(deadline.Token);
            acquired = true;
            digest = await ResolveDigestAsync(deadline.Token);
            logger.LogInformation("RC2_AI_PROVIDER_SELECTED phase={PhaseNumber} provider={Provider} model={Model} digest={ModelDigest} invocationType={InvocationType} correlationId={CorrelationId}",
                7, ProviderName, _options.Model, digest, request.Variant, request.ProviderCallId);
            var body = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt }
                },
                stream = false,
                format = BuildSchema(request),
                options = new { temperature = 0.2, num_ctx = _options.ContextWindow, num_predict = outputTokens }
            };
            using var envelope = await SendAsync(HttpMethod.Post, "api/chat", body, () => invocationStarted = true, deadline.Token);
            var root = envelope.RootElement;
            if (String(root, "model") != _options.Model || !root.TryGetProperty("done", out var done)
                || done.ValueKind != JsonValueKind.True || String(root, "done_reason") == "length"
                || !root.TryGetProperty("message", out var message) || String(message, "role") != "assistant"
                || String(message, "content") is not { } response)
                throw Failure("P7_PROVIDER_OUTPUT_VALIDATION_FAILED", "OutputValidation", "Ollama returned an incomplete or incompatible chat response.", invocationStarted);

            ValidateNarration(response, request);
            // Tags are mutable. Refuse to commit a result if its observed model identity changed.
            if (!string.Equals(digest, await ResolveDigestAsync(deadline.Token), StringComparison.Ordinal))
                throw Failure("P7_PROVIDER_MODEL_IDENTITY_CHANGED", "ModelIdentity", "The installed model digest changed during narration.", invocationStarted);

            var result = NarrationProviderCallResult.Completed(request, ProviderName, _options.Model, started, DateTime.UtcNow, response);
            LogOutcome(request, started, watch.ElapsedMilliseconds, digest, "StructuredContractValid", "None");
            return result;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                LogOutcome(request, started, watch.ElapsedMilliseconds, digest, "NotAccepted", "Cancelled");
                throw;
            }

            var failure = ex switch
            {
                NarrationProviderException known when invocationStarted && !known.InvocationStarted =>
                    new NarrationProviderException(known.ReasonCode, known.Category, known.Message, true,
                        known.Retryable, known.HttpStatus, known.SdkErrorCode, known),
                NarrationProviderException known => known,
                OperationCanceledException => Failure("P7_PROVIDER_TIMEOUT", "Timeout", "The local narration deadline expired.", invocationStarted, true, inner: ex),
                JsonException => Failure("P7_PROVIDER_OUTPUT_VALIDATION_FAILED", "OutputValidation", "Ollama returned invalid JSON.", invocationStarted, inner: ex),
                HttpRequestException => Failure("P7_PROVIDER_UNAVAILABLE", "Transport", "Unable to reach the configured local narration service.", invocationStarted, true, inner: ex),
                _ => Failure("P7_PROVIDER_UNKNOWN_FAILURE", "Unknown", "Local narration failed; no fallback was attempted.", invocationStarted, inner: ex)
            };
            LogOutcome(request, started, watch.ElapsedMilliseconds, digest, "NotAccepted", failure.Category);
            throw failure;
        }
        finally
        {
            if (acquired) limiter.Semaphore.Release();
        }
    }

    private async Task<string> ResolveDigestAsync(CancellationToken token)
    {
        using var tags = await SendAsync(HttpMethod.Get, "api/tags", null, null, token);
        if (tags.RootElement.ValueKind != JsonValueKind.Object
            || !tags.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw Failure("P7_PROVIDER_MODEL_IDENTITY_UNVERIFIED", "ModelIdentity", "Ollama did not return a local model inventory.", false);
        var matches = models.EnumerateArray().Where(m => String(m, "name") == _options.Model || String(m, "model") == _options.Model).ToArray();
        if (matches.Length != 1)
            throw Failure("P7_PROVIDER_MODEL_MISSING", "MissingModel", "The configured model must exactly match one installed Ollama model; no download or fallback was attempted.", false);
        var model = matches[0];
        if (!string.IsNullOrEmpty(String(model, "remote_host")) || !string.IsNullOrEmpty(String(model, "remote_model")))
            throw Failure("P7_PROVIDER_REMOTE_MODEL_FORBIDDEN", "ModelIdentity", "Remote/cloud-backed models are forbidden for this local pilot.", false);
        var rawDigest = String(model, "digest") ?? "";
        var digest = NormalizeDigest(rawDigest);
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw Failure("P7_PROVIDER_MODEL_IDENTITY_UNVERIFIED", "ModelIdentity", "The installed model has no valid SHA256 digest.", false);
        if (_options.ExpectedModelDigest is { } expected && NormalizeDigest(expected) != digest)
            throw Failure("P7_PROVIDER_MODEL_DIGEST_MISMATCH", "ModelIdentity", "The installed model does not match the configured digest pin.", false);
        return digest;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, Action? onSend, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), path));
            if (body is not null) request.Content = JsonContent.Create(body);
            try
            {
                onSend?.Invoke();
                using var response = await httpClient.SendAsync(request, token);
                if (response.IsSuccessStatusCode)
                    return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (IsTransient(response.StatusCode) && attempt < _options.MaxRetries)
                {
                    await DelayAsync(attempt, token);
                    continue;
                }
                throw response.StatusCode switch
                {
                    HttpStatusCode.NotFound => Failure("P7_PROVIDER_MODEL_MISSING", "MissingModel", "The configured Ollama model or endpoint was not found.", path == "api/chat", status: 404),
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Failure("P7_PROVIDER_AUTHENTICATION_FAILED", "Authentication", "Ollama rejected the request credentials.", path == "api/chat", status: (int)response.StatusCode),
                    HttpStatusCode.TooManyRequests => Failure("P7_PROVIDER_RATE_LIMITED", "RateLimited", "Ollama rate limit persisted after bounded retries.", path == "api/chat", true, (int)response.StatusCode),
                    _ when (int)response.StatusCode is >= 400 and < 500 => Failure("P7_PROVIDER_REQUEST_REJECTED", "RequestRejected", "Ollama rejected the narration request or structured-output schema.", path == "api/chat", status: (int)response.StatusCode),
                    _ => Failure("P7_PROVIDER_REQUEST_FAILED", "Transport", "Ollama rejected the request; no fallback was attempted.", path == "api/chat", IsTransient(response.StatusCode), (int)response.StatusCode)
                };
            }
            catch (HttpRequestException) when (attempt < _options.MaxRetries)
            {
                await DelayAsync(attempt, token);
            }
        }
    }

    private Task DelayAsync(int attempt, CancellationToken token)
        => Task.Delay(TimeSpan.FromMilliseconds(_options.RetryDelayMilliseconds * (attempt + 1)), token);

    private static OllamaNarrationOptions Snapshot(OllamaNarrationOptions source) => new()
    {
        BaseUrl = source.BaseUrl, Model = source.Model, ExpectedModelDigest = source.ExpectedModelDigest,
        TimeoutSeconds = source.TimeoutSeconds, MaxConcurrentRequests = source.MaxConcurrentRequests,
        ContextWindow = source.ContextWindow, MaxOutputTokens = source.MaxOutputTokens,
        MaxRetries = source.MaxRetries, RetryDelayMilliseconds = source.RetryDelayMilliseconds
    };

    private static bool IsTransient(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;
    private static string NormalizeDigest(string digest) => (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : digest).ToLowerInvariant();
    private static bool IsVariant(string variant) => variant.Equals("Short", StringComparison.OrdinalIgnoreCase) || variant.Equals("Long", StringComparison.OrdinalIgnoreCase);
    private static string? String(JsonElement element, string key)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static object BuildSchema(NarrationProviderCall request)
    {
        var number = new Dictionary<string, object> { ["type"] = "integer", ["minimum"] = 1 };
        var scenes = new Dictionary<string, object>
        {
            ["type"] = "array", ["minItems"] = Math.Max(1, request.SceneCount),
            ["items"] = new
            {
                type = "object", additionalProperties = false, required = new[] { "sceneNumber", "narrationText" },
                properties = new { sceneNumber = number, narrationText = new { type = "string", minLength = 1 } }
            }
        };
        if (request.SceneCount > 0)
        {
            number["maximum"] = request.SceneCount;
            scenes["maxItems"] = request.SceneCount;
        }
        var variants = request.Variant.Length == 0 ? new[] { "Short", "Long" }
            : new[] { request.Variant.Equals("short", StringComparison.OrdinalIgnoreCase) ? "Short" : "Long" };
        return new
        {
            type = "object", additionalProperties = false, required = new[] { "variant", "scenes" },
            properties = new { variant = new { type = "string", @enum = variants }, scenes }
        };
    }

    private static void ValidateNarration(string response, NarrationProviderCall request)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        var variant = String(root, "variant");
        if (!ExactFields(root, "variant", "scenes") || variant is null || !IsVariant(variant)
            || request.Variant.Length > 0 && !variant.Equals(request.Variant, StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array
            || scenes.GetArrayLength() == 0 || request.SceneCount > 0 && scenes.GetArrayLength() != request.SceneCount)
            throw InvalidOutput();
        var count = scenes.GetArrayLength();
        var seen = new HashSet<int>();
        foreach (var scene in scenes.EnumerateArray())
        {
            if (!ExactFields(scene, "sceneNumber", "narrationText")
                || !scene.TryGetProperty("sceneNumber", out var number) || number.ValueKind != JsonValueKind.Number
                || !number.TryGetInt32(out var id) || id < 1 || id > count || !seen.Add(id)
                || string.IsNullOrWhiteSpace(String(scene, "narrationText")))
                throw InvalidOutput();
        }
    }

    private static bool ExactFields(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var fields = element.EnumerateObject().Select(x => x.Name).ToArray();
        return fields.Length == allowed.Length && fields.Distinct(StringComparer.Ordinal).Count() == allowed.Length
            && fields.All(allowed.Contains);
    }

    private static NarrationProviderException InvalidOutput()
        => Failure("P7_PROVIDER_OUTPUT_VALIDATION_FAILED", "OutputValidation", "Ollama output violates the existing variant/scenes narration contract.", true);

    private static NarrationProviderException Failure(string code, string category, string message, bool started,
        bool retryable = false, int? status = null, Exception? inner = null)
        => new(code, category, message, started, retryable, status, innerException: inner);

    private void LogOutcome(NarrationProviderCall request, DateTime started, long duration, string? digest, string validation, string category)
        => logger.LogInformation(
            "Narration invocation {ProviderCallId} attempt={AttemptId} variant={Variant} provider={Provider} model={Model} digest={ModelDigest} startedUtc={RequestStartedUtc} durationMs={ExecutionDurationMs} promptVersion={PromptTemplateVersion} promptChecksum={PromptChecksum} validation={OutputValidationResult} failureCategory={FailureCategory}",
            request.ProviderCallId, request.AttemptId, request.Variant, ProviderName, _options.Model, digest ?? "Unknown",
            started, duration, PromptTemplateVersion, request.PromptChecksum, validation, category);
}
