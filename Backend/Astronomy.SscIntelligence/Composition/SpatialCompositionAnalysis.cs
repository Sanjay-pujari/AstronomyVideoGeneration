namespace Astronomy.SscIntelligence.Composition;

public enum SpatialGroupingClassification
{
    TightGrouping,
    MediumGrouping,
    WidePanorama,
    ImpossibleGrouping
}

public sealed record SpatialPairDistance(
    string ObjectA,
    string ObjectB,
    double AzimuthDeltaDeg,
    double AltitudeDeltaDeg,
    double AngularDistanceDeg);

public sealed record SpatialCompositionAnalysis(
    IReadOnlyList<SpatialPairDistance> PairDistances,
    double AzimuthSpreadDeg,
    double AltitudeSpreadDeg,
    double MaxAngularDistanceDeg,
    SpatialGroupingClassification Classification,
    double RecommendedFovDeg,
    bool SplitScene,
    IReadOnlyList<IReadOnlyList<string>> SuggestedSceneGroups);
