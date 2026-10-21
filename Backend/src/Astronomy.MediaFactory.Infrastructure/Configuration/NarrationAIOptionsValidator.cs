using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Configuration;

internal sealed class NarrationAIOptionsValidator : IValidateOptions<NarrationAIOptions>
{
    public ValidateOptionsResult Validate(string? name, NarrationAIOptions options)
    {
        if (string.Equals(options.Provider, NarrationAIOptions.AzureProvider, StringComparison.OrdinalIgnoreCase))
            return ValidateOptionsResult.Success;
        if (!options.IsOllama)
            return ValidateOptionsResult.Fail("NarrationAI:Provider must be AzureOpenAI or Ollama.");

        var errors = new List<string>();
        var local = options.Ollama;
        if (!Uri.TryCreate(local.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            errors.Add("NarrationAI:Ollama:BaseUrl must be an HTTP(S) URL without credentials, query or fragment.");
        if (string.IsNullOrWhiteSpace(local.Model) || local.Model != local.Model.Trim()
            || local.Model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
            errors.Add("NarrationAI:Ollama:Model must identify an installed local model, not a cloud model.");
        if (local.ExpectedModelDigest is not null && !IsDigest(local.ExpectedModelDigest))
            errors.Add("NarrationAI:Ollama:ExpectedModelDigest must be a SHA256 digest.");
        if (local.TimeoutSeconds is < 1 or > 3600)
            errors.Add("NarrationAI:Ollama:TimeoutSeconds must be between 1 and 3600.");
        if (local.MaxConcurrentRequests is < 1 or > 16)
            errors.Add("NarrationAI:Ollama:MaxConcurrentRequests must be between 1 and 16.");
        if (local.ContextWindow is < 512 or > 131072 || local.MaxOutputTokens <= 0 || local.MaxOutputTokens >= local.ContextWindow)
            errors.Add("NarrationAI:Ollama requires a ContextWindow of 512–131072 and a positive MaxOutputTokens below that window.");
        if (local.MaxRetries is < 0 or > 3 || local.RetryDelayMilliseconds is < 0 or > 5000)
            errors.Add("NarrationAI:Ollama retries must be 0–3 with delay between 0 and 5000 milliseconds.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsDigest(string digest)
    {
        var hex = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : digest;
        return hex.Length == 64 && hex.All(Uri.IsHexDigit);
    }
}
