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

    public ProductionStartupValidator(
        IHostEnvironment environment,
        IOptions<AzureOpenAiOptions> openAi,
        IOptions<AzureSpeechOptions> speech,
        IOptions<AzureBlobOptions> blob,
        IOptions<SkyfieldSidecarOptions> sidecar,
        IOptions<YouTubeOptions> youTube)
    {
        _environment = environment;
        _openAi = openAi;
        _speech = speech;
        _blob = blob;
        _sidecar = sidecar;
        _youTube = youTube;
    }

    public ValidateOptionsResult Validate(string? name, StartupValidationOptions options)
    {
        if (_environment.IsDevelopment())
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        var openAi = _openAi.Value;
        if (options.RequireAzureOpenAi)
        {
            if (!Uri.TryCreate(openAi.Endpoint, UriKind.Absolute, out _))
                errors.Add("AzureOpenAI:Endpoint must be an absolute URI in non-development environments.");
            if (string.IsNullOrWhiteSpace(openAi.ChatDeployment))
                errors.Add("AzureOpenAI:ChatDeployment is required in non-development environments.");
            if (!openAi.UseManagedIdentity && string.IsNullOrWhiteSpace(openAi.ApiKey))
                errors.Add("AzureOpenAI:ApiKey is required unless AzureOpenAI:UseManagedIdentity=true.");
        }

        var speech = _speech.Value;
        if (options.RequireAzureSpeech)
        {
            if (string.IsNullOrWhiteSpace(speech.Region) && string.IsNullOrWhiteSpace(speech.Endpoint))
                errors.Add("AzureSpeech:Region or AzureSpeech:Endpoint must be configured in non-development environments.");
            if (!speech.UseManagedIdentity && string.IsNullOrWhiteSpace(speech.Key))
                errors.Add("AzureSpeech:Key is required unless AzureSpeech:UseManagedIdentity=true.");
        }

        var blob = _blob.Value;
        if (options.RequireBlobStorage)
        {
            var hasConnection = !string.IsNullOrWhiteSpace(blob.ConnectionString);
            var hasManagedIdentityPath = blob.UseManagedIdentity && (!string.IsNullOrWhiteSpace(blob.AccountName) || !string.IsNullOrWhiteSpace(blob.ServiceUri));
            if (!hasConnection && !hasManagedIdentityPath)
                errors.Add("AzureBlob requires either AzureBlob:ConnectionString or managed identity settings (AzureBlob:UseManagedIdentity=true with AzureBlob:AccountName or AzureBlob:ServiceUri).");
            if (string.IsNullOrWhiteSpace(blob.ContainerName))
                errors.Add("AzureBlob:ContainerName is required in non-development environments.");
        }

        var sidecar = _sidecar.Value;
        if (options.RequireSkyfieldWhenEnabled && sidecar.Enabled && !Uri.TryCreate(sidecar.BaseUrl, UriKind.Absolute, out _))
            errors.Add("SkyfieldSidecar:BaseUrl must be an absolute URI when SkyfieldSidecar:Enabled=true.");

        var youtube = _youTube.Value;
        if (options.RequireYouTubeWhenPublishingEnabled && youtube.PublishingEnabled)
        {
            if (string.IsNullOrWhiteSpace(youtube.ClientId) || string.IsNullOrWhiteSpace(youtube.ClientSecret))
                errors.Add("YouTube:ClientId and YouTube:ClientSecret are required when YouTube:PublishingEnabled=true.");
            if (string.IsNullOrWhiteSpace(youtube.RefreshToken) && string.IsNullOrWhiteSpace(youtube.TokenFilePath))
                errors.Add("YouTube:RefreshToken or YouTube:TokenFilePath is required when YouTube:PublishingEnabled=true.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
