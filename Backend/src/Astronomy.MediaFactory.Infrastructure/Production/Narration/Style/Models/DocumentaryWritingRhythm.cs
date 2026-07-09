namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Models;

/// <summary>Defines the canonical documentary writing rhythm applied to every narration scene.</summary>
public sealed record DocumentaryWritingRhythm(string Observe, string Wonder, string Understand, string Continue)
{
    /// <summary>Gets the default observe-to-continue rhythm used by Project Aurora.</summary>
    public static DocumentaryWritingRhythm Default { get; } = new("Observe", "Wonder", "Understand", "Continue");
}
