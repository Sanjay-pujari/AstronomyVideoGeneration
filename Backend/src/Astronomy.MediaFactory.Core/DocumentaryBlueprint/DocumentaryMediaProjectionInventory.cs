using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryMediaProjectionStatus { Complete, Rejected }
public enum DocumentaryMediaProjectionRejectionReason { MaterializationRecordNotComplete, MaterializationIdentityMismatch, ExportSpecificationIdentityMismatch, CertificationIdentityMismatch, ProvenanceIdentityMismatch, PackageIdentityMismatch, CorrelationMismatch, ProjectionPolicyRejected, TopicProfileRejected, RequiredVariantMissing, VariantInventoryMismatch, VariantOrderMismatch, VariantIdentityMismatch, SceneInventoryMismatch, SceneOrderMismatch, SceneIdentityMismatch, NarrativeMappingMismatch, SubtitleMappingMismatch, VisualPromptMappingMismatch, TimingPlanMismatch, UnsupportedVariantPresent }
public enum DocumentaryAstronomyTopicFamily { Constellation, PlanetConjunction, PlanetPairing, PlanetOpposition, PlanetElongation, PlanetVisibility, MeteorShower, MoonPhase, NamedMoon, SolarEclipse, LunarEclipse, Occultation, PlanetTransit, Planet, Moon, DeepSkyObject, Galaxy, Nebula, OpenCluster, GlobularCluster, DoubleStar, VariableStar, SupernovaRemnant, Comet, Asteroid, SeasonalSky, ObservingGuide }
public enum DocumentaryVideoFormat { Long, Short }
public enum DocumentaryMediaLanguage { English, Hindi }
public enum DocumentaryMediaVariantType { LongEnglish, LongHindi, ShortEnglish, ShortHindi }
public enum DocumentaryMediaSceneRole { Hook, Introduction, Identity, Context, Location, Visibility, MajorFeature, SupportingFeature, Science, Mythology, Observation, Equipment, Astrophotography, Safety, Summary, CallToAction }
public enum DocumentaryMediaVisualType { GeneratedIllustration, SkySimulation, StarChart, TelescopeView, OrbitalDiagram, ScientificDiagram, HistoricalIllustration, ObjectPortrait, TextCard, Map, Timeline }
public enum DocumentaryCameraMotion { None, SlowZoomIn, SlowZoomOut, PanLeft, PanRight, PanUp, PanDown, OrbitClockwise, OrbitCounterClockwise, TrackForward, TrackBackward }
public enum DocumentarySceneTransition { Cut, CrossFade, FadeToBlack, FadeFromBlack, Dissolve }
public enum DocumentarySubtitlePresentation { Standard, Emphasis, ScientificTerm, ObjectName, ObservationInstruction }

internal static class DocumentaryMediaProjectionInventory
{
    internal static readonly DocumentaryMediaVariantType[] Variants = Enum.GetValues<DocumentaryMediaVariantType>();
    internal static readonly DocumentaryMediaLanguage[] Languages = Enum.GetValues<DocumentaryMediaLanguage>();
    internal static readonly DocumentaryVideoFormat[] Formats = Enum.GetValues<DocumentaryVideoFormat>();
    internal static readonly DocumentaryAstronomyTopicFamily[] TopicFamilies = Enum.GetValues<DocumentaryAstronomyTopicFamily>();
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> value, string name) { ArgumentNullException.ThrowIfNull(value, name); return new ReadOnlyCollection<T>(value.ToArray()); }
    internal static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
    internal static (DocumentaryVideoFormat Format, DocumentaryMediaLanguage Language) Mapping(DocumentaryMediaVariantType type) => type switch
    {
        DocumentaryMediaVariantType.LongEnglish => (DocumentaryVideoFormat.Long, DocumentaryMediaLanguage.English),
        DocumentaryMediaVariantType.LongHindi => (DocumentaryVideoFormat.Long, DocumentaryMediaLanguage.Hindi),
        DocumentaryMediaVariantType.ShortEnglish => (DocumentaryVideoFormat.Short, DocumentaryMediaLanguage.English),
        DocumentaryMediaVariantType.ShortHindi => (DocumentaryVideoFormat.Short, DocumentaryMediaLanguage.Hindi),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
