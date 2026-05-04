using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

internal sealed class PassThroughSeoMetadataGeneratorService : ISeoMetadataGeneratorService
{
    public Task<SeoMetadataResult> GenerateAsync(SeoMetadataRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new SeoMetadataResult());
}

internal sealed class NoOpSeoMetadataGeneratorService : ISeoMetadataGeneratorService
{
    public Task<SeoMetadataResult> GenerateAsync(SeoMetadataRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new SeoMetadataResult());
}
