using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Spatial;

public sealed record SpatialCompositionResult(
    SpatialCompositionClass CompositionClass,
    IReadOnlyList<SpatialPairAnalysis> PairDistances,
    double MaxAngularSeparationDeg,
    double AltitudeSpreadDeg,
    double AzimuthSpreadDeg,
    SpatialPairAnalysis? ClosestPair,
    SpatialPairAnalysis? FarthestPair,
    (double MinDeg, double MaxDeg)? RecommendedFovRange,
    bool SplitRecommended,
    IReadOnlyList<SpatialObjectCluster> Clusters,
    SpatialObjectCluster DominantCluster,
    IReadOnlyList<SkyObjectPosition> DeferredObjects
);
