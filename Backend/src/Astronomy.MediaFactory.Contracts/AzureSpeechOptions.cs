namespace Astronomy.MediaFactory.Contracts;

public sealed class AzureSpeechOptions
{
    public const string SectionName = "AzureSpeech";
    public string Key { get; set; } = "";
    public string Region { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public bool UseManagedIdentity { get; set; }
    public string? ResourceId { get; set; }
    public string? ManagedIdentityClientId { get; set; }

    public string? PrimaryVoice { get; set; } = "en-US-AriaNeural";
    public string[] FallbackVoices { get; set; } = ["en-US-JennyNeural", "en-US-GuyNeural"];

    // Backwards-compatible settings; used only when new fields are not configured.
    public string? Voice { get; set; }
    public string? FallbackVoice { get; set; }

    public IReadOnlyList<string> GetPreferredVoices()
    {
        PrimaryVoice ??= "en-US-AriaNeural";
        FallbackVoices ??= ["en-US-JennyNeural", "en-US-GuyNeural"];

        var voices = new List<string>();

        AddIfSet(voices, PrimaryVoice);
        foreach (var fallbackVoice in FallbackVoices)
        {
            AddIfSet(voices, fallbackVoice);
        }

        if (voices.Count == 0)
        {
            AddIfSet(voices, Voice);
            AddIfSet(voices, FallbackVoice);
        }

        if (voices.Count == 0)
        {
            voices.Add("en-US-AriaNeural");
            voices.Add("en-US-JennyNeural");
            voices.Add("en-US-GuyNeural");
        }

        return voices;
    }

    private static void AddIfSet(List<string> voices, string? voice)
    {
        if (string.IsNullOrWhiteSpace(voice))
        {
            return;
        }

        var trimmedVoice = voice.Trim();
        if (voices.Any(v => string.Equals(v, trimmedVoice, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        voices.Add(trimmedVoice);
    }
}
