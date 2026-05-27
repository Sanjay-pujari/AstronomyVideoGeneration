using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Composition;

public interface IPrimaryTargetResolver
{
    PrimaryTargetResult Resolve(IReadOnlyList<SkyObjectPosition> visibleObjects, string? sceneCode, string? sceneTitle, IReadOnlyList<string>? explicitTargets);
}
