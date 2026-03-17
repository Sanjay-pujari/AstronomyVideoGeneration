namespace Astronomy.MediaFactory.Infrastructure.Configuration;

public sealed class StartupValidationOptions
{
    public const string SectionName = "StartupValidation";

    public bool RequireAzureOpenAi { get; set; } = true;
    public bool RequireAzureSpeech { get; set; } = true;
    public bool RequireBlobStorage { get; set; } = true;
    public bool RequireYouTubeWhenPublishingEnabled { get; set; } = true;
    public bool RequireSkyfieldWhenEnabled { get; set; } = true;
}
