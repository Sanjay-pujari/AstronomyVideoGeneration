using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.Composition;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Camera;

public sealed class DynamicFovCalculator : IDynamicFovCalculator
{
    private readonly SpatialCompositionAnalyzer _spatialAnalyzer = new();

    public CameraSolution Calculate(IReadOnlyList<SkyObjectPosition> visibleObjects, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets, IReadOnlyList<SkyObjectPosition> contextTargets, double centerAltitudeDeg, double centerAzimuthDeg, VisibilityRules rules, SceneIntentType intent)
    {
        ArgumentNullException.ThrowIfNull(visibleObjects);
        if (visibleObjects.Count == 0) throw new ArgumentException("At least one visible object is required.", nameof(visibleObjects));
        var scoped = intent switch
        {
            SceneIntentType.HeroShot or SceneIntentType.CloseUp when primaryTargets.Count > 0 => primaryTargets,
            SceneIntentType.WideNight => visibleObjects,
            SceneIntentType.Grouping => primaryTargets.Concat(secondaryTargets).ToList(),
            SceneIntentType.Educational => primaryTargets.Concat(secondaryTargets).Concat(contextTargets).ToList(),
            _ => visibleObjects
        };
        if (scoped.Count == 0) scoped = visibleObjects;
        var analysis = _spatialAnalyzer.Analyze(scoped);
        var spread = analysis.MaxAngularDistanceDeg;
        var fov = visibleObjects.Count == 1
            ? Single(intent)
            : analysis.Classification == SpatialGroupingClassification.ImpossibleGrouping
                ? Math.Min(60, analysis.RecommendedFovDeg)
                : Clamp(intent, spread * Pad(intent));
        var requiresSplit = analysis.SplitScene || spread > rules.MaximumGroupSpreadDeg;
        return new CameraSolution(centerAltitudeDeg, centerAzimuthDeg, fov, requiresSplit, spread);
    }
    static double Single(SceneIntentType i)=>i switch{SceneIntentType.HeroShot=>25,SceneIntentType.CloseUp=>18,SceneIntentType.WideNight=>55,SceneIntentType.Educational=>45,_=>35};
    static double Pad(SceneIntentType i)=>i switch{SceneIntentType.HeroShot=>1.35,SceneIntentType.WideNight=>1.85,SceneIntentType.Educational=>1.7,SceneIntentType.CloseUp=>1.25,_=>1.55};
    static double Clamp(SceneIntentType i,double v)=>i switch{SceneIntentType.CloseUp=>Math.Clamp(v,12,35),SceneIntentType.HeroShot=>Math.Clamp(v,18,55),SceneIntentType.WideNight=>Math.Clamp(v,45,95),SceneIntentType.Educational=>Math.Clamp(v,35,90),_=>Math.Clamp(v,25,75)};
}
