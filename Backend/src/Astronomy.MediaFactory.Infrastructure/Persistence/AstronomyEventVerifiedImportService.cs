using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyEventVerifiedImportService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<AstronomyEventVerifiedImportService> logger) : IAstronomyEventVerifiedImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<AstronomyEventVerifiedImportResponse> ImportVerifiedEventsAsync(AstronomyEventVerifiedImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var inputPath = BuildInputPath(request.RegionId, request.Year);
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Verified astronomy events JSON was not found at '{inputPath}'.", inputPath);

        await using var stream = File.OpenRead(inputPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var events = ReadEvents(document.RootElement).ToArray();
        var language = NormalizeLanguage(request.Language);
        var now = DateTimeOffset.UtcNow;

        var externalIds = events.Select(e => e.ExternalEventId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingEventRows = await db.AstronomyEventIntelligences
            .Include(e => e.Objects)
            .Where(e => e.Year == request.Year && e.RegionId == request.RegionId && e.Language == language && externalIds.Contains(e.ExternalEventId))
            .ToListAsync(cancellationToken);
        var existingEvents = existingEventRows.ToDictionary(e => e.ExternalEventId, StringComparer.OrdinalIgnoreCase);

        var existingPlans = request.CreateContentPlans
            ? await db.ContentGenerationPlans
                .Where(p => p.RegionId == request.RegionId && p.Language == language && p.SourceExternalEventId != null && externalIds.Contains(p.SourceExternalEventId))
                .Select(p => new PlanKey(p.SourceExternalEventId!, p.RegionId, p.Language, p.ContentCategoryCode, p.PlannedFormat ?? string.Empty))
                .ToListAsync(cancellationToken)
            : new List<PlanKey>();
        var planKeys = existingPlans.ToHashSet();

        var inserted = 0;
        var updated = 0;
        var plansCreated = 0;
        var plansSkipped = 0;
        var highPriorityPlans = 0;
        var manualReviewEvents = events.Count(e => IsManualReview(e));
        var autoGenerateAllowedEvents = events.Count(e => e.AutoGenerateAllowed);

        foreach (var source in events)
        {
            if (string.IsNullOrWhiteSpace(source.ExternalEventId))
                throw new ArgumentException("Every verified event must include a non-empty eventId.");

            var rawJson = source.Raw.GetRawText();
            var metadataJson = BuildEventMetadataJson(source, inputPath, now);

            var isNewEntity = !existingEvents.TryGetValue(source.ExternalEventId, out var entity);
            if (isNewEntity)
            {
                entity = new AstronomyEventIntelligence();
                ApplyEvent(source, entity, request.Year, request.RegionId, language, rawJson, metadataJson);
                ReplaceObjects(entity, source);
                if (!request.DryRun)
                    db.AstronomyEventIntelligences.Add(entity);
                existingEvents[source.ExternalEventId] = entity;
                inserted++;
            }
            else if (request.OverwriteExisting)
            {
                ApplyEvent(source, entity!, request.Year, request.RegionId, language, rawJson, metadataJson);
                ReplaceObjects(entity!, source);
                updated++;
            }
            else if (entity!.Objects.Count == 0)
            {
                ReplaceObjects(entity, source);
            }

            if (!request.CreateContentPlans)
            {
                plansSkipped++;
                continue;
            }

            if (!CanCreatePlan(source))
            {
                plansSkipped++;
                continue;
            }

            var category = ResolveContentCategoryCode(source);
            var format = source.RecommendedContentTypes.FirstOrDefault() ?? string.Empty;
            var key = new PlanKey(source.ExternalEventId, request.RegionId, language, category, format);
            if (planKeys.Contains(key))
            {
                plansSkipped++;
                continue;
            }

            if (!request.DryRun)
                db.ContentGenerationPlans.Add(CreatePlan(source, entity!, request.RegionId, language, category, format, now));
            planKeys.Add(key);
            plansCreated++;
            if (source.PublishPriority.Equals("High", StringComparison.OrdinalIgnoreCase))
                highPriorityPlans++;
        }

        if (!request.DryRun)
            await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Imported {EventCount} verified astronomy events from {InputPath}. Inserted={Inserted} Updated={Updated} PlansCreated={PlansCreated} DryRun={DryRun}", events.Length, inputPath, inserted, updated, plansCreated, request.DryRun);

        return new AstronomyEventVerifiedImportResponse(
            request.Year,
            request.RegionId,
            events.Length,
            inserted,
            updated,
            plansCreated,
            plansSkipped,
            manualReviewEvents,
            autoGenerateAllowedEvents,
            highPriorityPlans,
            request.DryRun,
            []);
    }

    private static ImportedVerifiedEvent[] ReadEvents(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Select(ImportedVerifiedEvent.FromJson).ToArray();
        if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            return events.EnumerateArray().Select(ImportedVerifiedEvent.FromJson).ToArray();
        throw new ArgumentException("Verified astronomy events JSON must be an array or contain an events array.");
    }

    private static void ApplyEvent(ImportedVerifiedEvent source, AstronomyEventIntelligence entity, int year, string regionId, string language, string rawJson, string metadataJson)
    {
        entity.EventCode = BuildEventCode(source.ExternalEventId, year, regionId, language);
        entity.ExternalEventId = source.ExternalEventId;
        entity.Year = year;
        entity.RegionId = regionId;
        entity.Language = language;
        entity.EventType = source.EventType;
        entity.Title = source.Title;
        entity.Summary = source.ShortTitle;
        entity.Description = source.SourceNotes;
        entity.StartUtc = source.StartUtc;
        entity.PeakUtc = source.PeakUtc;
        entity.EndUtc = source.EndUtc;
        entity.ContentOpportunityScore = source.ContentWorthinessScore;
        entity.VisibilityScore = source.VisibilityScore;
        entity.RarityScore = source.RarityScore;
        entity.AudienceInterestScore = source.PublicInterestScore;
        entity.VerificationStatus = source.VerificationStatus;
        entity.AutoGenerateAllowed = source.AutoGenerateAllowed;
        entity.ContentStrategy = source.ContentStrategy;
        entity.RecommendedCategory = ResolveContentCategoryCode(source);
        entity.Status = "Verified";
        entity.RawDataJson = rawJson;
        entity.MetadataJson = metadataJson;
        entity.Touch();
    }

    private void ReplaceObjects(AstronomyEventIntelligence entity, ImportedVerifiedEvent source)
    {
        if (entity.Objects.Count > 0)
            db.AstronomyEventObjects.RemoveRange(entity.Objects);
        entity.Objects.Clear();
        foreach (var name in source.PrimaryObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            entity.Objects.Add(new AstronomyEventObject { ObjectName = name, ObjectType = "CelestialObject", ObjectRole = "Primary", MetadataJson = JsonSerializer.Serialize(new { source = "primaryObjects" }, JsonOptions) });
        foreach (var name in source.SecondaryObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            entity.Objects.Add(new AstronomyEventObject { ObjectName = name, ObjectType = "CelestialObject", ObjectRole = "Secondary", MetadataJson = JsonSerializer.Serialize(new { source = "secondaryObjects" }, JsonOptions) });
    }

    private static ContentGenerationPlan CreatePlan(ImportedVerifiedEvent source, AstronomyEventIntelligence entity, string regionId, string language, string category, string format, DateTimeOffset now)
    {
        var scheduledUtc = source.RecommendedPublishStartUtc ?? source.PeakUtc.AddDays(-7);
        var priority = ResolvePriority(source.PublishPriority);
        var eventDetails = new
        {
            importedUtc = now,
            sourceExternalEventId = source.ExternalEventId,
            sourceTitle = source.Title,
            sourceEventType = source.EventType,
            sourceVerificationStatus = source.VerificationStatus,
            sourceContentStrategy = source.ContentStrategy,
            sourcePublishPriority = source.PublishPriority,
            recommendedPublishStartUtc = source.RecommendedPublishStartUtc,
            recommendedContentTypes = source.RecommendedContentTypes
        };

        return new ContentGenerationPlan
        {
            AstronomyEventIntelligenceId = entity.Id,
            SourceExternalEventId = source.ExternalEventId,
            RegionId = regionId,
            Language = language,
            Status = "Draft",
            PlanStatus = "Draft",
            ContentCategoryCode = category,
            PlannedFormat = format,
            Title = source.Title,
            Priority = priority,
            PriorityScore = source.ContentWorthinessScore,
            RequestedOutputTypesJson = JsonSerializer.Serialize(source.RecommendedContentTypes, JsonOptions),
            PlannedObjectNamesJson = JsonSerializer.Serialize(source.PrimaryObjects.Concat(source.SecondaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions),
            AssetPlanJson = JsonSerializer.Serialize(eventDetails, JsonOptions),
            ScheduledUtc = scheduledUtc,
            PrimaryAstronomyEventTypeCode = source.EventType,
            PrimaryCelestialObjectCode = source.PrimaryObjects.FirstOrDefault(),
            PlanningReason = "Imported from verified astronomy-event JSON."
        };
    }

    private static bool CanCreatePlan(ImportedVerifiedEvent source)
        => source.AutoGenerateAllowed
           && !IsManualReview(source)
           && !source.ContentStrategy.Equals("SkipAutoGeneration", StringComparison.OrdinalIgnoreCase)
           && !source.ContentStrategy.Equals("EducationalOnly", StringComparison.OrdinalIgnoreCase)
           && source.RecommendedContentTypes.Count > 0;

    private static bool IsManualReview(ImportedVerifiedEvent source)
        => source.VerificationStatus.Equals("NeedsManualReview", StringComparison.OrdinalIgnoreCase);

    private static string ResolveContentCategoryCode(ImportedVerifiedEvent source)
    {
        if (source.EventType.Equals("PLANET_GROUPING", StringComparison.OrdinalIgnoreCase)
            || source.EventType.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase))
        {
            return "PlanetGrouping";
        }

        return source.PublishPriority.Equals("High", StringComparison.OrdinalIgnoreCase) ? "RareEventAlert" : "CosmicStoryShort";
    }

    private static int ResolvePriority(string publishPriority)
        => publishPriority.Equals("High", StringComparison.OrdinalIgnoreCase) ? 10 : publishPriority.Equals("Medium", StringComparison.OrdinalIgnoreCase) ? 50 : 100;

    private static string BuildEventCode(string externalEventId, int year, string regionId, string language)
    {
        var code = $"{year}-{regionId}-{language}-{externalEventId}".Replace(' ', '-');
        if (code.Length <= 160)
            return code;

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)))[..12].ToLowerInvariant();
        return $"{code[..147]}-{hash}";
    }

    private static string BuildEventMetadataJson(ImportedVerifiedEvent source, string inputPath, DateTimeOffset importedUtc)
        => JsonSerializer.Serialize(new
        {
            importedUtc,
            inputPath,
            sourceType = source.SourceType,
            sourceNotes = source.SourceNotes,
            localPeakTime = source.LocalPeakTime,
            visibilityRegion = source.VisibilityRegion,
            skyDirectionHint = source.SkyDirectionHint,
            publishPriority = source.PublishPriority,
            recommendedContentTypes = source.RecommendedContentTypes,
            recommendedPublishStartUtc = source.RecommendedPublishStartUtc,
            recommendedPublishEndUtc = source.RecommendedPublishEndUtc
        }, JsonOptions);

    private string BuildInputPath(string regionId, int year)
    {
        var root = string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
        return Path.Combine(root, "assets", regionId, "event-discovery", year.ToString(), $"astronomy-event-verified-{year}.json");
    }

    private static void Validate(AstronomyEventVerifiedImportRequest request)
    {
        if (request.Year < 1900 || request.Year > 2200) throw new ArgumentException("Year must be between 1900 and 2200.");
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("RegionId is required.");
    }

    private static string NormalizeLanguage(string? language) => string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private readonly record struct PlanKey(string SourceExternalEventId, string RegionId, string Language, string ContentCategoryCode, string PlannedFormat);

    private sealed record ImportedVerifiedEvent(
        JsonElement Raw,
        string ExternalEventId,
        string EventType,
        string Title,
        string ShortTitle,
        DateTimeOffset StartUtc,
        DateTimeOffset PeakUtc,
        DateTimeOffset EndUtc,
        string LocalPeakTime,
        string VisibilityRegion,
        IReadOnlyList<string> PrimaryObjects,
        IReadOnlyList<string> SecondaryObjects,
        string SkyDirectionHint,
        decimal ContentWorthinessScore,
        decimal VisibilityScore,
        decimal RarityScore,
        decimal PublicInterestScore,
        IReadOnlyList<string> RecommendedContentTypes,
        DateTimeOffset? RecommendedPublishStartUtc,
        DateTimeOffset? RecommendedPublishEndUtc,
        string SourceType,
        string SourceNotes,
        string VerificationStatus,
        string PublishPriority,
        bool AutoGenerateAllowed,
        string ContentStrategy)
    {
        public static ImportedVerifiedEvent FromJson(JsonElement e)
            => new(
                e.Clone(),
                GetString(e, "eventId"),
                GetString(e, "eventType"),
                GetString(e, "title"),
                GetString(e, "shortTitle"),
                GetDate(e, "startUtc"),
                GetDate(e, "peakUtc"),
                GetDate(e, "endUtc"),
                GetString(e, "localPeakTime"),
                GetString(e, "visibilityRegion"),
                GetStringArray(e, "primaryObjects"),
                GetStringArray(e, "secondaryObjects"),
                GetString(e, "skyDirectionHint"),
                GetDecimal(e, "contentWorthinessScore"),
                GetDecimal(e, "visibilityScore"),
                GetDecimal(e, "rarityScore"),
                GetDecimal(e, "publicInterestScore"),
                GetStringArray(e, "recommendedContentTypes"),
                GetNestedDate(e, "recommendedPublishWindow", "publishStartUtc"),
                GetNestedDate(e, "recommendedPublishWindow", "publishEndUtc"),
                GetString(e, "sourceType"),
                GetString(e, "sourceNotes"),
                GetString(e, "verificationStatus"),
                GetString(e, "publishPriority"),
                GetBool(e, "autoGenerateAllowed"),
                GetString(e, "contentStrategy"));

        private static string GetString(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : string.Empty;
        private static bool GetBool(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
        private static decimal GetDecimal(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.TryGetDecimal(out var value) ? value : 0m;
        private static DateTimeOffset GetDate(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.TryGetDateTimeOffset(out var value) ? value : DateTimeOffset.MinValue;
        private static DateTimeOffset? GetNestedDate(JsonElement e, string objectName, string propertyName)
            => e.TryGetProperty(objectName, out var obj) && obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propertyName, out var p) && p.TryGetDateTimeOffset(out var value) ? value : null;
        private static IReadOnlyList<string> GetStringArray(JsonElement e, string name)
            => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                : [];
    }
}
