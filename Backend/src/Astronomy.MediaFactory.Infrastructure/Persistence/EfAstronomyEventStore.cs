using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EfAstronomyEventStore : IAstronomyEventStore
{
    private readonly MediaFactoryDbContext _db;

    public EfAstronomyEventStore(MediaFactoryDbContext db) => _db = db;

    public async Task UpsertEventsAsync(IReadOnlyCollection<AstronomyEvent> events, CancellationToken cancellationToken)
    {
        foreach (var item in events)
        {
            item.TargetDate = item.TargetDate == default ? DateOnly.FromDateTime((item.PeakUtc ?? item.StartUtc).UtcDateTime) : item.TargetDate;
            item.Touch();
            var existing = await _db.AstronomyEvents.FirstOrDefaultAsync(x => x.EventId == item.EventId, cancellationToken);
            if (existing is null)
            {
                item.Touch();
                await _db.AstronomyEvents.AddAsync(item, cancellationToken);
                continue;
            }

            existing.EventType = item.EventType;
            existing.Title = item.Title;
            existing.Description = item.Description;
            existing.StartUtc = item.StartUtc;
            existing.PeakUtc = item.PeakUtc;
            existing.EndUtc = item.EndUtc;
            existing.TargetDate = item.TargetDate;
            existing.RegionId = item.RegionId;
            existing.LocationName = item.LocationName;
            existing.Latitude = item.Latitude;
            existing.Longitude = item.Longitude;
            existing.Timezone = item.Timezone;
            existing.GlobalVisibility = item.GlobalVisibility;
            existing.VisibilityRegions = item.VisibilityRegions;
            existing.RelatedObjects = item.RelatedObjects;
            existing.Source = item.Source;
            existing.ConfidenceScore = item.ConfidenceScore;
            existing.RarityScore = item.RarityScore;
            existing.VisibilityScore = item.VisibilityScore;
            existing.AudienceInterestScore = item.AudienceInterestScore;
            existing.TimingUrgencyScore = item.TimingUrgencyScore;
            existing.ContentOpportunityScore = item.ContentOpportunityScore;
            existing.RecommendedContentType = item.RecommendedContentType;
            existing.Status = item.Status;
            existing.Touch();
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AstronomyEvent>> GetUpcomingAsync(DateOnly fromDate, DateOnly toDate, string? regionId, CancellationToken cancellationToken)
    {
        var events = await _db.AstronomyEvents.AsNoTracking()
            .Where(x => x.TargetDate >= fromDate && x.TargetDate <= toDate)
            .OrderBy(x => x.TargetDate)
            .ThenByDescending(x => x.ContentOpportunityScore)
            .ToListAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(regionId))
            return events;

        return events.Where(x => x.GlobalVisibility || x.RegionId == null || x.RegionId == regionId || x.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public Task<AstronomyEvent?> GetByEventIdAsync(string eventId, CancellationToken cancellationToken)
        => _db.AstronomyEvents.AsNoTracking().FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);

    public async Task<bool> HasGenerationHistoryAsync(string eventId, string regionId, DateOnly targetDate, ContentType contentType, CancellationToken cancellationToken)
    {
        var eventGuid = await _db.AstronomyEvents.AsNoTracking().Where(x => x.EventId == eventId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (eventGuid is null)
            return false;
        var contentTypeName = contentType.ToString();
        return await _db.AstronomyEventGenerationHistory.AnyAsync(x => x.AstronomyEventId == eventGuid.Value && x.RegionId == regionId && x.TargetDate == targetDate && x.ContentType == contentTypeName, cancellationToken);
    }

    public async Task AddGenerationHistoryAsync(Guid astronomyEventId, Guid pipelineRunId, string regionId, DateOnly targetDate, ContentType contentType, string generationMode, CancellationToken cancellationToken)
    {
        var contentTypeName = contentType.ToString();
        if (await _db.AstronomyEventGenerationHistory.AnyAsync(x => x.AstronomyEventId == astronomyEventId && x.RegionId == regionId && x.TargetDate == targetDate && x.ContentType == contentTypeName, cancellationToken))
            return;

        await _db.AstronomyEventGenerationHistory.AddAsync(new AstronomyEventGenerationHistory
        {
            AstronomyEventId = astronomyEventId,
            PipelineRunId = pipelineRunId,
            RegionId = regionId,
            TargetDate = targetDate,
            ContentType = contentTypeName,
            GenerationMode = generationMode
        }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
