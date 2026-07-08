namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Configuration;

public sealed class EditorialIntelligenceOptions
{
    public const string SectionName = "EditorialIntelligence";
    public bool Enabled { get; set; } = true;
    public string StyleGuideVersion { get; set; } = "AstroPulse-v1";
    public string DefaultVoice { get; set; } = "CalmDocumentary";
    public bool UseObservationConsistency { get; set; } = true;
    public bool UseEditorialConnectors { get; set; } = true;
    public bool UseObservationConfidence { get; set; } = true;
    public bool UseChannelIdentity { get; set; } = true;
    public bool FailOnUnsupportedObservationMetadata { get; set; } = false;
}
