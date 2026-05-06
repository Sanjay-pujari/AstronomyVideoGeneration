using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Configuration;

public sealed class ProductionStartupValidator : IValidateOptions<StartupValidationOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<AzureOpenAiOptions> _openAi;
    private readonly IOptions<AzureSpeechOptions> _speech;
    private readonly IOptions<AzureBlobOptions> _blob;
    private readonly IOptions<SkyfieldSidecarOptions> _sidecar;
    private readonly IOptions<YouTubeOptions> _youTube;
    private readonly IOptions<PublishingOptions> _publishing;

    public ProductionStartupValidator(
        IHostEnvironment environment,
        IOptions<AzureOpenAiOptions> openAi,
        IOptions<AzureSpeechOptions> speech,
        IOptions<AzureBlobOptions> blob,
        IOptions<SkyfieldSidecarOptions> sidecar,
        IOptions<YouTubeOptions> youTube,
        IOptions<PublishingOptions> publishing)
    {
        _environment = environment;
        _openAi = openAi;
        _speech = speech;
        _blob = blob;
        _sidecar = sidecar;
        _youTube = youTube;
        _publishing = publishing;
    }

    public ValidateOptionsResult Validate(string? name, StartupValidationOptions options)
    {
        if (_environment.IsDevelopment())
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (options.RequireAzureOpenAi)
            errors.AddRange(AzureConfigurationValidation.ValidateOpenAi(_openAi.Value, requireConfiguration: true));

        if (options.RequireAzureSpeech)
            errors.AddRange(AzureConfigurationValidation.ValidateSpeech(_speech.Value, requireConfiguration: true));

        if (options.RequireBlobStorage)
            errors.AddRange(AzureConfigurationValidation.ValidateBlob(_blob.Value, requireConfiguration: true));

        var sidecar = _sidecar.Value;
        if (options.RequireSkyfieldWhenEnabled && sidecar.Enabled && !Uri.TryCreate(sidecar.BaseUrl, UriKind.Absolute, out _))
            errors.Add("SkyfieldSidecar:BaseUrl must be an absolute URI when SkyfieldSidecar:Enabled=true.");

        var youtube = _youTube.Value;
        var publishing = _publishing.Value;
        var publishingRequiresYouTube = publishing.Enabled && !string.Equals(publishing.Mode, "Disabled", StringComparison.OrdinalIgnoreCase) && !string.Equals(publishing.Mode, "DryRun", StringComparison.OrdinalIgnoreCase);
        if (options.RequireYouTubeWhenPublishingEnabled && (youtube.PublishingEnabled || publishingRequiresYouTube))
        {
            if (string.IsNullOrWhiteSpace(youtube.ClientId) || string.IsNullOrWhiteSpace(youtube.ClientSecret))
                errors.Add("YouTube:ClientId and YouTube:ClientSecret are required when YouTube publishing is enabled.");
            if (string.IsNullOrWhiteSpace(youtube.RefreshToken) && string.IsNullOrWhiteSpace(youtube.TokenFilePath))
                errors.Add("YouTube:RefreshToken or YouTube:TokenFilePath is required when YouTube publishing is enabled.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
