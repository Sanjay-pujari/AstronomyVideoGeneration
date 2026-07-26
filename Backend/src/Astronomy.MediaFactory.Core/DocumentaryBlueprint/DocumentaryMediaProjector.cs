namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryMediaProjector
{
    public DocumentaryMediaProjectionResult Project(DocumentaryMediaProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reasons=DocumentaryMediaProjectionValidator.ValidateRequest(request);
        if(reasons.Count>0)return Reject(reasons);
        IReadOnlyList<DocumentarySemanticFact> facts;
        try{facts=DocumentaryMediaKnowledgeExtractor.Extract(request);}catch(System.Text.Json.JsonException){return Reject([DocumentaryMediaProjectionRejectionReason.TopicProfileRejected]);}
        var plan=DocumentarySemanticScenePlanner.Create(request,facts);
        if(plan is null)return Reject([DocumentaryMediaProjectionRejectionReason.TopicProfileRejected]);
        var id=$"{request.MaterializationRecord.MaterializationId}.media-project";
        var variants=new List<DocumentaryMediaVariant>();
        foreach(var type in DocumentaryMediaProjectionInventory.Variants){var variant=CreateVariant(id,type,plan,request);if(variant is null)return Reject([DocumentaryMediaProjectionRejectionReason.SceneInventoryMismatch]);variants.Add(variant);}
        return FinalizeProjection(request,variants);
    }

    internal static DocumentaryMediaProjectionResult FinalizeProjection(DocumentaryMediaProjectionRequest request,IReadOnlyList<DocumentaryMediaVariant> candidateVariants)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(candidateVariants);
        var reasons=DocumentaryMediaProjectionValidator.ValidateRequest(request).Concat(DocumentaryMediaProjectionValidator.ValidateVariants(request,candidateVariants)).Distinct().OrderBy(x=>(int)x).ToArray();
        if(reasons.Length>0)return Reject(reasons);
        var r=request.MaterializationRecord;var id=$"{r.MaterializationId}.media-project";
        var project=new DocumentaryMediaProject(id,r,r.ExportSpecification,r.CertificationRecord,r.ProvenanceRecord,r.ProductionPackage,request.Policy,request.Metadata,request.TopicProfile,candidateVariants,r.MaterializationId,r.ExportSpecificationId,r.CertificationId,r.ProvenanceId,r.PackageId,r.ReleaseCandidateId,r.ConvergenceId,request.TopicProfile.TopicId,4,candidateVariants.Sum(x=>x.SceneCount),candidateVariants.Sum(x=>x.PlannedDurationMilliseconds),true);
        return new(DocumentaryMediaProjectionStatus.Complete,[],project);
    }
    private static DocumentaryMediaProjectionResult Reject(IEnumerable<DocumentaryMediaProjectionRejectionReason> reasons)=>new(DocumentaryMediaProjectionStatus.Rejected,reasons.Distinct().OrderBy(x=>(int)x).ToArray(),null);

    private static DocumentaryMediaVariant? CreateVariant(string projectId,DocumentaryMediaVariantType type,DocumentarySemanticScenePlan plan,DocumentaryMediaProjectionRequest request)
    {
        var (format,language)=DocumentaryMediaProjectionInventory.Mapping(type);var p=request.Policy;
        var selected=plan.Scenes.Where(s=>format==DocumentaryVideoFormat.Long?s.IncludeInLong:s.IncludeInShort).OrderBy(s=>format==DocumentaryVideoFormat.Long?s.SemanticSceneId:$"{999-s.Importance:D3}.{s.SemanticSceneId}",StringComparer.Ordinal).Take(format==DocumentaryVideoFormat.Long?p.LongMaximumSceneCount:p.ShortMaximumSceneCount).ToArray();
        var minimum=format==DocumentaryVideoFormat.Long?p.LongMinimumSceneCount:p.ShortMinimumSceneCount;var maximum=format==DocumentaryVideoFormat.Long?p.LongMaximumSceneCount:p.ShortMaximumSceneCount;if(selected.Length<minimum||selected.Length>maximum)return null;
        var variantId=$"{projectId}.{type}";var raw=selected.Select(s=>Estimate(language,Text(s,language))).ToArray();var target=(format==DocumentaryVideoFormat.Long?p.LongMinimumDurationSeconds:p.ShortMinimumDurationSeconds)*1000L;var extra=Math.Max(0,target-raw.Sum());long start=0;var scenes=new List<DocumentaryMediaScene>();
        for(var i=0;i<selected.Length;i++){var duration=raw[i]+extra/selected.Length+(i<extra%selected.Length?1:0);scenes.Add(CreateScene(variantId,type,format,language,selected[i],i,start,duration,request));start+=duration;}
        var name=language==DocumentaryMediaLanguage.English?request.TopicProfile.DisplayNameEnglish:request.TopicProfile.DisplayNameHindi;
        return new(variantId,type,format,language,name,$"Certified factual projection of {name}",Text(selected[0],language),scenes,scenes.Count,start,format==DocumentaryVideoFormat.Long?"16:9":"9:16",request.Metadata.CorrelationId);
    }
    private static DocumentaryMediaScene CreateScene(string variantId,DocumentaryMediaVariantType type,DocumentaryVideoFormat format,DocumentaryMediaLanguage language,DocumentarySemanticScene semantic,int sequence,long start,long duration,DocumentaryMediaProjectionRequest request)
    {
        var sceneId=$"{variantId}.scene.{sequence}";var refs=semantic.KnowledgeReferences.Select((r,i)=>new DocumentaryMediaKnowledgeReference($"{sceneId}.reference.{i}",r.PayloadId,r.PayloadType,r.SourceItemId,r.ArtifactIdentity,r.ArtifactVersion,r.JsonPointer,i,r.CorrelationId)).ToArray();var text=Shorten(Text(semantic,language),request.Policy.MaximumNarrationCharactersPerScene);var narrationMs=Math.Min(duration,Estimate(language,text));var narration=new DocumentaryNarrationBlock($"{sceneId}.narration.0",language,text,0,narrationMs,refs,request.Metadata.CorrelationId);var subtitles=Subtitles(sceneId,narration,text,narrationMs,request.Policy,refs,request.Metadata.CorrelationId);var fact=semantic.Facts[0];var subjects=fact.SubjectIds.Count>0?fact.SubjectIds:request.TopicProfile.PrimaryObjectIds;var visual=new DocumentaryVisualPrompt($"{sceneId}.visual.0",fact.PreferredVisualType,$"Accurate {semantic.VisualIntent}: {fact.ValueEnglish}",fact.ValueHindi,"",format==DocumentaryVideoFormat.Long?"16:9":"9:16",DocumentaryCameraMotion.SlowZoomIn,subjects,refs,0,request.Metadata.CorrelationId);var timing=new DocumentarySceneTiming($"{sceneId}.timing",start,start+duration,duration,narrationMs,duration-narrationMs,0,request.Metadata.CorrelationId);return new(sceneId,type,semantic.SceneRole,language==DocumentaryMediaLanguage.English?semantic.TitleEnglish:semantic.TitleHindi,sequence,[narration],subtitles,[visual],timing,sequence==0?DocumentarySceneTransition.FadeFromBlack:DocumentarySceneTransition.CrossFade,refs,request.Metadata.CorrelationId);
    }
    private static string Text(DocumentarySemanticScene s,DocumentaryMediaLanguage l)=>string.Join(" ",s.Facts.Select(f=>l==DocumentaryMediaLanguage.English?f.ValueEnglish:f.ValueHindi));
    private static long Estimate(DocumentaryMediaLanguage l,string text){var words=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length;var wpm=l==DocumentaryMediaLanguage.English?145d:125d;return Math.Max(1000,(long)Math.Ceiling(words/wpm*60000));}
    private static string Shorten(string text,int maximum){if(text.Length<=maximum)return text;var cut=text.LastIndexOfAny(['.','!','?'],maximum-1);if(cut<maximum/2)cut=text.LastIndexOf(' ',maximum-1);return text[..Math.Max(1,cut+(cut>=0&&".!?".Contains(text[cut])?1:0))].TrimEnd();}
    private static IReadOnlyList<DocumentarySubtitleCue> Subtitles(string sceneId,DocumentaryNarrationBlock narration,string text,long duration,DocumentaryMediaProjectionPolicy policy,IReadOnlyList<DocumentaryMediaKnowledgeReference> refs,string correlation)
    {var words=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);var lines=new List<string>();var current="";foreach(var word in words){if(word.Length>policy.MaximumSubtitleCharactersPerLine)throw new InvalidOperationException("A narration word exceeds the subtitle line limit.");var next=current.Length==0?word:$"{current} {word}";if(next.Length>policy.MaximumSubtitleCharactersPerLine){lines.Add(current);current=word;}else current=next;}if(current.Length>0)lines.Add(current);var groups=lines.Chunk(policy.MaximumSubtitleLines).ToArray();var result=new List<DocumentarySubtitleCue>();long start=0;for(var i=0;i<groups.Length;i++){var end=i==groups.Length-1?duration:duration*(i+1)/groups.Length;var cueText=string.Join(" ",groups[i]);result.Add(new($"{sceneId}.subtitle.{i}",narration.Language,cueText,groups[i][0],groups[i].Length>1?groups[i][1]:null,DocumentarySubtitlePresentation.Standard,start,end,i,narration.NarrationId,refs,correlation));start=end;}return result;}
}
