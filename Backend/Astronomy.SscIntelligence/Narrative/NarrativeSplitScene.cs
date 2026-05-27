using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;
namespace Astronomy.SscIntelligence.Narrative;
public sealed record NarrativeSplitScene(string SceneCode,string SceneTitle,SceneIntentType SceneIntent,NarrativeSceneRole SceneRole,IReadOnlyList<SkyObjectPosition> TargetObjects,IReadOnlyList<string> SourceCluster,string Language,string Region,DateTime SelectedObservationUtc,DateTime SelectedObservationLocal,string SourceSceneCode,string? NarrationSegmentReference);
