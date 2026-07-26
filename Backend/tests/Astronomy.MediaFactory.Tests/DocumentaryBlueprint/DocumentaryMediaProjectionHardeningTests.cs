using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryMediaProjectionHardeningTests
{
    [Fact]
    public void Orion_projects_four_topic_filtered_canonical_variants()
    {
        var project=Project(DocumentaryMediaProjectionFixture.Orion());var text=English(project);
        Assert.Equal(Enum.GetValues<DocumentaryMediaVariantType>(),project.Variants.Select(x=>x.VariantType));
        Assert.Contains("Orion",text);Assert.DoesNotContain("Leo",text);Assert.DoesNotContain("Regulus",text);
        Assert.NotEqual(project.Variants[0].Scenes.Select(x=>x.Title),project.Variants[2].Scenes.Select(x=>x.Title));
        Assert.Equal(project.Variants[0].Scenes.SelectMany(x=>x.KnowledgeReferences).Select(Key),project.Variants[1].Scenes.SelectMany(x=>x.KnowledgeReferences).Select(Key));
        Assert.All(project.Variants.Where(x=>x.Format==DocumentaryVideoFormat.Long),x=>Assert.Equal("16:9",x.AspectRatio));
        Assert.All(project.Variants.Where(x=>x.Format==DocumentaryVideoFormat.Short),x=>Assert.Equal("9:16",x.AspectRatio));
    }

    [Fact]
    public void Leo_projection_includes_certified_features_and_excludes_Orion()
    {var text=English(Project(DocumentaryMediaProjectionFixture.Leo()));foreach(var expected in new[]{"Leo","Regulus","Sickle","Leo Triplet","spring"})Assert.Contains(expected,text,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Orion",text);}

    [Fact]
    public void Planet_conjunction_projection_contains_profile_requirements()
    {var text=English(Project(DocumentaryMediaProjectionFixture.Conjunction()));foreach(var expected in new[]{"Mars","Jupiter","August","degrees","eastern","binoculars"})Assert.Contains(expected,text,StringComparison.OrdinalIgnoreCase);}

    [Fact]
    public void Subtitle_cues_reconstruct_every_complete_narration()
    {var project=Project(DocumentaryMediaProjectionFixture.Orion());foreach(var variant in project.Variants)foreach(var scene in variant.Scenes){var narration=Assert.Single(scene.Narration);Assert.Equal(Enumerable.Range(0,scene.SubtitleCues.Count),scene.SubtitleCues.Select(x=>x.Sequence));Assert.All(scene.SubtitleCues,c=>{Assert.True(c.Line1.Length<=34);Assert.True(c.Line2 is null||c.Line2.Length<=34);});Assert.Equal(Normalize(narration.Text),Normalize(string.Join(" ",scene.SubtitleCues.Select(x=>x.Text))));Assert.Equal(0,scene.SubtitleCues[0].StartOffsetMilliseconds);Assert.Equal(narration.EstimatedDurationMilliseconds,scene.SubtitleCues[^1].EndOffsetMilliseconds);Assert.All(scene.SubtitleCues.Zip(scene.SubtitleCues.Skip(1)),pair=>Assert.True(pair.First.EndOffsetMilliseconds<=pair.Second.StartOffsetMilliseconds));}}

    [Fact]
    public void Shared_payload_topic_filtering_is_bidirectional()
    {var orion=English(Project(DocumentaryMediaProjectionFixture.Orion()));var leo=English(Project(DocumentaryMediaProjectionFixture.Leo()));Assert.DoesNotContain("Leo",orion);Assert.DoesNotContain("Orion",leo);}

    [Fact]
    public void Projection_is_non_mutating_and_byte_deterministic()
    {var first=DocumentaryMediaProjectionFixture.Orion();var before=JsonSerializer.Serialize(first,new JsonSerializerOptions(JsonSerializerDefaults.Web));var result1=new DocumentaryMediaProjector().Project(first);Assert.Equal(before,JsonSerializer.Serialize(first,new JsonSerializerOptions(JsonSerializerDefaults.Web)));var result2=new DocumentaryMediaProjector().Project(DocumentaryMediaProjectionFixture.Orion());Assert.Equal(JsonSerializer.Serialize(result1,new JsonSerializerOptions(JsonSerializerDefaults.Web)),JsonSerializer.Serialize(result2,new JsonSerializerOptions(JsonSerializerDefaults.Web)));}

    [Fact]
    public void Finalizer_categorizes_variant_order_and_identity_corruption()
    {var request=DocumentaryMediaProjectionFixture.Orion();var good=Project(request).Variants;var order=DocumentaryMediaProjector.FinalizeProjection(request,good.Reverse().ToArray());Assert.Contains(DocumentaryMediaProjectionRejectionReason.VariantOrderMismatch,order.RejectionReasons);var source=good[0];var bad=new DocumentaryMediaVariant(source.VariantId+".bad",source.VariantType,source.Format,source.Language,source.Title,source.Description,source.Hook,source.Scenes,source.SceneCount,source.PlannedDurationMilliseconds,source.AspectRatio,source.CorrelationId);var identity=DocumentaryMediaProjector.FinalizeProjection(request,[bad,.. good.Skip(1)]);Assert.Contains(DocumentaryMediaProjectionRejectionReason.VariantIdentityMismatch,identity.RejectionReasons);}

    private static DocumentaryMediaProject Project(DocumentaryMediaProjectionRequest request){var result=new DocumentaryMediaProjector().Project(request);Assert.True(result.IsComplete,string.Join(", ",result.RejectionReasons));return Assert.IsType<DocumentaryMediaProject>(result.MediaProject);}
    private static string English(DocumentaryMediaProject project)=>string.Join(" ",project.Variants.Single(x=>x.VariantType==DocumentaryMediaVariantType.LongEnglish).Scenes.SelectMany(x=>x.Narration).Select(x=>x.Text));
    private static string Key(DocumentaryMediaKnowledgeReference r)=>$"{r.PayloadId}|{r.JsonPointer}";
    private static string Normalize(string value)=>string.Join(" ",value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
}
