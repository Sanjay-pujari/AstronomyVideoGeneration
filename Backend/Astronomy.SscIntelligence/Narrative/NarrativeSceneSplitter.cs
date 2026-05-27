using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Spatial;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;
namespace Astronomy.SscIntelligence.Narrative;
public sealed class NarrativeSceneSplitter : INarrativeSceneSplitter
{
 public NarrativeSceneSplitResult Split(string sceneCode,string sceneTitle,string language,string region,DateTime selectedObservationUtc,DateTime selectedObservationLocal,string? narrationSegmentReference,IReadOnlyList<SkyObjectPosition> resolvedSkyObjects,SpatialCompositionResult spatialComposition,NightWindowResult nightWindow,int maxSplitScenes=3,int maxTotalScenes=4){
 if(spatialComposition.CompositionClass!=SpatialCompositionClass.ImpossibleGrouping) return new(false,"composition-not-impossible",[Build(sceneCode,sceneTitle,SceneIntentType.Grouping,NarrativeSceneRole.Original,resolvedSkyObjects,["original"],language,region,selectedObservationUtc,selectedObservationLocal,sceneCode,narrationSegmentReference)]);
 var scenes=new List<NarrativeSplitScene>(); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 foreach(var cluster in spatialComposition.Clusters.OrderByDescending(c=>c.Objects.Count)){
 if(scenes.Count>=maxSplitScenes||scenes.Count>=maxTotalScenes) break; var key=string.Join('|',cluster.ObjectNames.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)); if(!seen.Add(key)) continue;
 scenes.Add(BuildCluster(cluster,spatialComposition.DominantCluster,sceneCode,sceneTitle,language,region,selectedObservationUtc,selectedObservationLocal,narrationSegmentReference)); }
 if(scenes.Count==0) return new(false,"no-unique-clusters",[Build(sceneCode,sceneTitle,SceneIntentType.Grouping,NarrativeSceneRole.Original,resolvedSkyObjects,["original"],language,region,selectedObservationUtc,selectedObservationLocal,sceneCode,narrationSegmentReference)]);
 return new(true,"impossible-grouping-split",scenes.Take(maxTotalScenes).ToList()); }

 static NarrativeSplitScene BuildCluster(SpatialObjectCluster c,SpatialObjectCluster dominant,string sourceCode,string title,string language,string region,DateTime utc,DateTime local,string? narration){
 var planets=c.ObjectNames.Where(IsPlanet).ToList(); var isDominant=c.ObjectNames.SequenceEqual(dominant.ObjectNames);
 if(planets.Count>=2&&IsWestern(c)) return Build("western_planet_grouping_scene",title,SceneIntentType.Grouping,NarrativeSceneRole.DominantCluster,c.Objects,c.ObjectNames,language,region,utc,local,sourceCode,narration);
 if(c.Objects.Count==1&&c.ObjectNames.Contains("Moon",StringComparer.OrdinalIgnoreCase)) return Build("moon_hero_scene",title,SceneIntentType.HeroShot,NarrativeSceneRole.HeroObject,c.Objects,c.ObjectNames,language,region,utc,local,sourceCode,narration);
 if(c.Objects.Count==1&&planets.Count==1) return Build($"{planets[0].ToLowerInvariant()}_hero_scene",title,SceneIntentType.HeroShot,NarrativeSceneRole.HeroObject,c.Objects,c.ObjectNames,language,region,utc,local,sourceCode,narration);
 return Build($"{sourceCode}_wide_context_scene",title,SceneIntentType.WideNight,isDominant?NarrativeSceneRole.DominantCluster:NarrativeSceneRole.DeferredCluster,c.Objects,c.ObjectNames,language,region,utc,local,sourceCode,narration); }
 static NarrativeSplitScene Build(string code,string title,SceneIntentType intent,NarrativeSceneRole role,IReadOnlyList<SkyObjectPosition> objs,IReadOnlyList<string> cluster,string lang,string region,DateTime utc,DateTime local,string src,string? narr)=>new(code,title,intent,role,objs,cluster,lang,region,utc,local,src,narr);
 static bool IsWestern(SpatialObjectCluster c){var a=c.Objects.Average(x=>x.AzimuthDeg); return a>=225&&a<=315;}
 static bool IsPlanet(string n)=>new[]{"Mercury","Venus","Mars","Jupiter","Saturn","Uranus","Neptune"}.Contains(n,StringComparer.OrdinalIgnoreCase);
}
