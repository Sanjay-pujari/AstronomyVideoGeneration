using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryMediaPipelineCertificationMatrixTests
{
    [Fact]
    public void Every_rejection_reason_is_executable_in_a_validated_result_collection()
    {
        var baseline=DocumentaryMediaPipelineFixture.Run(DocumentaryMediaPipelineFixture.Orion()).ExecutionRecord!;
        var reasons=Enum.GetValues<DocumentaryMediaPipelineRejectionReason>().Order().ToArray();
        var variants=baseline.VariantRecords.Select((variant,index)=>index==0
            ? variant with{Status=DocumentaryMediaPipelineStatus.Rejected,OutputAssetId=null,RejectionReasons=reasons}
            : variant).ToArray();
        var manifest=baseline.OutputManifest with{VariantRecords=variants,CompletedVariantCount=3,FailedVariantCount=1};
        var first=baseline with{Status=DocumentaryMediaPipelineStatus.PartiallyComplete,VariantRecords=variants,OutputManifest=manifest,
            CompletedVariantCount=3,FailedVariantCount=1};
        DocumentaryMediaPipelineValidator.ValidateExecutionRecord(first);
        var actual=first.VariantRecords[0].RejectionReasons;
        Assert.Equal(28,actual.Count); Assert.All(actual,AssertDefined);
        Assert.Equal(actual.Count,actual.Distinct().Count()); Assert.Equal(actual.Order(),actual);
        var reconstructed=first with{VariantRecords=first.VariantRecords.Select(x=>x with{RejectionReasons=x.RejectionReasons.ToArray()}).ToArray()};
        Assert.Equal(actual,reconstructed.VariantRecords[0].RejectionReasons);
    }

    public static IEnumerable<object[]> ProviderResultCases()
    {
        var types = new[] { DocumentaryMediaAssetType.VisualImage, DocumentaryMediaAssetType.NarrationAudio,
            DocumentaryMediaAssetType.SubtitleDocument, DocumentaryMediaAssetType.SceneVideo, DocumentaryMediaAssetType.VariantVideo };
        foreach (var type in types)
        foreach (var mutation in Enumerable.Range(0,13)) yield return [type,mutation];
    }

    [Theory]
    [MemberData(nameof(ProviderResultCases))]
    public void Every_provider_result_field_is_validated(DocumentaryMediaAssetType targetType, int mutation)
    {
        var fake=new DocumentaryMediaPipelineFakeProviders();
        fake.AssetResultTransform=(plan,result) => plan.AssetType==targetType ? Mutate(result,mutation) : result;
        var pipeline=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion()));
        var expected=targetType switch {
            DocumentaryMediaAssetType.VisualImage => DocumentaryMediaPipelineRejectionReason.VisualGenerationFailed,
            DocumentaryMediaAssetType.NarrationAudio => DocumentaryMediaPipelineRejectionReason.NarrationSynthesisFailed,
            DocumentaryMediaAssetType.SubtitleDocument => DocumentaryMediaPipelineRejectionReason.SubtitleGenerationFailed,
            DocumentaryMediaAssetType.SceneVideo => DocumentaryMediaPipelineRejectionReason.SceneCompositionFailed,
            _ => DocumentaryMediaPipelineRejectionReason.VariantCompositionFailed };
        Assert.Contains(expected,pipeline.RejectionReasons);
        Assert.DoesNotContain(pipeline.ExecutionRecord!.VariantRecords.Where(v=>v.RejectionReasons.Contains(expected)),v=>v.OutputAssetId is not null);
    }

    public static IEnumerable<object[]> VerificationCases() => Enumerable.Range(0,13).Select(x=>new object[]{x});

    [Theory]
    [MemberData(nameof(VerificationCases))]
    public void Every_render_verification_field_is_independently_enforced(int mutation)
    {
        var fake=new DocumentaryMediaPipelineFakeProviders();
        fake.VerificationResultTransform=(request,result)=>Mutate(result,mutation);
        var pipeline=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion()));
        Assert.Contains(DocumentaryMediaPipelineRejectionReason.RenderVerificationFailed,pipeline.RejectionReasons);
        Assert.All(pipeline.ExecutionRecord!.VariantRecords,v=>Assert.Null(v.OutputAssetId));
        Assert.Empty(pipeline.ExecutionRecord.OutputManifest.Assets.Where(x=>x.AssetType==DocumentaryMediaAssetType.VariantVideo));
    }

    private static DocumentaryMediaAssetResult Mutate(DocumentaryMediaAssetResult value,int field) => field switch {
        0=>value with{AssetId="wrong"}, 1=>value with{AssetType=DocumentaryMediaAssetType.HistoricalIllustrationImage},
        2=>value with{AssetFormat=DocumentaryMediaAssetFormat.Jpeg}, 3=>value with{Status=DocumentaryMediaAssetStatus.Failed},
        4=>value with{AttemptCount=0}, 5=>value with{Width=value.Width+1}, 6=>value with{FrameRate=value.FrameRate+1},
        7=>value with{SampleRate=value.SampleRate+1}, 8=>value with{ChannelCount=value.ChannelCount+1},
        9=>value with{CorrelationId="wrong"}, 10=>value with{DurationMilliseconds=0}, 11=>value with{Checksum=null},
        12=>value with{ContentIdentity=null}, _=>throw new ArgumentOutOfRangeException(nameof(field)) };

    private static DocumentaryRenderVerificationResult Mutate(DocumentaryRenderVerificationResult value,int field) => field switch {
        0=>value with{IsValid=false}, 1=>value with{ActualSceneCount=value.ActualSceneCount+1},
        2=>value with{ActualWidth=value.ActualWidth+1}, 3=>value with{ActualHeight=value.ActualHeight+1},
        4=>value with{ActualFrameRate=value.ActualFrameRate+1}, 5=>value with{ActualAudioSampleRate=value.ActualAudioSampleRate+1},
        6=>value with{ActualAudioChannelCount=value.ActualAudioChannelCount+1}, 7=>value with{ActualDurationMilliseconds=0},
        8=>value with{HasVideo=false}, 9=>value with{HasAudio=false}, 10=>value with{HasSubtitleTrack=false},
        11=>value with{ChecksumValid=false}, 12=>value with{Failures=["independent field failure"]},
        _=>throw new ArgumentOutOfRangeException(nameof(field)) };

    private static void AssertDefined(DocumentaryMediaPipelineRejectionReason reason) => Assert.True(Enum.IsDefined(reason));
}
