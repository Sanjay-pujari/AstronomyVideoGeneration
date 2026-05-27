using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Composition;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Rendering;
using Astronomy.SscIntelligence.SceneIntent;
using Astronomy.SscIntelligence.Storytelling;
using Astronomy.SscIntelligence.Visibility;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.SscIntelligence;

public static class DependencyInjection
{
    public static IServiceCollection AddSscIntelligence(this IServiceCollection services)
    {
        services.AddSingleton<INightWindowResolver, NightWindowResolver>();
        services.AddSingleton<IVisibilityFilter, VisibilityFilter>();
        services.AddSingleton<ICameraCenterCalculator, CameraCenterCalculator>();
        services.AddSingleton<IDynamicFovCalculator, DynamicFovCalculator>();
        services.AddSingleton<IPrimaryTargetResolver, PrimaryTargetResolver>();
        services.AddSingleton<ICompositionBiasResolver, CompositionBiasResolver>();
        services.AddSingleton<IDynamicBiasLimiter, DynamicBiasLimiter>();
        services.AddSingleton<IScreenSpaceFramingSolver, ScreenSpaceFramingSolver>();
        services.AddSingleton<ICinematicAnchorSolver, CinematicAnchorSolver>();
        services.AddSingleton<IUnifiedCameraComposer, UnifiedCameraComposer>();
        services.AddSingleton<IStellariumSscRenderer, StellariumSscRenderer>();
        services.AddSingleton<ISceneIntentResolver, SceneIntentResolver>();
        services.AddSingleton<IAngularRelationshipAnalyzer, AngularRelationshipAnalyzer>();
        services.AddSingleton<ICelestialEventClassifier, CelestialEventClassifier>();
        services.AddSingleton<IVisualSignificanceEngine, VisualSignificanceEngine>();
        services.AddSingleton<IAstronomicalSceneScorer, AstronomicalSceneScorer>();
        services.AddSingleton<ISscIntelligenceService, SscIntelligenceService>();
        return services;
    }
}
