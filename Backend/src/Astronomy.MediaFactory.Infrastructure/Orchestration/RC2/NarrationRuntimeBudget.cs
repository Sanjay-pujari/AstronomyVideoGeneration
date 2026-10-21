using Astronomy.MediaFactory.ContentGen;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

public static class NarrationRuntimeBudget
{
    // Speech words, language-dependent token estimate, JSON overhead and safety margin.
    // This is deliberately an estimate; the provider separately bounds input + output context.
    public static int EstimateOutputTokens(IReadOnlyList<NarrationContextBeat> beats, string language, OllamaNarrationOptions options)
    {
        var hindi = language.Equals("hi", StringComparison.OrdinalIgnoreCase);
        var words = beats.Sum(b => Math.Max(40d, Math.Max(0, b.TargetDurationSeconds) * (hindi ? 125 : 140) / 60d));
        var estimate = checked((int)Math.Ceiling(words * (hindi ? 4 : 2) * 1.2 + beats.Count * 48 + 32));
        if (estimate > options.MaxOutputTokens)
            throw new NarrationProviderException("P7_PROVIDER_OUTPUT_BUDGET_INSUFFICIENT", "RequestRejected",
                $"Estimated narration output requires {estimate} tokens; configured local maximum is {options.MaxOutputTokens}. Increase reviewed limits or use a compatible scene-batching follow-up; output will not be silently truncated.", false);
        return estimate;
    }
}
