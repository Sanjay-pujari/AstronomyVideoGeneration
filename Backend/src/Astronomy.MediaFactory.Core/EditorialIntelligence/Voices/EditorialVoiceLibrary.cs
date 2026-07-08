namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Voices;
public static class EditorialVoiceLibrary
{
    public static EditorialVoiceProfile CalmDocumentary { get; } = new("CalmDocumentary", "short to medium", ["calm", "precise", "elegant"], ["avoid hype", "explain one idea at a time", "prefer observation-first language"]);
    public static EditorialVoiceProfile Get(string? name) => string.Equals(name, CalmDocumentary.Name, StringComparison.OrdinalIgnoreCase) ? CalmDocumentary : CalmDocumentary;
}
