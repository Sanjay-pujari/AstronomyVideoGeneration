using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal sealed record DocumentarySemanticFact(string FactId,string Category,string Key,string ValueEnglish,string ValueHindi,int Importance,IReadOnlyList<DocumentaryMediaKnowledgeReference> KnowledgeReferences,bool SupportsLong,bool SupportsShort,DocumentaryMediaSceneRole PreferredSceneRole,DocumentaryMediaVisualType PreferredVisualType,IReadOnlyList<string> SubjectIds,string CorrelationId);
internal sealed record DocumentarySemanticScene(string SemanticSceneId,DocumentaryMediaSceneRole SceneRole,string TitleEnglish,string TitleHindi,IReadOnlyList<DocumentarySemanticFact> Facts,string VisualIntent,int Importance,bool IncludeInLong,bool IncludeInShort,IReadOnlyList<DocumentaryMediaKnowledgeReference> KnowledgeReferences,string CorrelationId);
internal sealed record DocumentarySemanticScenePlan(string TopicId,DocumentaryAstronomyTopicFamily TopicFamily,IReadOnlyList<DocumentarySemanticScene> Scenes,string CorrelationId);

internal static class DocumentaryMediaKnowledgeExtractor
{
    internal static IReadOnlyList<DocumentarySemanticFact> Extract(DocumentaryMediaProjectionRequest request)
    {
        var facts=new List<DocumentarySemanticFact>();
        foreach(var payload in request.MaterializationRecord.Payloads)
        {
            using var document=JsonDocument.Parse(payload.Content);
            Visit(document.RootElement,"",payload,request,facts);
        }
        return facts.GroupBy(x=>x.FactId,StringComparer.Ordinal).Select(x=>x.First()).ToArray();
    }

    // Certified producers may embed semantic fact objects anywhere in canonical JSON.  Unknown
    // shapes are deliberately ignored: projection never turns field names into astronomy facts.
    private static void Visit(JsonElement element,string pointer,DocumentaryExportPayload payload,DocumentaryMediaProjectionRequest request,List<DocumentarySemanticFact> facts)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            if(TryString(element,"category",out var category)&&TryString(element,"valueEnglish",out var english)&&TryString(element,"valueHindi",out var hindi))
            {
                var key=TryString(element,"key",out var k)?k:$"fact-{facts.Count}";
                var id=TryString(element,"factId",out var supplied)?supplied:$"{payload.PayloadId}.fact.{facts.Count}";
                var importance=TryInt(element,"importance",out var rank)?rank:50;
                var role=ParseEnum(element,"preferredSceneRole",Role(category));
                var visual=ParseEnum(element,"preferredVisualType",Visual(category));
                var subjects=Strings(element,"subjectIds");
                var reference=new DocumentaryMediaKnowledgeReference($"{id}.reference.0",payload.PayloadId,payload.PayloadType,payload.SourceItemId,payload.ArtifactIdentity,payload.ArtifactVersion,pointer.Length==0?"/":pointer,0,request.Metadata.CorrelationId);
                facts.Add(new(id,category,key,english,hindi,importance,[reference],Bool(element,"supportsLong",true),Bool(element,"supportsShort",importance>=70),role,visual,subjects,request.Metadata.CorrelationId));
            }
            foreach(var property in element.EnumerateObject()) Visit(property.Value,$"{pointer}/{Escape(property.Name)}",payload,request,facts);
        }
        else if(element.ValueKind==JsonValueKind.Array)
        { var i=0;foreach(var item in element.EnumerateArray())Visit(item,$"{pointer}/{i++}",payload,request,facts); }
    }
    private static bool TryString(JsonElement e,string name,out string value){value="";if(!e.TryGetProperty(name,out var p))e.TryGetProperty(char.ToUpperInvariant(name[0])+name[1..],out p);if(p.ValueKind!=JsonValueKind.String)return false;value=p.GetString()??"";return !string.IsNullOrWhiteSpace(value);}
    private static bool TryInt(JsonElement e,string n,out int value){value=0;return e.TryGetProperty(n,out var p)&&p.TryGetInt32(out value);}
    private static bool Bool(JsonElement e,string n,bool fallback)=>e.TryGetProperty(n,out var p)&&p.ValueKind is JsonValueKind.True or JsonValueKind.False?p.GetBoolean():fallback;
    private static T ParseEnum<T>(JsonElement e,string n,T fallback) where T:struct,Enum=>TryString(e,n,out var s)&&Enum.TryParse<T>(s,true,out var x)&&Enum.IsDefined(x)?x:fallback;
    private static IReadOnlyList<string> Strings(JsonElement e,string n)=>e.TryGetProperty(n,out var a)&&a.ValueKind==JsonValueKind.Array?a.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray():[];
    private static string Escape(string x)=>x.Replace("~","~0",StringComparison.Ordinal).Replace("/","~1",StringComparison.Ordinal);
    private static DocumentaryMediaSceneRole Role(string c)=>c.ToLowerInvariant() switch{"identity" or "event identity"=>DocumentaryMediaSceneRole.Identity,"location" or "direction"=>DocumentaryMediaSceneRole.Location,"visibility" or "date or time window"=>DocumentaryMediaSceneRole.Visibility,"science" or "scientific explanation"=>DocumentaryMediaSceneRole.Science,"mythology"=>DocumentaryMediaSceneRole.Mythology,"equipment"=>DocumentaryMediaSceneRole.Equipment,"astrophotography"=>DocumentaryMediaSceneRole.Astrophotography,"summary"=>DocumentaryMediaSceneRole.Summary,var x when x.Contains("observation",StringComparison.Ordinal)||x.Contains("viewing",StringComparison.Ordinal)=>DocumentaryMediaSceneRole.Observation,_=>DocumentaryMediaSceneRole.MajorFeature};
    private static DocumentaryMediaVisualType Visual(string c)=>c.ToLowerInvariant() switch{"location" or "direction" or "visibility"=>DocumentaryMediaVisualType.StarChart,"deep-sky objects" or "telescope observation"=>DocumentaryMediaVisualType.TelescopeView,"mythology"=>DocumentaryMediaVisualType.HistoricalIllustration,"date or time window"=>DocumentaryMediaVisualType.Timeline,"angular separation" or "scientific explanation"=>DocumentaryMediaVisualType.OrbitalDiagram,_=>DocumentaryMediaVisualType.ScientificDiagram};
}

internal static class DocumentarySemanticScenePlanner
{
    internal static DocumentarySemanticScenePlan? Create(DocumentaryMediaProjectionRequest request,IReadOnlyList<DocumentarySemanticFact> facts)
    {
        var relevant=facts.Where(f=>f.ValueEnglish.Length>0&&f.ValueHindi.Length>0).OrderBy(f=>LongOrder(f.PreferredSceneRole)).ThenByDescending(f=>f.Importance).ThenBy(f=>f.FactId,StringComparer.Ordinal).ToArray();
        var distinct=relevant.GroupBy(f=>f.Category,StringComparer.OrdinalIgnoreCase).SelectMany(g=>g).ToArray();
        if(!relevant.Any(f=>f.PreferredSceneRole==DocumentaryMediaSceneRole.Identity)||relevant.Select(f=>f.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count()<request.Policy.LongMinimumSceneCount)return null;
        var shortIds=relevant.Where(f=>f.SupportsShort).OrderByDescending(f=>f.Importance).ThenBy(f=>ShortOrder(f.PreferredSceneRole)).Take(request.Policy.ShortMaximumSceneCount).Select(f=>f.FactId).ToHashSet(StringComparer.Ordinal);
        if(shortIds.Count<request.Policy.ShortMinimumSceneCount) shortIds.UnionWith(relevant.OrderByDescending(f=>f.Importance).Take(request.Policy.ShortMinimumSceneCount).Select(f=>f.FactId));
        var scenes=distinct.Select((f,i)=>new DocumentarySemanticScene($"{request.TopicProfile.TopicId}.semantic-scene.{i}",f.PreferredSceneRole,f.Key,f.Key,[f],f.Category,f.Importance,f.SupportsLong,shortIds.Contains(f.FactId),f.KnowledgeReferences,request.Metadata.CorrelationId)).ToArray();
        return new(request.TopicProfile.TopicId,request.TopicProfile.TopicFamily,scenes,request.Metadata.CorrelationId);
    }
    private static int LongOrder(DocumentaryMediaSceneRole r)=>r switch{DocumentaryMediaSceneRole.Identity=>0,DocumentaryMediaSceneRole.Location=>1,DocumentaryMediaSceneRole.Visibility=>2,DocumentaryMediaSceneRole.MajorFeature=>3,DocumentaryMediaSceneRole.SupportingFeature=>4,DocumentaryMediaSceneRole.Science=>5,DocumentaryMediaSceneRole.Mythology=>6,DocumentaryMediaSceneRole.Observation=>7,DocumentaryMediaSceneRole.Equipment=>8,DocumentaryMediaSceneRole.Astrophotography=>9,DocumentaryMediaSceneRole.Summary=>10,_=>11};
    private static int ShortOrder(DocumentaryMediaSceneRole r)=>r switch{DocumentaryMediaSceneRole.MajorFeature=>0,DocumentaryMediaSceneRole.Observation=>1,DocumentaryMediaSceneRole.Identity=>2,DocumentaryMediaSceneRole.Summary=>3,_=>4};
}
