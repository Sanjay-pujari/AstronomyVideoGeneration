namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries;

/// <summary>Selects semantic documentary transitions between scene purposes.</summary>
public sealed class DocumentaryTransitionLibrary
{
    private readonly Dictionary<string, string> transitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Science→Observation"] = "Now that we know why it happens, let's step outside and see how to find it.",
        ["Hook→Discovery"] = "So where should you look when the sky begins to darken?",
        ["Observation→Takeaway"] = "And that's what makes this one of the most rewarding sights of the evening.",
        ["Discovery→Science"] = "To understand why it appears this way, we need to shift from watching to wondering."
    };

    /// <summary>Gets all known semantic transition templates.</summary>
    public IReadOnlyDictionary<string, string> All => transitions;

    /// <summary>Selects a transition from one scene purpose to the next.</summary>
    public string Select(string fromPurpose, string toPurpose)
    {
        var key = $"{fromPurpose}→{toPurpose}";
        return transitions.TryGetValue(key, out var transition) ? transition : "Let that idea carry us into the next part of the story.";
    }
}
