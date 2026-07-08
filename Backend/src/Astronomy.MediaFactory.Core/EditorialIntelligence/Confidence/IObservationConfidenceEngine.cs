using Astronomy.MediaFactory.Core.EditorialIntelligence.Observation;
namespace Astronomy.MediaFactory.Core.EditorialIntelligence.Confidence;
public interface IObservationConfidenceEngine { IReadOnlyList<ObservationConfidenceCue> BuildCues(ObservationMetadata metadata); }
