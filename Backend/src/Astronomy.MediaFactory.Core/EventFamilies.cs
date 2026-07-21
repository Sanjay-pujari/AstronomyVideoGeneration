namespace Astronomy.MediaFactory.Core;

public enum EventFamily
{
    Meteor,
    PlanetGrouping,
    Moon,
    Eclipse,
    Constellation,
    SpecialEvent,
    Unknown
}

public sealed record EventFamilyResolution(
    EventFamily Family,
    string Reason,
    IReadOnlyDictionary<string, object> Input);

public static class EventFamilyResolver
{
    private static readonly IReadOnlyDictionary<EventFamily, string[]> Tokens = new Dictionary<EventFamily, string[]>
    {
        [EventFamily.Meteor] = ["MeteorShower", "METEOR_SHOWER", "MeteorShowerPeak"],
        [EventFamily.PlanetGrouping] = ["PLANET_CONJUNCTION", "PlanetConjunction", "PLANET_GROUPING", "PlanetGrouping", "PLANET_PAIRING", "PlanetPairing", "PLANET_PARADE", "PlanetParade", "PLANET_ALIGNMENT", "MoonPlanetPairing"],
        [EventFamily.Moon] = ["NamedFullMoon", "FULL_MOON", "FullMoon", "SpecialMoonPhase", "NEW_MOON", "NewMoon", "BLUE_MOON", "BlueMoon", "SUPERMOON", "Supermoon", "MICROMOON", "Micromoon", "MOON_PHASE", "MoonPhase", "FirstQuarter", "LastQuarter"],
        [EventFamily.Eclipse] = ["Eclipse", "SolarEclipse", "LunarEclipse", "TotalSolarEclipse", "PartialSolarEclipse", "AnnularSolarEclipse", "TotalLunarEclipse", "PartialLunarEclipse", "PenumbralLunarEclipse", "SOLAR_ECLIPSE", "LUNAR_ECLIPSE", "TOTAL_SOLAR_ECLIPSE", "PARTIAL_SOLAR_ECLIPSE", "ANNULAR_SOLAR_ECLIPSE", "TOTAL_LUNAR_ECLIPSE", "PARTIAL_LUNAR_ECLIPSE", "PENUMBRAL_LUNAR_ECLIPSE"],
        [EventFamily.Constellation] = ["CONSTELLATION", "Constellation"],
        [EventFamily.SpecialEvent] = ["COMET", "DEEP_SKY_OBJECT", "OCCULTATION", "ASTERISM", "RARE_VISIBILITY_EVENT"]
    };

    public static EventFamily Resolve(string? eventType, string? contentCategoryCode, IReadOnlyList<string>? primaryObjects, IReadOnlyList<string>? secondaryObjects, string? title = null)
        => ResolveWithDiagnostics(eventType, contentCategoryCode, primaryObjects, secondaryObjects, title).Family;

    public static EventFamilyResolution ResolveWithDiagnostics(string? eventType, string? contentCategoryCode, IReadOnlyList<string>? primaryObjects, IReadOnlyList<string>? secondaryObjects, string? title = null)
    {
        var values = new[] { (Name: "eventType", Value: eventType), (Name: "contentCategoryCode", Value: contentCategoryCode) };
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            foreach (var entry in Tokens)
            {
                var match = entry.Value.FirstOrDefault(token => MatchesToken(value, token));
                if (match is not null)
                    return Build(entry.Key, $"Matched {name} '{value}' to mapping token '{match}'.", eventType, contentCategoryCode, primaryObjects, secondaryObjects);
            }
        }

        if (!string.IsNullOrWhiteSpace(title) && title.Contains("Moon", StringComparison.OrdinalIgnoreCase))
            return Build(EventFamily.Moon, $"Matched title fallback '{title}' because it contains Moon.", eventType, contentCategoryCode, primaryObjects, secondaryObjects);

        return Build(EventFamily.Unknown, "No eventType, contentCategoryCode, or title fallback mapping matched.", eventType, contentCategoryCode, primaryObjects, secondaryObjects);
    }

    private static bool MatchesToken(string value, string token)
        => string.Equals(value, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Normalize(value), Normalize(token), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static EventFamilyResolution Build(EventFamily family, string reason, string? eventType, string? contentCategoryCode, IReadOnlyList<string>? primaryObjects, IReadOnlyList<string>? secondaryObjects)
        => new(family, reason, new Dictionary<string, object>
        {
            ["eventType"] = eventType ?? string.Empty,
            ["contentCategoryCode"] = contentCategoryCode ?? string.Empty,
            ["primaryObjects"] = primaryObjects ?? [],
            ["secondaryObjects"] = secondaryObjects ?? []
        });
}

public interface IEventFamilyProfile
{
    EventFamily Family { get; }
    string ValidatorProfile { get; }
    string ThumbnailCompositionType { get; }
    string SelectedProfile { get; }
    IReadOnlyList<string> ForbiddenTerms { get; }
    IReadOnlyList<string> RequiredVisualElements { get; }
    IReadOnlyList<string> RequiredOverlayElements { get; }
    IReadOnlyList<string> RequiredDiagnosticFields { get; }
    bool AllowsGuideCard { get; }
    bool AllowsObjectLabels { get; }
    bool AllowsDirectionCue { get; }
    bool AllowsSeparationCue { get; }
}

public abstract class EventFamilyProfileBase : IEventFamilyProfile
{
    public abstract EventFamily Family { get; }
    public abstract string ValidatorProfile { get; }
    public abstract string ThumbnailCompositionType { get; }
    public virtual string SelectedProfile => GetType().Name;
    public virtual IReadOnlyList<string> ForbiddenTerms => [];
    public virtual IReadOnlyList<string> RequiredVisualElements => [];
    public virtual IReadOnlyList<string> RequiredOverlayElements => [];
    public virtual IReadOnlyList<string> RequiredDiagnosticFields => ["eventFamily", "eventFamilyResolverInput", "eventFamilyResolverReason", "eventFamilyProfileName", "eventFamilyProfileVersion"];
    public virtual bool AllowsGuideCard => false;
    public virtual bool AllowsObjectLabels => false;
    public virtual bool AllowsDirectionCue => false;
    public virtual bool AllowsSeparationCue => false;
}

public sealed class MeteorFamilyProfile : EventFamilyProfileBase
{
    public override EventFamily Family => EventFamily.Meteor;
    public override string ValidatorProfile => "MeteorShower";
    public override string ThumbnailCompositionType => "RadiantBurstThumbnail";
}

public sealed class PlanetGroupingFamilyProfile : EventFamilyProfileBase
{
    public override EventFamily Family => EventFamily.PlanetGrouping;
    public override string ValidatorProfile => "PlanetConjunction";
    public override string ThumbnailCompositionType => "PlanetarySkyGuideThumbnail";
    public override bool AllowsGuideCard => true;
    public override bool AllowsObjectLabels => true;
    public override bool AllowsDirectionCue => true;
    public override bool AllowsSeparationCue => true;
}

public sealed class MoonFamilyProfile : EventFamilyProfileBase
{
    public override EventFamily Family => EventFamily.Moon;
    public override string ValidatorProfile => "Moon";
    public override string ThumbnailCompositionType => "MoonPhaseGuideThumbnail";
    public override IReadOnlyList<string> ForbiddenTerms => ["Jupiter", "Venus", "Mars", "conjunction", "pairing", "alignment", "separation", "radiant", "meteor streaks", "meteor", "meteor shower", "Geminids", "planet conjunction", "planet pairing", "Jupiter + Venus", "debris stream", "Phaethon"];
    public IReadOnlyList<string> AllowedConcepts => ["Moon", "full moon", "moonrise", "moon phase", "eastern sky", "visibility", "moonlight", "lunar disc", "craters"];
    public override IReadOnlyList<string> RequiredDiagnosticFields => base.RequiredDiagnosticFields.Concat(["validatorProfile", "moonPhaseName", "moonIlluminationPercent", "moonriseLocal", "moonsetLocal", "moonGuideCardAdded", "moonObjectRendered", "moonForbiddenTermsDetected"]).ToArray();
    public override bool AllowsGuideCard => true;
    public override bool AllowsDirectionCue => true;
}

public sealed class EclipseFamilyProfile : EventFamilyProfileBase
{
    public override EventFamily Family => EventFamily.Eclipse;
    public override string ValidatorProfile => "Eclipse";
    public override string ThumbnailCompositionType => "EclipseGuideThumbnail";
    public override IReadOnlyList<string> RequiredDiagnosticFields => base.RequiredDiagnosticFields.Concat(["validatorProfile", "eclipseType", "observationWarning", "directionCueAdded", "guideCardAdded"]).ToArray();
    public override bool AllowsGuideCard => true;
    public override bool AllowsObjectLabels => true;
    public override bool AllowsDirectionCue => true;
}

public sealed class ConstellationFamilyProfile : EventFamilyProfileBase
{
    public override EventFamily Family => EventFamily.Constellation;
    public override string ValidatorProfile => "Constellation";
    public override string ThumbnailCompositionType => "ConstellationNavigationThumbnail";
    public override IReadOnlyList<string> ForbiddenTerms => ["meteor radiant", "meteor streak", "meteor shower", "planet conjunction", "angular separation", "solar eclipse safety", "eclipse glasses"];
    public override IReadOnlyList<string> RequiredVisualElements => ["star pattern lines", "recognizable constellation star field", "major-star labels", "sky navigation context"];
    public override IReadOnlyList<string> RequiredOverlayElements => ["constellation name label", "direction guide", "Belt recognition steps"];
    public override IReadOnlyList<string> RequiredDiagnosticFields => base.RequiredDiagnosticFields.Concat(["validatorProfile", "constellationName", "iauAbbreviation", "scientificCulturalSeparation", "constellationVisualGuidanceApplied"]).ToArray();
    public override bool AllowsGuideCard => true;
    public override bool AllowsObjectLabels => true;
    public override bool AllowsDirectionCue => true;
}

public sealed class SpecialEventFamilyProfile : EventFamilyProfileBase
{
    private readonly SpecialEventSubtype subtype;

    public SpecialEventFamilyProfile(string? eventType = null)
    {
        subtype = SpecialEventSubtypeResolver.Resolve(eventType);
    }

    public override EventFamily Family => EventFamily.SpecialEvent;
    public override string ValidatorProfile => subtype switch
    {
        SpecialEventSubtype.Comet => "SpecialEventComet",
        SpecialEventSubtype.DeepSkyObject => "SpecialEventDeepSkyObject",
        SpecialEventSubtype.Constellation => "SpecialEventConstellation",
        SpecialEventSubtype.Occultation => "SpecialEventOccultation",
        _ => "SpecialEvent"
    };
    public override string ThumbnailCompositionType => subtype switch
    {
        SpecialEventSubtype.Comet => "CometSkyGuideThumbnail",
        SpecialEventSubtype.DeepSkyObject => "DeepSkyObjectGuideThumbnail",
        SpecialEventSubtype.Constellation => "ConstellationNavigationThumbnail",
        SpecialEventSubtype.Occultation => "OccultationTimingThumbnail",
        _ => "SpecialEventGuideThumbnail"
    };
    public override string SelectedProfile => $"SpecialEvent:{subtype}";
    public override IReadOnlyList<string> ForbiddenTerms => subtype == SpecialEventSubtype.Occultation
        ? ["meteor radiant", "radiant", "meteor streak", "meteor shower", "debris stream", "Phaethon", "solar eclipse safety", "eclipse glasses"]
        : ["meteor radiant", "radiant", "meteor streak", "meteor shower", "debris stream", "Phaethon", "angular separation", "separation label", "planet grouping", "planet conjunction", "solar eclipse safety", "eclipse glasses"];
    public override IReadOnlyList<string> RequiredVisualElements => subtype switch
    {
        SpecialEventSubtype.Comet => ["comet nucleus", "comet tail", "dark sky", "binocular viewing context"],
        SpecialEventSubtype.DeepSkyObject => ["nebula, cluster, or galaxy style target", "deep-sky field", "telescope or binocular viewing context"],
        SpecialEventSubtype.Constellation => ["star pattern lines", "recognizable star field", "easy sky navigation context"],
        SpecialEventSubtype.Occultation => ["foreground object crossing or covering background object", "paired objects when relevant", "time-sensitive sky geometry"],
        _ => ["special event sky target", "event-specific viewing context"]
    };
    public override IReadOnlyList<string> RequiredOverlayElements => subtype switch
    {
        SpecialEventSubtype.Comet => ["comet name label", "dark-sky/binocular guidance", "where-to-look cue"],
        SpecialEventSubtype.DeepSkyObject => ["object type label", "telescope/binocular guidance", "where-to-look cue"],
        SpecialEventSubtype.Constellation => ["constellation name label", "direction guide", "simple navigation steps"],
        SpecialEventSubtype.Occultation => ["occultation timing", "foreground/background object labels", "event window emphasis"],
        _ => ["special event label", "where-to-look cue"]
    };
    public override IReadOnlyList<string> RequiredDiagnosticFields => base.RequiredDiagnosticFields.Concat(["detectedFamily", "primaryEventTypeCode", "selectedProfile", "forbiddenTerms", "requiredVisualElements", "requiredOverlayElements"]).ToArray();
    public override bool AllowsGuideCard => true;
    public override bool AllowsObjectLabels => true;
    public override bool AllowsDirectionCue => subtype is SpecialEventSubtype.Comet or SpecialEventSubtype.DeepSkyObject or SpecialEventSubtype.Constellation;
    public override bool AllowsSeparationCue => subtype == SpecialEventSubtype.Occultation;
}

public enum SpecialEventSubtype
{
    Generic,
    Comet,
    DeepSkyObject,
    Constellation,
    Occultation
}

public static class SpecialEventSubtypeResolver
{
    public static SpecialEventSubtype Resolve(string? eventType)
    {
        var normalized = Normalize(eventType);
        if (normalized.Contains("COMET", StringComparison.OrdinalIgnoreCase)) return SpecialEventSubtype.Comet;
        if (normalized.Contains("DEEPSKYOBJECT", StringComparison.OrdinalIgnoreCase) || normalized.Contains("DSO", StringComparison.OrdinalIgnoreCase) || normalized.Contains("NEBULA", StringComparison.OrdinalIgnoreCase) || normalized.Contains("CLUSTER", StringComparison.OrdinalIgnoreCase) || normalized.Contains("GALAXY", StringComparison.OrdinalIgnoreCase)) return SpecialEventSubtype.DeepSkyObject;
        if (normalized.Contains("CONSTELLATION", StringComparison.OrdinalIgnoreCase)) return SpecialEventSubtype.Constellation;
        if (normalized.Contains("OCCULTATION", StringComparison.OrdinalIgnoreCase)) return SpecialEventSubtype.Occultation;
        return SpecialEventSubtype.Generic;
    }

    public static string Normalize(string? eventType)
        => string.IsNullOrWhiteSpace(eventType) ? string.Empty : new string(eventType.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

public static class EventFamilyProfiles
{
    public const string Version = "RC2-family-abstraction-v1";

    public static IEventFamilyProfile Resolve(EventFamily family, string? eventType = null)
        => family switch
        {
            EventFamily.Meteor => new MeteorFamilyProfile(),
            EventFamily.PlanetGrouping => new PlanetGroupingFamilyProfile(),
            EventFamily.Moon => new MoonFamilyProfile(),
            EventFamily.Eclipse => new EclipseFamilyProfile(),
            EventFamily.Constellation => new ConstellationFamilyProfile(),
            EventFamily.SpecialEvent => new SpecialEventFamilyProfile(eventType),
            _ => new UnknownFamilyProfile(eventType)
        };

    private sealed class UnknownFamilyProfile(string? eventType) : EventFamilyProfileBase
    {
        public override EventFamily Family => EventFamily.Unknown;
        public override string ValidatorProfile => "CurrentEvent";
        public override string ThumbnailCompositionType => string.IsNullOrWhiteSpace(eventType) ? "RC1CinematicThumbnail" : "RC1CinematicThumbnail";
    }
}
