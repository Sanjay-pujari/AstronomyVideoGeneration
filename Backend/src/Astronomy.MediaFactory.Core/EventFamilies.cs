namespace Astronomy.MediaFactory.Core;

public enum EventFamily
{
    Meteor,
    PlanetGrouping,
    Moon,
    Eclipse,
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
        [EventFamily.Eclipse] = ["SOLAR_ECLIPSE", "LUNAR_ECLIPSE", "TOTAL_SOLAR_ECLIPSE", "PARTIAL_SOLAR_ECLIPSE", "ANNULAR_SOLAR_ECLIPSE", "TOTAL_LUNAR_ECLIPSE", "PARTIAL_LUNAR_ECLIPSE", "PENUMBRAL_LUNAR_ECLIPSE"],
        [EventFamily.SpecialEvent] = ["COMET", "DEEP_SKY_OBJECT", "CONSTELLATION", "OCCULTATION", "ASTERISM", "RARE_VISIBILITY_EVENT"]
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
    IReadOnlyList<string> ForbiddenTerms { get; }
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
    public virtual IReadOnlyList<string> ForbiddenTerms => [];
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
    public override string ValidatorProfile => "CurrentEvent";
    public override string ThumbnailCompositionType => "RC1CinematicThumbnail";
}

public sealed class SpecialEventFamilyProfile : EventFamilyProfileBase
{
    public override EventFamily Family => EventFamily.SpecialEvent;
    public override string ValidatorProfile => "CurrentEvent";
    public override string ThumbnailCompositionType => "RC1CinematicThumbnail";
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
            EventFamily.SpecialEvent => new SpecialEventFamilyProfile(),
            _ => new UnknownFamilyProfile(eventType)
        };

    private sealed class UnknownFamilyProfile(string? eventType) : EventFamilyProfileBase
    {
        public override EventFamily Family => EventFamily.Unknown;
        public override string ValidatorProfile => "CurrentEvent";
        public override string ThumbnailCompositionType => string.IsNullOrWhiteSpace(eventType) ? "RC1CinematicThumbnail" : "RC1CinematicThumbnail";
    }
}
