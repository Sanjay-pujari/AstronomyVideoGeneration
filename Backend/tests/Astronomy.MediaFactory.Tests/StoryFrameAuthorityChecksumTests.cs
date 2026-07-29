using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StoryFrameAuthorityChecksumTests
{
    [Fact]
    public void Authority_checksum_is_stable_across_generated_utc()
    {
        var value=Authority();
        Assert.Equal(StoryFrameAuthorityChecksum.Authority(value),StoryFrameAuthorityChecksum.Authority(value with { GeneratedUtc=value.GeneratedUtc.AddDays(1) }));
    }

    [Fact]
    public void Authority_checksum_is_stable_across_unordered_relationship_insertion_order()
    {
        var value=Authority(); var frame=value.Frames[0];
        var changed=value with { Frames=[frame with { ViewerQuestionIds=frame.ViewerQuestionIds.Reverse().ToArray() }] };
        Assert.Equal(StoryFrameAuthorityChecksum.Authority(value),StoryFrameAuthorityChecksum.Authority(changed));
    }

    [Theory]
    [InlineData("certification")]
    [InlineData("editorial")]
    [InlineData("phase4")]
    [InlineData("builder")]
    [InlineData("visual")]
    [InlineData("timing")]
    public void Authority_checksum_changes_for_semantic_mutations(string mutation)
    {
        var value=Authority(); var frame=value.Frames[0];
        var changed=mutation switch {
            "certification"=>value with { SourceCertificationChecksum="changed" },
            "editorial"=>value with { SourceEditorialContractChecksum="changed" },
            "phase4"=>value with { SourcePhase4Checksum="changed" },
            "builder"=>value with { BuilderVersion="changed" },
            "visual"=>value with { Frames=[frame with { VisualIntent="changed" }] },
            _=>value with { Frames=[frame with { EstimatedDuration=19 }] }
        };
        Assert.NotEqual(StoryFrameAuthorityChecksum.Authority(value),StoryFrameAuthorityChecksum.Authority(changed));
    }

    [Fact]
    public void Authority_checksum_changes_when_variant_order_changes_if_variant_order_is_semantic()
    {
        var value=Authority();
        Assert.NotEqual(StoryFrameAuthorityChecksum.Authority(value),StoryFrameAuthorityChecksum.Authority(value with { RequestedVariants=["Short","Long"] }));
    }

    [Fact]
    public void Index_checksum_is_stable_across_generated_utc_and_changes_for_duration()
    {
        var index=new StoryFrameIndex("i","e","event","en","profile","a","sum","editorial",
            [new("Long",1,1,["s1"],["f1"])],[new("Long","s1",1,"Hook","Hook",["f1"],1,18,true,true)],1,DateTimeOffset.UtcNow,"");
        Assert.Equal(StoryFrameAuthorityChecksum.Index(index),StoryFrameAuthorityChecksum.Index(index with { GeneratedUtc=index.GeneratedUtc.AddDays(1) }));
        Assert.NotEqual(StoryFrameAuthorityChecksum.Index(index),StoryFrameAuthorityChecksum.Index(index with { Scenes=[index.Scenes[0] with { EstimatedDuration=19 }] }));
    }

    private static StoryFramesAuthority Authority()
    {
        var frame=new StoryFrameAuthorityFrame("f1","s1",1,1,"Long","Hook","Hook","Primary",
            ["q2","q1"],["l1"],["k1"],"narrative","visual","Wide","direction","movement","subject","setting","composition","lighting","mood","motion","FadeIn","FadeOut",[],[],["image"],[],true,"Phase7",0,18,[],[],[]);
        return new("a","e","p","event","en","profile","cert","cert-sum","editorial","editorial-sum","phase4",
            "builder","v1",["Long","Short"],[frame],DateTimeOffset.UtcNow,"");
    }
}
