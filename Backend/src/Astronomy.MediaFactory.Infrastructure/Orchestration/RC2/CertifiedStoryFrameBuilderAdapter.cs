using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

/// <summary>Test seam that delegates exactly once to the existing production storyboard builder.</summary>
public sealed class CertifiedStoryFrameBuilderAdapter(CreativeStoryboardBuilder builder) : ICertifiedStoryFrameBuilder
{
    public string BuilderType => nameof(CreativeStoryboardBuilder);
    public string BuilderVersion => CreativeStoryboardBuilder.AuthorityBuilderVersion;

    public Task<IReadOnlyList<StoryFrameAuthorityFrame>> BuildAsync(
        DocumentaryBlueprintEditorialContract editorialContract,
        IReadOnlyList<string> requestedVariants,
        CancellationToken cancellationToken) =>
        builder.BuildCertifiedFramesAsync(editorialContract, requestedVariants, cancellationToken);
}
