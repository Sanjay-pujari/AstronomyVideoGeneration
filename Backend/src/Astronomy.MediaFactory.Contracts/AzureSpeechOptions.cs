namespace Astronomy.MediaFactory.Contracts;

public sealed class AzureSpeechOptions
{
    public const string SectionName = "AzureSpeech";
    public string Key { get; set; } = "";
    public string Region { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public bool UseManagedIdentity { get; set; }
    public string Voice { get; set; } = "en-US-FableMultilingualNeural";
}
