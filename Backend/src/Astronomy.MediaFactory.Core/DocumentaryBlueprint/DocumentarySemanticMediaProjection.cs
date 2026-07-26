using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal sealed record DocumentarySemanticFact(string FactId,IReadOnlyList<string> TopicIds,IReadOnlyList<DocumentaryAstronomyTopicFamily> TopicFamilies,string Category,string Key,string ValueEnglish,string ValueHindi,int Importance,IReadOnlyList<DocumentaryMediaKnowledgeReference> KnowledgeReferences,bool SupportsLong,bool SupportsShort,DocumentaryMediaSceneRole PreferredSceneRole,DocumentaryMediaVisualType PreferredVisualType,IReadOnlyList<string> SubjectIds,IReadOnlyList<string> KnowledgeTags,string CorrelationId);
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
            // A semantic fact is certified only when its complete, explicit schema is present.
            if(TryString(element,"factId",out var id)&&TryString(element,"category",out var category)&&TryString(element,"key",out var key)&&TryString(element,"valueEnglish",out var english)&&TryString(element,"valueHindi",out var hindi)
                &&TryInt(element,"importance",out var importance)&&TryBool(element,"supportsLong",out var supportsLong)&&TryBool(element,"supportsShort",out var supportsShort)
                &&TryString(element,"preferredSceneRole",out _)&&TryString(element,"preferredVisualType",out _)
                &&TryStrings(element,"topicIds",out var topicIds)&&TryFamilies(element,"topicFamilies",out var topicFamilies)&&TryStrings(element,"subjectIds",out var subjects)&&TryStrings(element,"knowledgeTags",out var tags))
            {
                var role=ParseEnum(element,"preferredSceneRole",Role(category));
                var visual=ParseEnum(element,"preferredVisualType",Visual(category));
                var reference=new DocumentaryMediaKnowledgeReference($"{id}.reference.0",payload.PayloadId,payload.PayloadType,payload.SourceItemId,payload.ArtifactIdentity,payload.ArtifactVersion,pointer.Length==0?"/":pointer,0,request.Metadata.CorrelationId);
                facts.Add(new(id,topicIds,topicFamilies,category,key,english,hindi,importance,[reference],supportsLong,supportsShort,role,visual,subjects,tags,request.Metadata.CorrelationId));
            }
            foreach(var property in element.EnumerateObject()) Visit(property.Value,$"{pointer}/{Escape(property.Name)}",payload,request,facts);
        }
        else if(element.ValueKind==JsonValueKind.Array)
        { var i=0;foreach(var item in element.EnumerateArray())Visit(item,$"{pointer}/{i++}",payload,request,facts); }
    }
    private static bool TryString(JsonElement e,string name,out string value){value="";if(!e.TryGetProperty(name,out var p))e.TryGetProperty(char.ToUpperInvariant(name[0])+name[1..],out p);if(p.ValueKind!=JsonValueKind.String)return false;value=p.GetString()??"";return !string.IsNullOrWhiteSpace(value);}
    private static bool TryInt(JsonElement e,string n,out int value){value=0;return e.TryGetProperty(n,out var p)&&p.TryGetInt32(out value);}
    private static bool TryBool(JsonElement e,string n,out bool value){value=false;if(!Property(e,n,out var p)||p.ValueKind is not (JsonValueKind.True or JsonValueKind.False))return false;value=p.GetBoolean();return true;}
    private static T ParseEnum<T>(JsonElement e,string n,T fallback) where T:struct,Enum=>TryString(e,n,out var s)&&Enum.TryParse<T>(s,true,out var x)&&Enum.IsDefined(x)?x:fallback;
    private static bool TryStrings(JsonElement e,string n,out IReadOnlyList<string> values){values=[];if(!Property(e,n,out var a)||a.ValueKind!=JsonValueKind.Array)return false;var result=a.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();if(result.Length!=a.GetArrayLength())return false;values=result;return true;}
    private static bool TryFamilies(JsonElement e,string n,out IReadOnlyList<DocumentaryAstronomyTopicFamily> values){values=[];if(!TryStrings(e,n,out var strings))return false;var result=new List<DocumentaryAstronomyTopicFamily>();foreach(var value in strings){if(!Enum.TryParse<DocumentaryAstronomyTopicFamily>(value,true,out var family)||!Enum.IsDefined(family))return false;result.Add(family);}values=result;return true;}
    private static bool Property(JsonElement e,string n,out JsonElement p){if(e.TryGetProperty(n,out p))return true;return e.TryGetProperty(char.ToUpperInvariant(n[0])+n[1..],out p);}
    private static string Escape(string x)=>x.Replace("~","~0",StringComparison.Ordinal).Replace("/","~1",StringComparison.Ordinal);
    private static DocumentaryMediaSceneRole Role(string c)=>c.ToLowerInvariant() switch{"identity" or "event identity"=>DocumentaryMediaSceneRole.Identity,"location" or "direction"=>DocumentaryMediaSceneRole.Location,"visibility" or "date or time window"=>DocumentaryMediaSceneRole.Visibility,"science" or "scientific explanation"=>DocumentaryMediaSceneRole.Science,"mythology"=>DocumentaryMediaSceneRole.Mythology,"equipment"=>DocumentaryMediaSceneRole.Equipment,"astrophotography"=>DocumentaryMediaSceneRole.Astrophotography,"summary"=>DocumentaryMediaSceneRole.Summary,var x when x.Contains("observation",StringComparison.Ordinal)||x.Contains("viewing",StringComparison.Ordinal)=>DocumentaryMediaSceneRole.Observation,_=>DocumentaryMediaSceneRole.MajorFeature};
    private static DocumentaryMediaVisualType Visual(string c)=>c.ToLowerInvariant() switch{"location" or "direction" or "visibility"=>DocumentaryMediaVisualType.StarChart,"deep-sky objects" or "telescope observation"=>DocumentaryMediaVisualType.TelescopeView,"mythology"=>DocumentaryMediaVisualType.HistoricalIllustration,"date or time window"=>DocumentaryMediaVisualType.Timeline,"angular separation" or "scientific explanation"=>DocumentaryMediaVisualType.OrbitalDiagram,_=>DocumentaryMediaVisualType.ScientificDiagram};
}

internal static class DocumentarySemanticScenePlanner
{
    internal static DocumentarySemanticScenePlan? Create(DocumentaryMediaProjectionRequest request,IReadOnlyList<DocumentarySemanticFact> facts)
    {
        var profile=request.TopicProfile;var objects=profile.PrimaryObjectIds.Concat(profile.SecondaryObjectIds).ToHashSet(StringComparer.Ordinal);var tags=profile.KnowledgeTags.ToHashSet(StringComparer.Ordinal);
        var relevant=facts.Where(f=>f.ValueEnglish.Length>0&&f.ValueHindi.Length>0&&
            (f.TopicIds.Contains(profile.TopicId,StringComparer.Ordinal)||f.SubjectIds.Any(objects.Contains)||(f.TopicFamilies.Contains(profile.TopicFamily)&&f.KnowledgeTags.Any(tags.Contains))))
            .OrderBy(f=>LongOrder(profile.TopicFamily,f.PreferredSceneRole)).ThenByDescending(f=>f.Importance).ThenBy(f=>f.FactId,StringComparer.Ordinal).ToArray();
        var distinct=relevant.GroupBy(f=>f.Category,StringComparer.OrdinalIgnoreCase).SelectMany(g=>g).ToArray();
        if(!Required(profile.TopicFamily,relevant)||relevant.Count(f=>f.SupportsLong)<request.Policy.LongMinimumSceneCount)return null;
        var shortIds=relevant.Where(f=>f.SupportsShort).OrderBy(f=>ShortOrder(profile.TopicFamily,f)).ThenByDescending(f=>f.Importance).ThenBy(f=>f.FactId,StringComparer.Ordinal).Take(request.Policy.ShortMaximumSceneCount).Select(f=>f.FactId).ToHashSet(StringComparer.Ordinal);
        if(shortIds.Count<request.Policy.ShortMinimumSceneCount)return null;
        var scenes=distinct.Select((f,i)=>new DocumentarySemanticScene($"{request.TopicProfile.TopicId}.semantic-scene.{i}",f.PreferredSceneRole,f.Key,f.Key,[f],f.Category,f.Importance,f.SupportsLong,shortIds.Contains(f.FactId),f.KnowledgeReferences,request.Metadata.CorrelationId)).ToArray();
        return new(request.TopicProfile.TopicId,request.TopicProfile.TopicFamily,scenes,request.Metadata.CorrelationId);
    }
    private static bool Required(DocumentaryAstronomyTopicFamily family,IReadOnlyList<DocumentarySemanticFact> facts)
    {bool Category(string value)=>facts.Any(f=>string.Equals(f.Category,value,StringComparison.OrdinalIgnoreCase));bool Role(DocumentaryMediaSceneRole role)=>facts.Any(f=>f.PreferredSceneRole==role);return family switch{DocumentaryAstronomyTopicFamily.Constellation=>Role(DocumentaryMediaSceneRole.Identity)&&Role(DocumentaryMediaSceneRole.Visibility)&&Role(DocumentaryMediaSceneRole.MajorFeature)&&(Role(DocumentaryMediaSceneRole.Observation)||Role(DocumentaryMediaSceneRole.Location)),DocumentaryAstronomyTopicFamily.PlanetConjunction=>Category("Event identity")&&Category("Objects involved")&&Category("Date or time window")&&Category("Angular separation")&&(Role(DocumentaryMediaSceneRole.Visibility)||Category("Direction"))&&Role(DocumentaryMediaSceneRole.Observation),_=>false};}
    private static int LongOrder(DocumentaryAstronomyTopicFamily family,DocumentaryMediaSceneRole r)=>family==DocumentaryAstronomyTopicFamily.PlanetConjunction?r switch{DocumentaryMediaSceneRole.Identity=>0,DocumentaryMediaSceneRole.Context=>1,DocumentaryMediaSceneRole.Visibility=>2,DocumentaryMediaSceneRole.MajorFeature=>3,DocumentaryMediaSceneRole.Location=>4,DocumentaryMediaSceneRole.Science=>5,DocumentaryMediaSceneRole.Observation=>6,DocumentaryMediaSceneRole.Equipment=>7,DocumentaryMediaSceneRole.Summary=>8,_=>9}:r switch{DocumentaryMediaSceneRole.Identity=>0,DocumentaryMediaSceneRole.Location=>1,DocumentaryMediaSceneRole.Visibility=>2,DocumentaryMediaSceneRole.MajorFeature=>3,DocumentaryMediaSceneRole.SupportingFeature=>4,DocumentaryMediaSceneRole.Science=>5,DocumentaryMediaSceneRole.Mythology=>6,DocumentaryMediaSceneRole.Observation=>7,DocumentaryMediaSceneRole.Equipment=>8,DocumentaryMediaSceneRole.Astrophotography=>9,DocumentaryMediaSceneRole.Summary=>10,_=>11};
    private static int ShortOrder(DocumentaryAstronomyTopicFamily family,DocumentarySemanticFact f)=>family==DocumentaryAstronomyTopicFamily.PlanetConjunction?(f.Category.Equals("Angular separation",StringComparison.OrdinalIgnoreCase)?0:f.PreferredSceneRole switch{DocumentaryMediaSceneRole.MajorFeature=>0,DocumentaryMediaSceneRole.Visibility=>1,DocumentaryMediaSceneRole.Observation=>2,DocumentaryMediaSceneRole.Identity=>3,_=>4}):f.PreferredSceneRole switch{DocumentaryMediaSceneRole.MajorFeature=>0,DocumentaryMediaSceneRole.Visibility or DocumentaryMediaSceneRole.Observation=>1,DocumentaryMediaSceneRole.Identity=>2,DocumentaryMediaSceneRole.Summary=>3,_=>4};
}
