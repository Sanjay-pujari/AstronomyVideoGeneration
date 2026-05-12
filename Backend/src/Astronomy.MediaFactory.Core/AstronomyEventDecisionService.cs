using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class AstronomyEventDecisionService : IAstronomyEventDecisionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IAstronomyEventDiscoveryService _discovery;
    private readonly IAstronomyEventStore? _store;
    private readonly AstronomyEventsOptions _options;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ILogger<AstronomyEventDecisionService> _logger;

    public AstronomyEventDecisionService(
        IAstronomyEventDiscoveryService discovery,
        IOptions<AstronomyEventsOptions> options,
        IOptions<MaintenanceOptions> maintenanceOptions,
        ILogger<AstronomyEventDecisionService> logger,
        IAstronomyEventStore? store = null)
    {
        _discovery = discovery;
        _options = options.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _logger = logger;
        _store = store;
    }

    public async Task<EventContentDecision> DecideAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var normalizedRegion = NormalizeRegion(regionId);
        try
        {
            var events = (await _discovery.GetTopEventsAsync(normalizedRegion, targetDate, cancellationToken))
                .Where(e => e.ContentOpportunityScore >= _options.MinimumContentOpportunityScore)
                .OrderByDescending(e => e.ContentOpportunityScore)
                .ThenBy(e => e.PeakUtc ?? e.StartUtc)
                .ToArray();

            var skipped = new List<AstronomyEvent>();
            var injectable = new List<AstronomyEvent>();
            var special = new List<AstronomyEvent>();

            foreach (var item in events)
            {
                var duplicate = _store is not null && await _store.HasGenerationHistoryAsync(item.EventId, normalizedRegion, targetDate, ContentType.SpecialEventGuide, cancellationToken);
                if (duplicate)
                {
                    skipped.Add(item);
                    continue;
                }

                if (item.ContentOpportunityScore >= _options.MajorEventThreshold && _options.EnableSpecialEventVideos && special.Count < _options.MaxSpecialEventVideosPerDay)
                    special.Add(item);
                if (item.ContentOpportunityScore >= _options.MediumEventThreshold && _options.EnableDailyGuideEventInjection && injectable.Count < _options.MaxInjectedEventsPerDailyGuide)
                    injectable.Add(item);
            }

            var primary = special.FirstOrDefault() ?? injectable.FirstOrDefault() ?? events.FirstOrDefault();
            var decisionType = primary is null || primary.ContentOpportunityScore < _options.MediumEventThreshold
                ? (primary is null ? "None" : "MentionOnly")
                : special.Count > 0 ? "GenerateBoth" : "InjectIntoDailyGuide";

            var decision = new EventContentDecision
            {
                HasEvent = primary is not null,
                PrimaryEvent = primary,
                DecisionType = decisionType,
                InjectedEvents = decisionType == "GenerateBoth" && primary is not null ? [primary] : injectable,
                SpecialEventCandidates = special,
                SkippedEvents = skipped,
                Reason = primary is null
                    ? $"No astronomy event met score { _options.MinimumContentOpportunityScore:0.00 } for {normalizedRegion} on {targetDate:yyyy-MM-dd}."
                    : $"Top event score {primary.ContentOpportunityScore:0.00}; decision={decisionType}."
            };

            await WriteDecisionDiagnosticsAsync(normalizedRegion, targetDate, decision, cancellationToken);
            return decision;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astronomy event decision failed for {RegionId} on {TargetDate}; continuing normal DailySkyGuide.", normalizedRegion, targetDate);
            var failSafe = new EventContentDecision { DecisionType = "None", Reason = $"Event decision failed: {ex.Message}" };
            await WriteDecisionDiagnosticsAsync(normalizedRegion, targetDate, failSafe, cancellationToken);
            return failSafe;
        }
    }

    private async Task WriteDecisionDiagnosticsAsync(string regionId, DateOnly targetDate, EventContentDecision decision, CancellationToken cancellationToken)
    {
        var directory = string.IsNullOrWhiteSpace(_maintenanceOptions.WorkingDirectory) ? "./media-output" : _maintenanceOptions.WorkingDirectory;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "event-content-decision.json"), JsonSerializer.Serialize(new
        {
            regionId,
            targetDate,
            decision.DecisionType,
            decision.HasEvent,
            selectedEvent = decision.PrimaryEvent,
            injectedEvents = decision.InjectedEvents,
            specialEventCandidates = decision.SpecialEventCandidates,
            skippedEvents = decision.SkippedEvents,
            decision.Reason
        }, JsonOptions), cancellationToken);
    }

    private static string NormalizeRegion(string regionId)
        => string.IsNullOrWhiteSpace(regionId) ? "default" : regionId.Trim().ToLowerInvariant().Replace(' ', '-');
}
