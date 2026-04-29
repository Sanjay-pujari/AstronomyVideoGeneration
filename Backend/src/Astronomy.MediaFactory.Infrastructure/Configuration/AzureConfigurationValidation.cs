using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Configuration;

internal static class AzureConfigurationValidation
{
    public static IEnumerable<string> ValidateOpenAi(AzureOpenAiOptions options, bool requireConfiguration)
    {
        var isConfigured = requireConfiguration
                           || options.UseManagedIdentity
                           || HasValue(options.Endpoint)
                           || HasValue(options.ApiKey)
                           || HasValue(options.ChatDeployment)
                           || HasValue(options.ManagedIdentityClientId);

        if (!isConfigured)
            yield break;

        if (!IsAbsoluteUri(options.Endpoint))
            yield return "AzureOpenAI:Endpoint must be an absolute URI when Azure OpenAI is enabled.";

        if (!HasValue(options.ChatDeployment))
            yield return "AzureOpenAI:ChatDeployment is required when Azure OpenAI is enabled.";
        else if (LooksLikeEmbeddingDeployment(options.ChatDeployment))
            yield return "AzureOpenAI:ChatDeployment appears to be an embeddings deployment. Configure a chat-capable deployment (for example, gpt-4.1/gpt-4o) for script generation.";

        if (!options.UseManagedIdentity && !HasValue(options.ApiKey))
            yield return "AzureOpenAI:ApiKey is required unless AzureOpenAI:UseManagedIdentity=true.";
    }

    public static IEnumerable<string> ValidateSpeech(AzureSpeechOptions options, bool requireConfiguration)
    {
        var isConfigured = requireConfiguration
                           || options.UseManagedIdentity
                           || HasValue(options.Key)
                           || HasValue(options.Region)
                           || HasValue(options.Endpoint)
                           || HasValue(options.ResourceId)
                           || HasValue(options.ManagedIdentityClientId);

        if (!isConfigured)
            yield break;

        if (HasValue(options.Endpoint) && !IsAbsoluteUri(options.Endpoint))
            yield return "AzureSpeech:Endpoint must be an absolute URI when provided.";

        if (!HasValue(options.Region) && !HasValue(options.Endpoint))
            yield return "AzureSpeech:Region or AzureSpeech:Endpoint is required when Azure Speech is enabled.";

        if (options.UseManagedIdentity)
        {
            if (!HasValue(options.Region))
                yield return "AzureSpeech:Region is required when AzureSpeech:UseManagedIdentity=true.";

            if (!HasValue(options.ResourceId))
                yield return "AzureSpeech:ResourceId is required when AzureSpeech:UseManagedIdentity=true.";
        }
        else if (!HasValue(options.Key))
        {
            yield return "AzureSpeech:Key is required unless AzureSpeech:UseManagedIdentity=true.";
        }
    }

    public static IEnumerable<string> ValidateBlob(AzureBlobOptions options, bool requireConfiguration)
    {
        var isConfigured = requireConfiguration
                           || options.UseManagedIdentity
                           || HasValue(options.ConnectionString)
                           || HasValue(options.AccountName)
                           || HasValue(options.ServiceUri)
                           || HasValue(options.ManagedIdentityClientId);

        if (!isConfigured)
            yield break;

        if (HasValue(options.ServiceUri) && !IsAbsoluteUri(options.ServiceUri))
            yield return "AzureBlob:ServiceUri must be an absolute URI when provided.";

        if (!HasValue(options.ContainerName))
            yield return "AzureBlob:ContainerName is required when Azure Blob storage is enabled.";

        var hasConnection = HasValue(options.ConnectionString);
        var hasManagedIdentityPath = options.UseManagedIdentity && (HasValue(options.AccountName) || HasValue(options.ServiceUri));
        if (!hasConnection && !hasManagedIdentityPath)
            yield return "AzureBlob requires either AzureBlob:ConnectionString or AzureBlob:UseManagedIdentity=true with AzureBlob:AccountName or AzureBlob:ServiceUri.";
    }

    public static IEnumerable<string> ValidateKeyVault(KeyVaultOptions options)
    {
        if (!HasValue(options.VaultUri))
            yield break;

        if (!IsAbsoluteUri(options.VaultUri))
            yield return "KeyVault:VaultUri must be an absolute URI when provided.";
    }

    private static bool HasValue(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static bool IsAbsoluteUri(string? value)
        => HasValue(value) && Uri.TryCreate(value, UriKind.Absolute, out _);

    private static bool LooksLikeEmbeddingDeployment(string deploymentName)
        => deploymentName.Contains("embedding", StringComparison.OrdinalIgnoreCase);
}
