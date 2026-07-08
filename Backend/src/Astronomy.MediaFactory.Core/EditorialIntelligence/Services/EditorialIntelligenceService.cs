using System.Text.Json;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Configuration;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Connectors;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Contracts;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Confidence;
using Astronomy.MediaFactory.Core.EditorialIntelligence.StyleGuide;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Voices;
using Microsoft.Extensions.Options;
namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Services;
public sealed class EditorialIntelligenceService(IOptions<EditorialIntelligenceOptions> options, IObservationConsistencyEngine consistencyEngine, IObservationConfidenceEngine confidenceEngine) : IEditorialIntelligenceService
{
    public EditorialIntelligenceContract? CreateContract(string? eventId, string? eventName, string? eventType, JsonElement? eventMetadata)
    {
        var opt = options.Value; if (!opt.Enabled) return null;
        var metadata = ObservationMetadata.From(eventType, eventMetadata);
        var guidance = opt.UseObservationConsistency ? consistencyEngine.BuildGuidance(metadata) : new ObservationGuidance(ObservationConsistencyEngine.MissingMetadataFallback, [], true);
        var cues = opt.UseObservationConfidence ? confidenceEngine.BuildCues(metadata) : [];
        var connectors = opt.UseEditorialConnectors ? EditorialConnectorLibrary.All : new Dictionary<string, IReadOnlyList<string>>();
        var voice = EditorialVoiceLibrary.Get(opt.DefaultVoice);
        var warnings = new List<string>();
        if (guidance.FallbackUsed) warnings.Add("Observation metadata was incomplete; cautious fallback wording is required.");
        return new(opt.StyleGuideVersion, voice.Name, guidance, cues, connectors, opt.UseChannelIdentity ? AstroPulseStyleGuide.ChannelIdentityRules.DefaultEnding : string.Empty, AstroPulseStyleGuide.VocabularyRules.PreferredPhrases, AstroPulseStyleGuide.VocabularyRules.ProhibitedPhrases, warnings);
    }
    public string BuildPromptGuidance(EditorialIntelligenceContract? c) => c is null ? string.Empty : "Observation instructions must come from the EditorialIntelligenceContract. Do not invent viewing direction, timing, brightness, altitude, constellation, or visibility details. EditorialIntelligenceContract: " + JsonSerializer.Serialize(c, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
