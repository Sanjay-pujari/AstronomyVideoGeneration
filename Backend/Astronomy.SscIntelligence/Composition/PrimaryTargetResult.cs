using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Composition;

public sealed record PrimaryTargetResult(
    IReadOnlyList<SkyObjectPosition> PrimaryTargets,
    IReadOnlyList<SkyObjectPosition> SecondaryTargets,
    IReadOnlyList<SkyObjectPosition> ContextTargets)
{
    public IReadOnlyList<SkyObjectPosition> AllTargets => PrimaryTargets.Concat(SecondaryTargets).Concat(ContextTargets).ToList();
}
