using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public sealed record AngularPairSeparation(string ObjectA, string ObjectB, double SeparationDeg);

public sealed record AngularRelationshipResult(
    IReadOnlyList<AngularPairSeparation> PairwiseSeparations,
    AngularPairSeparation? ClosestPair,
    double MaxSpreadDeg,
    double AverageAltitudeDeg,
    SkyObjectPosition? BrightestObject,
    int ObjectCount);
