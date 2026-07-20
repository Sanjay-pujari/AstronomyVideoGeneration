namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;
public sealed record AstronomyCustomTemporalAnchor : AstronomyTemporalAnchor
{
    public AstronomyCustomTemporalAnchor(string code, string? note = null) { Code = TemporalGuards.Token(code, nameof(code), "Custom temporal anchor code"); Note = TemporalGuards.OptionalText(note, TemporalGuards.MaxTextLength, nameof(note), "Custom temporal anchor note"); }
    public override AstronomyTemporalAnchorKind Kind => AstronomyTemporalAnchorKind.Custom;
    public string Code { get; }
    public string? Note { get; }
}
