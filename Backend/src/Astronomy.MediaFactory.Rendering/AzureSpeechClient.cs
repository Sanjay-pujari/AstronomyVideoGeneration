using System.Security;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Azure.Core;
using Azure.Identity;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Rendering;

public interface IAzureSpeechClient
{
    Task<byte[]> SynthesizeMp3Async(
        string text,
        AzureSpeechOptions options,
        CancellationToken cancellationToken);
}

public sealed class AzureSpeechClient(ILogger<AzureSpeechClient> logger) : IAzureSpeechClient
{
    private static readonly TokenRequestContext AzureCognitiveServicesScope = new(["https://cognitiveservices.azure.com/.default"]);

    public async Task<byte[]> SynthesizeMp3Async(
        string text,
        AzureSpeechOptions options,
        CancellationToken cancellationToken)
    {
        var speechConfig = options.UseManagedIdentity
            ? await CreateManagedIdentityConfigAsync(options, cancellationToken)
            : CreateSubscriptionConfig(options);

        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz160KBitRateMonoMp3);

        options.PrimaryVoice ??= "en-US-AriaNeural";

        var voices = new List<string>();
        voices.Add(options.PrimaryVoice);
        voices.AddRange(options.FallbackVoices ?? []);

        Exception? lastUnsupportedVoiceException = null;
        foreach (var voice in voices.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation("Trying voice: {Voice}", voice);

            try
            {
                var audio = await SynthesizeWithVoiceAsync(text, speechConfig, voice, cancellationToken);
                logger.LogInformation("Voice succeeded: {Voice}", voice);
                return audio;
            }
            catch (InvalidOperationException ex) when (IsUnsupportedVoice(ex))
            {
                lastUnsupportedVoiceException = ex;
                logger.LogWarning(ex, "Voice failed: {Voice}, trying fallback", voice);
            }
        }

        throw new InvalidOperationException("All Azure Speech voices failed", lastUnsupportedVoiceException);
    }

    private static async Task<byte[]> SynthesizeWithVoiceAsync(string text, SpeechConfig speechConfig, string voice, CancellationToken cancellationToken)
    {
        speechConfig.SpeechSynthesisVoiceName = voice;

        using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig: null);
        var ssml = BuildSsml(text, voice);
        var result = await SpeakWithSsmlFallbackAsync(synthesizer, ssml, text, cancellationToken);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            return result.AudioData;
        }

        var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(result);
        throw new InvalidOperationException(
            $"Speech synthesis failed. Reason={result.Reason}, ErrorCode={cancellationDetails.ErrorCode}, Details={cancellationDetails.ErrorDetails}");
    }

    private static async Task<SpeechSynthesisResult> SpeakWithSsmlFallbackAsync(
        SpeechSynthesizer synthesizer,
        string ssml,
        string text,
        CancellationToken cancellationToken)
    {
        var ssmlResult = await synthesizer.SpeakSsmlAsync(ssml).WaitAsync(cancellationToken);
        if (ssmlResult.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            return ssmlResult;
        }

        return await synthesizer.SpeakTextAsync(text).WaitAsync(cancellationToken);
    }

    private static bool IsUnsupportedVoice(Exception ex)
    {
        return ex.Message.Contains("Unsupported voice", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("ErrorCode=BadRequest", StringComparison.OrdinalIgnoreCase);
    }

    private static SpeechConfig CreateSubscriptionConfig(AzureSpeechOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key))
            throw new InvalidOperationException("Azure Speech configuration is missing Key.");

        if (!string.IsNullOrWhiteSpace(options.Region)
            && options.Region.Contains('-', StringComparison.Ordinal)
            && options.Region.Length == 5)
        {
            throw new InvalidOperationException(
                $"Azure Speech Region appears to be a locale ('{options.Region}'). Use an Azure region name such as 'eastus'.");
        }

        if (!string.IsNullOrWhiteSpace(options.Region))
            return SpeechConfig.FromSubscription(options.Key, options.Region);

        if (!string.IsNullOrWhiteSpace(options.Endpoint) && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpointUri))
            return SpeechConfig.FromEndpoint(endpointUri, options.Key);

        throw new InvalidOperationException("Azure Speech configuration is missing Region and/or Endpoint.");
    }

    private static async Task<SpeechConfig> CreateManagedIdentityConfigAsync(AzureSpeechOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.ResourceId))
            throw new InvalidOperationException("Azure Speech managed identity requires Region and ResourceId.");

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId.Trim()
        });
        var accessToken = await credential.GetTokenAsync(AzureCognitiveServicesScope, cancellationToken);
        var authorizationToken = $"aad#{options.ResourceId.Trim()}#{accessToken.Token}";
        return SpeechConfig.FromAuthorizationToken(authorizationToken, options.Region);
    }

    private static string BuildSsml(string text, string voice)
    {
        var processedText = ProcessNarrationText(text);
        return $"""
                <speak version="1.0" xml:lang="en-US">
                  <voice name="{voice}">
                    <prosody rate="0.92" pitch="+2%">
                      {processedText}
                    </prosody>
                  </voice>
                </speak>
                """;
    }

    private static string ProcessNarrationText(string text)
    {
        var escapedText = SecurityElement.Escape(text) ?? string.Empty;

        escapedText = Regex.Replace(
            escapedText,
            @"\b(Moon|Jupiter|Saturn|Mars|Venus)\b",
            """<emphasis level="moderate">$1</emphasis>""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        escapedText = Regex.Replace(escapedText, @"(\r\n|\r|\n){2,}", """<break time="1000ms"/>""");
        escapedText = Regex.Replace(escapedText, @",\s*", """, <break time="400ms"/> """);
        escapedText = Regex.Replace(escapedText, @"\.\s*", """. <break time="700ms"/> """);
        escapedText = Regex.Replace(escapedText, @"(\r\n|\r|\n)+", " ");

        return escapedText.Trim();
    }
}
