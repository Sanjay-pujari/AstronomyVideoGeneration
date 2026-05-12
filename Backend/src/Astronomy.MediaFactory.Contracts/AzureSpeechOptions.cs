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

    public bool UseSsml { get; set; } = true;
    public string DefaultLanguage { get; set; } = "en";
    public Dictionary<string, string> Voices { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-US-JennyNeural",
        ["hi"] = "hi-IN-SwaraNeural"
    };
    public Dictionary<string, string> ProsodyRate { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "medium",
        ["hi"] = "medium"
    };
    public string? DefaultProsodyRate { get; set; } = "medium";
    public string? HindiProsodyRate { get; set; } = "medium";
    public string? EnglishProsodyRate { get; set; } = "medium";
    public bool AllowAudioTempoCompression { get; set; } = false;
    public double MaxAudioTempo { get; set; } = 1.0d;
    public double MinAudioTempo { get; set; } = 0.95d;

    // Legacy SSML knobs. Rate is ignored when the natural prosody-rate settings above are configured.
    public string? SsmlRate { get; set; }
    public string? SsmlPitch { get; set; } = "+2%";

    public string? PrimaryVoice { get; set; } = "en-US-AriaNeural";
    public string[] FallbackVoices { get; set; } = ["en-US-JennyNeural", "en-US-GuyNeural"];

    // Backwards-compatible settings; used only when new fields are not configured.
    public string? Voice { get; set; }
    public string? FallbackVoice { get; set; }
    public int TimeoutRetryAttempts { get; set; } = 2;
    public int TimeoutRetryDelayMs { get; set; } = 750;

    public IReadOnlyList<string> GetPreferredVoices(string? language = null)
    {
        PrimaryVoice ??= "en-US-AriaNeural";
        FallbackVoices ??= ["en-US-JennyNeural", "en-US-GuyNeural"];

        var voices = new List<string>();
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedLanguage) && (Voices ?? new Dictionary<string, string>()).TryGetValue(normalizedLanguage, out var configuredVoice))
        {
            AddIfSet(voices, configuredVoice);
        }

        if (!string.Equals(normalizedLanguage, "hi", StringComparison.OrdinalIgnoreCase))
        {
            AddIfSet(voices, PrimaryVoice);
        }
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

public sealed class SpeechOptions
{
    public const string SectionName = "Speech";
    public bool UseSsml { get; set; } = true;
    public string DefaultLanguage { get; set; } = "en";
    public Dictionary<string, string> Voices { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-US-JennyNeural",
        ["hi"] = "hi-IN-SwaraNeural"
    };
    public Dictionary<string, string> ProsodyRate { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "medium",
        ["hi"] = "medium"
    };
    public string? DefaultProsodyRate { get; set; } = "medium";
    public string? HindiProsodyRate { get; set; } = "medium";
    public string? EnglishProsodyRate { get; set; } = "medium";
    public bool AllowAudioTempoCompression { get; set; } = false;
    public double MaxAudioTempo { get; set; } = 1.0d;
    public double MinAudioTempo { get; set; } = 0.95d;
}
