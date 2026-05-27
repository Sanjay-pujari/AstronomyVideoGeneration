using Astronomy.SscIntelligence.Contracts; using Astronomy.SscIntelligence.NightWindow; using Astronomy.SscIntelligence.Spatial;
namespace Astronomy.SscIntelligence.Narrative;
public interface INarrativeSceneSplitter { NarrativeSceneSplitResult Split(string sceneCode,string sceneTitle,string language,string region,DateTime selectedObservationUtc,DateTime selectedObservationLocal,string? narrationSegmentReference,IReadOnlyList<SkyObjectPosition> resolvedSkyObjects,SpatialCompositionResult spatialComposition,NightWindowResult nightWindow,int maxSplitScenes=3,int maxTotalScenes=4); }
