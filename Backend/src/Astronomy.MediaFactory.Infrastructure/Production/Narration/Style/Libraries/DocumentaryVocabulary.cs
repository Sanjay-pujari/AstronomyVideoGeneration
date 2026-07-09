namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries;

/// <summary>Provides approved and forbidden documentary narration vocabulary.</summary>
public sealed class DocumentaryVocabulary
{
    /// <summary>Gets reusable preferred phrases for calm documentary narration.</summary>
    public IReadOnlyList<string> PreferredPhrases { get; } = ["As twilight deepens...", "As darkness settles...", "At first glance...", "From Earth's perspective...", "Take a moment...", "If skies remain clear...", "You'll notice...", "Slowly becoming visible...", "Steady golden glow...", "Brilliant evening star...", "Quietly unfolding..."];

    /// <summary>Gets terms that must never appear in production narration.</summary>
    public IReadOnlyList<string> ForbiddenExpressions { get; } = ["Metadata", "Verified", "JSON", "Prompt", "Planning", "Checklist", "The viewer should", "Scene goal", "Available facts", "Event identity", "RelativePositions", "ViewingWindow"];

    /// <summary>Selects vocabulary suited to a scene purpose.</summary>
    public IReadOnlyList<string> SelectPreferred(string scenePurpose) => scenePurpose.ToLowerInvariant() switch
    {
        "hook" => ["At first glance...", "Take a moment...", "Quietly unfolding..."],
        "observation" => ["As twilight deepens...", "If skies remain clear...", "You'll notice..."],
        "science" => ["From Earth's perspective...", "Slowly becoming visible..."],
        "takeaway" or "closing" => ["As darkness settles...", "Quietly unfolding..."],
        _ => PreferredPhrases.Take(3).ToArray()
    };
}
