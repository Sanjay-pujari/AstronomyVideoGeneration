namespace Astronomy.MediaFactory.ContentGen;

/// <summary>The configured, production LLM boundary for final viewer-facing narration.</summary>
public interface INarrationPerformer
{
    string ProviderName { get; }
    string ModelOrDeployment { get; }
    Task<string> PerformAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);

    /// <summary>
    /// The auditable provider boundary. Implementations which own a network adapter should override
    /// this method so an invocation is evidence of the request actually crossing that boundary.
    /// Test performers may rely on the default implementation through explicit DI registration.
    /// </summary>
    async Task<NarrationProviderCallResult> InvokeAsync(NarrationProviderCall request, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var response = await PerformAsync(request.SystemPrompt, request.UserPrompt, cancellationToken);
        return NarrationProviderCallResult.Completed(request, ProviderName, ModelOrDeployment, started, DateTime.UtcNow, response);
    }
}

public sealed record NarrationProviderCall(
    string ProviderCallId,
    string AttemptId,
    string Variant,
    string SystemPrompt,
    string UserPrompt,
    string PromptChecksum);

public sealed record NarrationProviderCallResult(
    string ProviderCallId,
    string AttemptId,
    string Variant,
    string ProviderName,
    string ModelOrDeployment,
    DateTime RequestStartedUtc,
    DateTime RequestCompletedUtc,
    string Response,
    int ResponseCharacterCount,
    string ResponseChecksum)
{
    public static NarrationProviderCallResult Completed(NarrationProviderCall request, string provider, string model,
        DateTime started, DateTime completed, string response) => new(
            request.ProviderCallId, request.AttemptId, request.Variant, provider, model, started, completed, response,
            response?.Length ?? 0, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(response ?? string.Empty))).ToLowerInvariant());
}
