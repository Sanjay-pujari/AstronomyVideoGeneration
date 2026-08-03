using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Deterministic registry for the bounded Phase 7 schema adapters.</summary>
public sealed class Phase7KnowledgeSectionAdapterRegistry
{
    public Phase7KnowledgeSectionAdapterRegistry(IEnumerable<IPhase7KnowledgeSectionAdapter>? adapters = null)
    {
        Adapters = (adapters ?? Defaults()).OrderBy(x => x.AdapterId, StringComparer.Ordinal).ToArray();
        var duplicate = Adapters.SelectMany(a => a.SupportedSectionNames.Select(s => (s, a.AdapterId)))
            .GroupBy(x => x.s, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"P7ADAPTER_DUPLICATE_SECTION:{duplicate.Key}");
    }
    public IReadOnlyList<IPhase7KnowledgeSectionAdapter> Adapters { get; }
    public IPhase7KnowledgeSectionAdapter? Find(string section) => Adapters.SingleOrDefault(a => a.SupportedSectionNames.Contains(section));
    private static IEnumerable<IPhase7KnowledgeSectionAdapter> Defaults() =>
    [
        new IdentityKnowledgeAdapter(), new ScientificKnowledgeAdapter(), new AstronomicalObjectKnowledgeAdapter(),
        new ObservationKnowledgeAdapter(), new AstrophotographyKnowledgeAdapter(), new HistoryKnowledgeAdapter(),
        new CultureAndMythologyKnowledgeAdapter(), new RegionalTraditionKnowledgeAdapter(), new AstrologyClarificationKnowledgeAdapter(),
        new EditorialSafetyKnowledgeAdapter(), new LocalizedContentKnowledgeAdapter(), new InterestingFactsKnowledgeAdapter(),
        new TimingKnowledgeAdapter(), new VisibilityKnowledgeAdapter(), new GeometryKnowledgeAdapter(), new MeteorShowerKnowledgeAdapter(),
        new ConjunctionGroupingKnowledgeAdapter(), new OccultationKnowledgeAdapter(), new TransitKnowledgeAdapter(),
        new OppositionKnowledgeAdapter(), new ElongationKnowledgeAdapter(), new CloseApproachKnowledgeAdapter(),
        new EclipseKnowledgeAdapter(), new CometKnowledgeAdapter(), new SatellitePassKnowledgeAdapter()
    ];
}

public abstract class ApprovedFieldKnowledgeAdapter(string id, string section,
    IReadOnlyDictionary<string, NarrationKnowledgeDomainKey> fields) : IPhase7KnowledgeSectionAdapter
{
    private static readonly HashSet<string> Metadata = new(StringComparer.OrdinalIgnoreCase)
        { "sourceIds", "stableKnowledgeId", "factId", "objectId", "externalId", "catalogId", "canonicalName", "objectType", "objectRole", "useCases", "notes", "confidence", "reviewStatus" };
    public string AdapterId => id;
    public string AdapterVersion => "phase7-section-adapter.v1";
    public IReadOnlySet<string> SupportedSectionNames { get; } = new HashSet<string>([section], StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<NarrationKnowledgeDomainKey> ProducedDomains { get; } = fields.Values.ToHashSet();
    protected virtual bool Qualified => section is "cultureAndMythology" or "regionalTraditions" or "astrologyRelationships";
    protected virtual bool HumanReview => Qualified;

    public Phase7KnowledgeSectionAdapterResult Extract(Phase7KnowledgeSectionContext context)
    {
        var claims = new List<Phase7AdapterClaimCandidate>(); var entities = new List<Phase7KnowledgeEntity>();
        var unknown = new SortedSet<string>(StringComparer.Ordinal); var blocking = new List<string>();
        Visit(context.SectionJson, context.SectionName, context.PayloadId, [], claims, entities, unknown, blocking);
        var duplicate = claims.GroupBy(x => x.SemanticIdentity, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) blocking.Add($"P7KNOWLEDGE_DUPLICATE_SEMANTIC_IDENTITY:{duplicate.Key}");
        var warnings = unknown.Select(x => $"P7KNOWLEDGE_UNKNOWN_PROPERTY:{x}").ToArray();
        var seed = new { AdapterId, AdapterVersion, context.SectionName, Claims = claims.OrderBy(x=>x.SemanticIdentity), Entities = entities.OrderBy(x=>x.KnowledgeId), Unknown = unknown };
        return new(claims, entities, warnings, blocking, unknown.ToArray(), Phase7Determinism.Hash(seed));
    }

    private void Visit(JsonElement value, string path, string inheritedId, string[] inheritedSources,
        List<Phase7AdapterClaimCandidate> claims, List<Phase7KnowledgeEntity> entities, SortedSet<string> unknown, List<string> blocking)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var identity = SemanticId(item, inheritedId);
                Visit(item, path, identity, inheritedSources, claims, entities, unknown, blocking);
            }
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        var sources = Strings(value, "sourceIds");
        if (sources.Length == 0) sources = inheritedSources;
        var entityId = SemanticId(value, inheritedId);
        var name = Scalar(value, "objectName") ?? Scalar(value, "canonicalName") ?? entityId;
        if (value.TryGetProperty("objectId", out _) || value.TryGetProperty("stableKnowledgeId", out _))
        {
            var e = new Phase7KnowledgeEntity(entityId, Scalar(value,"objectType") ?? section, name, sources, "");
            entities.Add(e with { Checksum = Phase7Determinism.Hash(e with { Checksum = "" }) });
        }
        foreach (var property in value.EnumerateObject())
        {
            if (Metadata.Contains(property.Name)) continue;
            var relative = path == section ? property.Name : $"{path[(section.Length + 1)..]}.{property.Name}";
            if (fields.TryGetValue(relative, out var domain) || fields.TryGetValue(property.Name, out domain))
            {
                Emit(property.Value, entityId, $"{section}.{relative}", domain, sources, claims);
            }
            else if (property.Value.ValueKind == JsonValueKind.Object && AllowsContainer(property.Name))
                Visit(property.Value, $"{path}.{property.Name}", entityId, sources, claims, entities, unknown, blocking);
            else unknown.Add($"{path}.{property.Name}");
        }
    }
    protected virtual bool AllowsContainer(string name) => false;
    private void Emit(JsonElement value, string entityId, string fieldPath, NarrationKnowledgeDomainKey domain, string[] sources, List<Phase7AdapterClaimCandidate> claims)
    {
        IEnumerable<string> texts = value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString()!],
            JsonValueKind.Array => value.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!),
            JsonValueKind.Number when fieldPath.EndsWith("areaSquareDegrees",StringComparison.Ordinal) => [$"Constellation area: {value.GetRawText()} square degrees."],
            _ => []
        };
        foreach (var text in texts.Where(x=>!string.IsNullOrWhiteSpace(x)))
        {
            var suffix = Phase7Determinism.Hash(text.Trim())[..12];
            var semantic = $"{entityId}.{fieldPath}.{suffix}".ToLowerInvariant();
            claims.Add(new(entityId, fieldPath, domain, text.Trim(), sources, Qualified, HumanReview, semantic));
        }
    }
    private static string SemanticId(JsonElement item, string fallback)
    {
        foreach (var key in new[] { "stableKnowledgeId", "factId", "objectId", "externalId", "catalogId" })
            if (item.ValueKind == JsonValueKind.Object && Scalar(item,key) is { Length: > 0 } id) return id.Trim().ToLowerInvariant();
        return item.ValueKind == JsonValueKind.Object ? $"{fallback}.{Phase7Determinism.Hash(JsonSerializer.Serialize(item))[..16]}" : fallback;
    }
    private static string? Scalar(JsonElement o,string key)=>o.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static string[] Strings(JsonElement o,string key)=>o.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).ToArray():[];
}

internal static class Phase7ApprovedFields
{
    public static IReadOnlyDictionary<string,NarrationKnowledgeDomainKey> Of(NarrationKnowledgeDomainKey d, params string[] names)
        => names.ToDictionary(x=>x,_=>d,StringComparer.OrdinalIgnoreCase);
}
public sealed class IdentityKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.identity.v1","identity",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Identity,"iauAbbreviation","genitiveName","subjectType"));
public sealed class ScientificKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.scientific.v1","scientific",new Dictionary<string,NarrationKnowledgeDomainKey>(StringComparer.OrdinalIgnoreCase) { ["summary"]=NarrationKnowledgeDomainKey.ScientificStructure,["approximatePosition"]=NarrationKnowledgeDomainKey.PhysicalCharacteristics,["areaSquareDegrees"]=NarrationKnowledgeDomainKey.PhysicalCharacteristics,["relativeSizeNote"]=NarrationKnowledgeDomainKey.Scale,["majorStars"]=NarrationKnowledgeDomainKey.KeyObjects,["orionBeltStars"]=NarrationKnowledgeDomainKey.Recognition,["majorDeepSkyObjects"]=NarrationKnowledgeDomainKey.DeepSkyObjects,["neighboringConstellations"]=NarrationKnowledgeDomainKey.RecognitionGeometry,["astronomicalImportance"]=NarrationKnowledgeDomainKey.ScientificSignificance,["starFormationContext"]=NarrationKnowledgeDomainKey.StarFormation,["distanceCautions"]=NarrationKnowledgeDomainKey.Distance });
public sealed class AstronomicalObjectKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.objects.v1","objects",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.KeyObjects,"objectName"));
public sealed class ObservationKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.observation.v1","observation",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Observation,"northernHemisphere","southernHemisphere","seasonalVisibility","nakedEyeRecognition","orionBeltIdentification","urbanGuidance","darkSkyGuidance","binocularGuidance","telescopeGuidance","locationDependentWarning"));
public sealed class AstrophotographyKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.astrophotography.v1","astrophotography",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Astrophotography,"wideFieldSuitability","beltAndSwordFraming","orionNebulaFraming","equipmentCategories","seasonalPlanning","lightPollution","exposureCaution"));
public sealed class HistoryKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.history.v1","history",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.History,"ancientRecognition","historicalCataloguing","navigationSeasonalImportance","modernInterpretation"));
public sealed class CultureAndMythologyKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.culture.v1","cultureAndMythology",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.CultureAndMythology,"summary","rashiNote","nakshatraNote","uncertaintyNote")) { protected override bool AllowsContainer(string name)=>name is "greek" or "roman" or "indianHindu" or "chinese" or "arabic" or "other"; }
public sealed class RegionalTraditionKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.regional.v1","regionalTraditions",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.RegionalTraditions,"summary","qualification","tradition"));
public sealed class AstrologyClarificationKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.astrology.v1","astrologyRelationships",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.AstrologyClarification,"westernZodiacNotes","indianRashiNotes","nakshatraNotes","disclaimer"));
public sealed class EditorialSafetyKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.safety.v1","editorialSafety",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.EditorialSafety,"sourceConflicts","aiAssistedEditorialText"));
public sealed class LocalizedContentKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.localized.v1","localizedContent",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.LocalizedContent,"summary","keyMessages")) { protected override bool AllowsContainer(string name)=>name.Length is 2 or 5; }
public sealed class InterestingFactsKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.facts.v1","interestingFacts",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.InterestingFacts,"text"));
public sealed class TimingKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.timing.v1","timing",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Timing,"startUtc","endUtc","peakUtc","summary","approximation"));
public sealed class VisibilityKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.visibility.v1","visibility",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Visibility,"summary","location","conditions","altitude","direction"));
public sealed class GeometryKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.geometry.v1","geometry",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Geometry,"summary","separation","positionAngle","alignment"));
public sealed class MeteorShowerKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.meteor.v1","meteor",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.ActivityRate,"summary","radiant","peakRate","parentBody"));
public sealed class ConjunctionGroupingKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.conjunction.v1","conjunction",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Geometry,"summary","participants","separation"));
public sealed class OccultationKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.occultation.v1","occultation",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.ContactTimeline,"summary","contacts","visibilityFootprint"));
public sealed class TransitKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.transit.v1","transit",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.ContactTimeline,"summary","contacts","duration"));
public sealed class OppositionKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.opposition.v1","opposition",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Geometry,"summary","distance","visibility"));
public sealed class ElongationKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.elongation.v1","elongation",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Geometry,"summary","angle","direction","visibility"));
public sealed class CloseApproachKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.close-approach.v1","closeApproach",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Geometry,"summary","distance","relativeVelocity"));
public sealed class EclipseKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.eclipse.v1","eclipse",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.ContactTimeline,"summary","contacts","visibilityFootprint","magnitude"));
public sealed class CometKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.comet.v1","comet",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.Orbit,"summary","orbit","brightness","visibility"));
public sealed class SatellitePassKnowledgeAdapter() : ApprovedFieldKnowledgeAdapter("phase7.satellite.v1","satellite",Phase7ApprovedFields.Of(NarrationKnowledgeDomainKey.OrbitalMotion,"summary","rise","culmination","set","visibility"));
