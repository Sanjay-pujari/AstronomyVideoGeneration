using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyEventVerificationService(
    IOptions<RenderingOptions> renderingOptions,
    TimeProvider timeProvider,
    ILogger<AstronomyEventVerificationService> logger) : IAstronomyEventVerificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan MoonDeduplicationWindow = TimeSpan.FromHours(6);

    public async Task<AstronomyEventVerificationResponse> VerifyAstronomyEventsAsync(AstronomyEventVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var outputDirectory = BuildOutputDirectory(request.RegionId, request.Year);
        var inputPath = Path.Combine(outputDirectory, $"astronomy-event-preview-{request.Year}.json");
        var outputPath = Path.Combine(outputDirectory, $"astronomy-event-verified-{request.Year}.json");

        if (!File.Exists(inputPath))
        {
            throw new ArgumentException($"Astronomy event preview file was not found: {inputPath}", nameof(request));
        }

        var preview = JsonSerializer.Deserialize<AstronomyEventPreviewDocument>(await File.ReadAllTextAsync(inputPath, cancellationToken), JsonOptions)
            ?? throw new ArgumentException($"Astronomy event preview file is not valid JSON: {inputPath}", nameof(request));

        var inputEventCount = preview.Events.Count;
        var normalizedLanguage = NormalizeLanguage(request.Language);
        var warnings = preview.Warnings.ToList();
        warnings.Add("Verification V1 classifies preview events for editorial review and does not create database records or production assets.");
        warnings.Add("ManualSeed events remain NeedsManualReview until checked against an authoritative ephemeris or external reference.");

        var deduplicated = DeduplicateMoonEvents(preview.Events, out var deduplicatedCount)
            .Select(ToVerifiedEvent)
            .OrderBy(e => e.PeakUtc)
            .ThenByDescending(e => e.ContentWorthinessScore)
            .ToArray();

        var highPriorityCount = deduplicated.Count(e => e.PublishPriority == "High");
        var manualReviewCount = deduplicated.Count(e => e.VerificationStatus == "NeedsManualReview");
        var autoGenerateAllowedCount = deduplicated.Count(e => e.AutoGenerateAllowed);
        var verifiedEventCount = deduplicated.Length;
        var verifiedStatusCount = deduplicated.Count(e => e.VerificationStatus == "Verified");

        var document = new AstronomyEventVerifiedDocument
        {
            Year = request.Year,
            RegionId = request.RegionId,
            Language = normalizedLanguage,
            InputEventCount = inputEventCount,
            VerifiedEventCount = verifiedEventCount,
            DeduplicatedCount = deduplicatedCount,
            HighPriorityCount = highPriorityCount,
            ManualReviewCount = manualReviewCount,
            AutoGenerateAllowedCount = autoGenerateAllowedCount,
            Events = deduplicated,
            TopEvents = deduplicated
                .Where(e => e.ContentWorthinessScore >= 85 && e.PublishPriority == "High" && e.AutoGenerateAllowed)
                .OrderByDescending(e => e.ContentWorthinessScore)
                .ThenBy(e => e.PeakUtc)
                .ToArray(),
            EventTypeCounts = deduplicated
                .GroupBy(e => e.EventType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            VerificationSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["inputEventCount"] = inputEventCount,
                ["verifiedEventCount"] = verifiedEventCount,
                ["deduplicatedCount"] = deduplicatedCount,
                ["highPriorityCount"] = highPriorityCount,
                ["manualReviewCount"] = manualReviewCount,
                ["autoGenerateAllowedCount"] = autoGenerateAllowedCount,
                ["verifiedStatusCount"] = verifiedStatusCount,
                ["approximateCount"] = deduplicated.Count(e => e.VerificationStatus == "Approximate")
            },
            Warnings = warnings,
            GeneratedUtc = timeProvider.GetUtcNow()
        };

        var generatedFiles = new List<string>();
        var generated = false;
        if (File.Exists(outputPath) && !request.OverwriteExisting)
        {
            logger.LogInformation("Astronomy event verification already exists at {OutputPath}; overwriteExisting=false.", outputPath);
        }
        else if (!request.DryRun)
        {
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(document, JsonOptions), cancellationToken);
            generatedFiles.Add(outputPath);
            generated = true;
            logger.LogInformation("Astronomy event verification generated at {OutputPath} with {EventCount} events from {InputEventCount} preview events.", outputPath, document.Events.Count, inputEventCount);
        }

        return new AstronomyEventVerificationResponse(
            request.Year,
            request.RegionId,
            generated,
            outputPath,
            inputEventCount,
            verifiedEventCount,
            deduplicatedCount,
            highPriorityCount,
            manualReviewCount,
            autoGenerateAllowedCount,
            generatedFiles);
    }

    private static IReadOnlyList<AstronomyEventVerificationDraft> DeduplicateMoonEvents(IReadOnlyList<AstronomyEventPreviewItem> events, out int deduplicatedCount)
    {
        var drafts = events.Select(e => new AstronomyEventVerificationDraft(e)).ToList();
        deduplicatedCount = 0;

        foreach (var named in drafts.Where(d => d.Event.EventType.Equals("NamedFullMoon", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var duplicates = drafts
                .Where(d => !ReferenceEquals(d, named)
                    && IsMergeableMoonDuplicate(d.Event.EventType)
                    && IsNearSamePeak(named.Event.PeakUtc, d.Event.PeakUtc))
                .ToArray();

            foreach (var duplicate in duplicates)
            {
                if (duplicate.Event.EventType.Equals("FullMoon", StringComparison.OrdinalIgnoreCase))
                {
                    named.Aliases.Add("Full Moon");
                }
                else
                {
                    named.SpecialTags.Add(duplicate.Event.EventType);
                }

                named.SpecialTags.UnionWith(duplicate.SpecialTags);
                named.ContentWorthinessScore = Math.Max(named.ContentWorthinessScore, duplicate.Event.ContentWorthinessScore);
                named.VisibilityScore = Math.Max(named.VisibilityScore, duplicate.Event.VisibilityScore);
                named.RarityScore = Math.Max(named.RarityScore, duplicate.Event.RarityScore);
                named.PublicInterestScore = Math.Max(named.PublicInterestScore, duplicate.Event.PublicInterestScore);
                foreach (var warning in duplicate.Event.Warnings)
                {
                    named.Warnings.Add(warning);
                }

                drafts.Remove(duplicate);
                deduplicatedCount++;
            }
        }

        return drafts;
    }

    private static bool IsMergeableMoonDuplicate(string eventType) =>
        eventType.Equals("FullMoon", StringComparison.OrdinalIgnoreCase)
        || eventType.Equals("BlueMoon", StringComparison.OrdinalIgnoreCase)
        || eventType.Equals("Supermoon", StringComparison.OrdinalIgnoreCase);

    private static bool IsNearSamePeak(DateTimeOffset first, DateTimeOffset second) =>
        (first - second).Duration() <= MoonDeduplicationWindow;

    private static AstronomyEventVerifiedItem ToVerifiedEvent(AstronomyEventVerificationDraft draft)
    {
        var sourceType = draft.Event.SourceType;
        var eventType = draft.Event.EventType;
        var verificationStatus = ResolveVerificationStatus(sourceType, eventType);
        var verificationSource = ResolveVerificationSource(sourceType, eventType);
        var visibilityType = ResolveVisibilityType(draft.Event);
        var localVisibilityConfirmed = visibilityType is "Local" or "Regional" && draft.Event.VisibilityScore >= 60;
        var publishPriority = ResolvePublishPriority(draft.Event, eventType, visibilityType);
        var autoGenerateAllowed = verificationStatus != "NeedsManualReview" && publishPriority != "Low" && visibilityType != "NotLocallyVisible";
        var contentStrategy = ResolveContentStrategy(eventType, visibilityType, autoGenerateAllowed);

        return new AstronomyEventVerifiedItem
        {
            EventId = draft.Event.EventId,
            EventType = draft.Event.EventType,
            Title = draft.Event.Title,
            ShortTitle = draft.Event.ShortTitle,
            StartUtc = draft.Event.StartUtc,
            PeakUtc = draft.Event.PeakUtc,
            EndUtc = draft.Event.EndUtc,
            LocalPeakTime = draft.Event.LocalPeakTime,
            VisibilityRegion = draft.Event.VisibilityRegion,
            PrimaryObjects = draft.Event.PrimaryObjects,
            SecondaryObjects = draft.Event.SecondaryObjects,
            SkyDirectionHint = draft.Event.SkyDirectionHint,
            ContentWorthinessScore = Math.Clamp(draft.ContentWorthinessScore, 0, 100),
            VisibilityScore = Math.Clamp(draft.VisibilityScore, 0, 100),
            RarityScore = Math.Clamp(draft.RarityScore, 0, 100),
            PublicInterestScore = Math.Clamp(draft.PublicInterestScore, 0, 100),
            Aliases = draft.Aliases.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray(),
            SpecialTags = draft.SpecialTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray(),
            RecommendedContentTypes = ResolveRecommendedContentTypes(eventType, publishPriority, autoGenerateAllowed, contentStrategy),
            RecommendedPublishWindow = draft.Event.RecommendedPublishWindow,
            SourceType = draft.Event.SourceType,
            SourceNotes = draft.Event.SourceNotes,
            Warnings = draft.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            VerificationStatus = verificationStatus,
            VerificationSource = verificationSource,
            VerificationNotes = ResolveVerificationNotes(verificationStatus, verificationSource, eventType),
            VisibilityType = visibilityType,
            LocalVisibilityConfirmed = localVisibilityConfirmed,
            LocalVisibilityNotes = ResolveLocalVisibilityNotes(draft.Event, visibilityType, localVisibilityConfirmed),
            PublishPriority = publishPriority,
            AutoGenerateAllowed = autoGenerateAllowed,
            ContentStrategy = contentStrategy
        };
    }

    private static string ResolveVerificationStatus(string sourceType, string eventType)
    {
        if (sourceType.Equals("ManualSeed", StringComparison.OrdinalIgnoreCase)) return "NeedsManualReview";
        if (sourceType.Equals("KnownCalendarRule", StringComparison.OrdinalIgnoreCase)) return "Approximate";
        return eventType.Contains("Moon", StringComparison.OrdinalIgnoreCase) ? "Approximate" : "NeedsManualReview";
    }

    private static string ResolveVerificationSource(string sourceType, string eventType)
    {
        if (sourceType.Equals("ManualSeed", StringComparison.OrdinalIgnoreCase)) return "ManualSeed";
        if (sourceType.Equals("KnownCalendarRule", StringComparison.OrdinalIgnoreCase)) return "KnownCalendarRule";
        return eventType.Contains("Moon", StringComparison.OrdinalIgnoreCase) ? "ExternalReferencePending" : "ExternalReferencePending";
    }

    private static string ResolveVerificationNotes(string status, string source, string eventType)
    {
        if (status == "NeedsManualReview") return "Manual seed or unverified event retained for editorial review; exact local circumstances are not asserted.";
        if (eventType.Contains("Moon", StringComparison.OrdinalIgnoreCase)) return "Moon phase timing remains approximate from preview calculations; verify exact phase instant before precision publishing.";
        if (source == "KnownCalendarRule") return "Known annual calendar rule is suitable for content planning, but exact peak timing remains approximate.";
        return string.Empty;
    }

    private static string ResolveVisibilityType(AstronomyEventPreviewItem e)
    {
        if (e.EventType.Equals("SolarEclipse", StringComparison.OrdinalIgnoreCase) && e.Warnings.Any(w => w.Contains("Not a local", StringComparison.OrdinalIgnoreCase))) return "NotLocallyVisible";
        if (e.VisibilityScore >= 70) return "Local";
        if (e.VisibilityScore >= 55) return "Regional";
        return e.VisibilityRegion.Contains("Global", StringComparison.OrdinalIgnoreCase) ? "Global" : "NotLocallyVisible";
    }

    private static string ResolveLocalVisibilityNotes(AstronomyEventPreviewItem e, string visibilityType, bool confirmed)
    {
        if (confirmed) return $"Preview visibility score {e.VisibilityScore} supports local/regional planning for {e.VisibilityRegion}.";
        if (visibilityType == "Global") return "Global/public-interest event; local viewing circumstances are not confirmed.";
        if (visibilityType == "NotLocallyVisible") return "Not confirmed as locally visible from the requested region in this verification pass.";
        return "Regional visibility is plausible but local conditions require review.";
    }

    private static string ResolvePublishPriority(AstronomyEventPreviewItem e, string eventType, string visibilityType)
    {
        if (eventType.Equals("NewMoon", StringComparison.OrdinalIgnoreCase)) return "Low";
        if (e.ContentWorthinessScore >= 85) return "High";
        if (eventType.Equals("NamedFullMoon", StringComparison.OrdinalIgnoreCase)) return "Medium";
        if (visibilityType == "NotLocallyVisible" && e.ContentWorthinessScore < 90) return "Low";
        return e.ContentWorthinessScore >= 70 ? "Medium" : "Low";
    }

    private static string ResolveContentStrategy(string eventType, string visibilityType, bool autoGenerateAllowed)
    {
        if (eventType.Equals("NewMoon", StringComparison.OrdinalIgnoreCase)) return "EducationalOnly";
        if (!autoGenerateAllowed && visibilityType == "NotLocallyVisible") return "GlobalAstronomyNews";
        if (!autoGenerateAllowed) return "SkipAutoGeneration";
        if (visibilityType is "Local" or "Regional") return "LocalViewingGuide";
        return "GlobalAstronomyNews";
    }

    private static IReadOnlyList<string> ResolveRecommendedContentTypes(string eventType, string publishPriority, bool autoGenerateAllowed, string contentStrategy)
    {
        if (contentStrategy == "EducationalOnly") return ["EducationalOnly"];
        if (!autoGenerateAllowed) return [];
        if (publishPriority == "High") return ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"];
        if (publishPriority == "Medium" && eventType.Equals("NamedFullMoon", StringComparison.OrdinalIgnoreCase)) return ["ShortVideo", "HeroAsset", "Thumbnail"];
        if (publishPriority == "Medium") return ["ShortVideo", "HeroAsset", "Thumbnail"];
        return [];
    }

    private string BuildOutputDirectory(string regionId, int year)
    {
        var root = string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
        return Path.Combine(root, "assets", SanitizePathSegment(regionId), "event-discovery", year.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Validate(AstronomyEventVerificationRequest request)
    {
        if (request.Year is < 1900 or > 2100) throw new ArgumentOutOfRangeException(nameof(request.Year), "year must be between 1900 and 2100.");
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
    }

    private static string NormalizeLanguage(string? language) => string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
    private static string SanitizePathSegment(string value) => string.Concat(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));

    private sealed class AstronomyEventVerificationDraft(AstronomyEventPreviewItem eventItem)
    {
        public AstronomyEventPreviewItem Event { get; } = eventItem;
        public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SpecialTags { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Warnings { get; } = eventItem.Warnings.ToHashSet(StringComparer.OrdinalIgnoreCase);
        public int ContentWorthinessScore { get; set; } = eventItem.ContentWorthinessScore;
        public int VisibilityScore { get; set; } = eventItem.VisibilityScore;
        public int RarityScore { get; set; } = eventItem.RarityScore;
        public int PublicInterestScore { get; set; } = eventItem.PublicInterestScore;
    }

    private sealed class AstronomyEventVerifiedDocument
    {
        public int Year { get; init; }
        public string RegionId { get; init; } = string.Empty;
        public string Language { get; init; } = "en";
        public int InputEventCount { get; init; }
        public int VerifiedEventCount { get; init; }
        public int DeduplicatedCount { get; init; }
        public int HighPriorityCount { get; init; }
        public int ManualReviewCount { get; init; }
        public int AutoGenerateAllowedCount { get; init; }
        public IReadOnlyList<AstronomyEventVerifiedItem> Events { get; init; } = [];
        public IReadOnlyList<AstronomyEventVerifiedItem> TopEvents { get; init; } = [];
        public IReadOnlyDictionary<string, int> EventTypeCounts { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> VerificationSummary { get; init; } = new Dictionary<string, int>();
        public IReadOnlyList<string> Warnings { get; init; } = [];
        public DateTimeOffset GeneratedUtc { get; init; }
    }

    private sealed class AstronomyEventVerifiedItem
    {
        public string EventId { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string ShortTitle { get; init; } = string.Empty;
        public DateTimeOffset StartUtc { get; init; }
        public DateTimeOffset PeakUtc { get; init; }
        public DateTimeOffset EndUtc { get; init; }
        public string LocalPeakTime { get; init; } = string.Empty;
        public string VisibilityRegion { get; init; } = string.Empty;
        public IReadOnlyList<string> PrimaryObjects { get; init; } = [];
        public IReadOnlyList<string> SecondaryObjects { get; init; } = [];
        public string SkyDirectionHint { get; init; } = string.Empty;
        public int ContentWorthinessScore { get; init; }
        public int VisibilityScore { get; init; }
        public int RarityScore { get; init; }
        public int PublicInterestScore { get; init; }
        public IReadOnlyList<string> Aliases { get; init; } = [];
        public IReadOnlyList<string> SpecialTags { get; init; } = [];
        public IReadOnlyList<string> RecommendedContentTypes { get; init; } = [];
        public RecommendedPublishWindow RecommendedPublishWindow { get; init; } = new(DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        public string SourceType { get; init; } = string.Empty;
        public string SourceNotes { get; init; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; init; } = [];
        public string VerificationStatus { get; init; } = string.Empty;
        public string VerificationSource { get; init; } = string.Empty;
        public string VerificationNotes { get; init; } = string.Empty;
        public string VisibilityType { get; init; } = string.Empty;
        public bool LocalVisibilityConfirmed { get; init; }
        public string LocalVisibilityNotes { get; init; } = string.Empty;
        public string PublishPriority { get; init; } = string.Empty;
        public bool AutoGenerateAllowed { get; init; }
        public string ContentStrategy { get; init; } = string.Empty;
    }
}
