using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Adapts the existing certified event-intelligence repository to the Phase 7 boundary.</summary>
public sealed class Phase7CertifiedKnowledgeSource(MediaFactoryDbContext db) : IPhase7CertifiedKnowledgeSource
{
    public async Task<CertifiedKnowledgePayload?> ResolveAsync(string eventId, string language, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var item = await db.AstronomyEventIntelligences.AsNoTracking().Include(x=>x.ReferenceSources)
            .FirstOrDefaultAsync(x => (x.ExternalEventId == eventId || x.EventCode == eventId || x.Id.ToString() == eventId)
                && x.Language == language, token);
        if (item is null) return null;
        var family = FamilyIdentifier(item.EventType, item.RecommendedCategory, item.Title);
        var sources = item.ReferenceSources.Select(x=>x.Id.ToString()).Where(x=>!string.IsNullOrWhiteSpace(x)).Order(StringComparer.Ordinal).ToArray();
        return new(item.Id.ToString(), eventId, family, item.EventType, item.Language, item.RawDataJson ?? "",
            item.MetadataJson, null, $"event-source-registry-{item.Id:N}", sources, item.VerificationStatus);
    }
    private static string FamilyIdentifier(string eventType,string category,string title)
    {
        var value=$"{eventType} {category} {title}".ToUpperInvariant();
        foreach(var family in new[]{"CONSTELLATION","METEOR_SHOWER","LUNAR_PHASE","ISS_PASS","CLOSE_APPROACH","DEEP_SKY_OBJECT","STAR_FORMING_REGION","CONJUNCTION","GROUPING","OCCULTATION","TRANSIT","OPPOSITION","ELONGATION","ECLIPSE","COMET","SATELLITE","GALAXY","NEBULA","CLUSTER","PLANET","MOON","STAR"})
            if(value.Contains(family.Replace('_',' '),StringComparison.Ordinal)||value.Contains(family,StringComparison.Ordinal))return family;
        return EventFamilyResolver.Resolve(eventType,category,[],[],title).ToString().ToUpperInvariant() switch { "PLANETGROUPING"=>"GROUPING","SPECIALEVENT"=>eventType.ToUpperInvariant(),var x=>x };
    }
}
