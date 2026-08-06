namespace Astronomy.MediaFactory.ContentGen;

/// <summary>The configured, production LLM boundary for final viewer-facing narration.</summary>
public interface INarrationPerformer
{
    string ProviderName { get; }
    string ModelOrDeployment { get; }
    Task<string> PerformAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
