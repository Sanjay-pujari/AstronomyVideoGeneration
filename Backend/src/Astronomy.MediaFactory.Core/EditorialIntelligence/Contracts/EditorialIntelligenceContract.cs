using Astronomy.MediaFactory.Core.EditorialIntelligence.Confidence;
using Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Contracts;
public sealed record EditorialIntelligenceContract(string StyleGuideVersion, string SelectedVoice, ObservationGuidance ObservationGuidance, IReadOnlyList<ObservationConfidenceCue> ConfidenceCues, IReadOnlyDictionary<string, IReadOnlyList<string>> SceneConnectors, string ChannelEnding, IReadOnlyList<string> PreferredPhrases, IReadOnlyList<string> ProhibitedPhrases, IReadOnlyList<string> EditorialWarnings);
