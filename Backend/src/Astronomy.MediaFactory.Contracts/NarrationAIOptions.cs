namespace Astronomy.MediaFactory.Contracts;

public sealed class NarrationAIOptions
{
    public const string SectionName = "NarrationAI";
    public const string AzureProvider = "AzureOpenAI";
    public const string OllamaProvider = "Ollama";

    public string Provider { get; set; } = AzureProvider;
    public OllamaNarrationOptions Ollama { get; set; } = new();

    public bool IsOllama => string.Equals(Provider, OllamaProvider, StringComparison.OrdinalIgnoreCase);
}

public sealed class OllamaNarrationOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";
    // Deliberately unset: deployment must select and verify an installed local model.
    public string Model { get; set; } = string.Empty;
    public string? ExpectedModelDigest { get; set; }
    public int TimeoutSeconds { get; set; } = 180;
    public int MaxConcurrentRequests { get; set; } = 1;
    public int ContextWindow { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 4096;
    public int MaxRetries { get; set; } = 2;
    public int RetryDelayMilliseconds { get; set; } = 250;
}
