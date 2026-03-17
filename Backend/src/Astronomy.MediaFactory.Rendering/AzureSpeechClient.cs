using System.Security;
using Microsoft.CognitiveServices.Speech;

namespace Astronomy.MediaFactory.Rendering;

public interface IAzureSpeechClient
{
    Task<byte[]> SynthesizeMp3Async(
        string text,
        string key,
        string region,
        string voice,
        CancellationToken cancellationToken);
}

public sealed class AzureSpeechClient : IAzureSpeechClient
{
    public async Task<byte[]> SynthesizeMp3Async(
        string text,
        string key,
        string region,
        string voice,
        CancellationToken cancellationToken)
    {
        var speechConfig = SpeechConfig.FromSubscription(key, region);
        speechConfig.SpeechSynthesisVoiceName = voice;
        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz160KBitRateMonoMp3);

        using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig: null);
        var ssml = BuildSsml(text, voice);
        var result = await synthesizer.SpeakSsmlAsync(ssml).WaitAsync(cancellationToken);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            return result.AudioData;
        }

        var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(result);
        throw new InvalidOperationException(
            $"Speech synthesis failed. Reason={result.Reason}, ErrorCode={cancellationDetails.ErrorCode}, Details={cancellationDetails.ErrorDetails}");
    }

    private static string BuildSsml(string text, string voice)
    {
        var escapedText = SecurityElement.Escape(text) ?? string.Empty;
        return $"""
                <speak version='1.0' xml:lang='en-US'>
                  <voice name='{voice}'>{escapedText}</voice>
                </speak>
                """;
    }
}
