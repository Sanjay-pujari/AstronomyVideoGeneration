using System.Globalization;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyEventVerificationService(
    IOptions<RenderingOptions> renderingOptions,
    TimeProvider timeProvider,
    ISkyfieldAccuracyProvider skyfieldAccuracyProvider,
    ILogger<AstronomyEventVerificationService> logger) : IAstronomyEventVerificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan MoonDeduplicationWindow = TimeSpan.FromHours(6);
    private static readonly string[] PlanetNames = ["Mercury", "Venus", "Mars", "Jupiter", "Saturn"];

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

        var skyfield = await TryComputeSkyfieldAccuracyAsync(request, preview.Events, cancellationToken);
        warnings.AddRange(skyfield.Warnings);

        var deduplicatedDrafts = DeduplicateMoonEvents(preview.Events, out var deduplicatedCount);
        var verified = deduplicatedDrafts
            .Select(d => ToVerifiedEvent(d, skyfield, request.RegionId))
            .ToList();

        var computedPlanetEvents = skyfield.PlanetPairings.Select(p => ToPlanetPairingEvent(p, request.RegionId)).ToArray();
        if (computedPlanetEvents.Length > 0)
        {
            verified.RemoveAll(e => e.SourceType.Equals("ManualSeed", StringComparison.OrdinalIgnoreCase)
                && (e.EventType.Equals("PlanetPairing", StringComparison.OrdinalIgnoreCase) || IsPlanetOnlyConjunction(e)));
            verified.AddRange(computedPlanetEvents);
        }

        verified = verified
            .OrderBy(e => e.PeakUtc)
            .ThenByDescending(e => e.ContentWorthinessScore)
            .ToList();

        var highPriorityCount = verified.Count(e => e.PublishPriority == "High");
        var manualReviewCount = verified.Count(e => e.VerificationStatus == "NeedsManualReview");
        var autoGenerateAllowedCount = verified.Count(e => e.AutoGenerateAllowed);
        var verifiedEventCount = verified.Count;
        var skyfieldVerifiedCount = verified.Count(e => e.VerificationSource == "Skyfield" && e.VerificationStatus == "Verified");
        var moonPhaseVerifiedCount = verified.Count(e => IsMoonPhaseEvent(e.EventType) && e.VerificationSource == "Skyfield" && e.VerificationStatus == "Verified");
        var planetPairingComputedCount = computedPlanetEvents.Length;
        var meteorMoonlightAdjustedCount = verified.Count(e => IsMeteorShower(e.EventType) && e.MoonIlluminationPercent.HasValue);
        if (skyfieldVerifiedCount == 0) warnings.Add("WARNING: skyfieldVerifiedCount remains 0; no events were promoted by Skyfield accuracy computations.");
        if (moonPhaseVerifiedCount == 0) warnings.Add("WARNING: moonPhaseVerifiedCount remains 0; exact Skyfield moon phase matching did not verify any preview moon events.");
        if (planetPairingComputedCount == 0) warnings.Add("WARNING: planetPairingComputedCount remains 0; no visible Skyfield planet pairings met the requested constraints.");
        if (meteorMoonlightAdjustedCount == 0) warnings.Add("WARNING: meteorMoonlightAdjustedCount remains 0; meteor shower moonlight was not adjusted.");

        var topEvents = verified
            .Where(e => e.ContentWorthinessScore >= 85
                && e.PublishPriority == "High"
                && e.AutoGenerateAllowed
                && !e.SourceType.Equals("ManualSeed", StringComparison.OrdinalIgnoreCase)
                && !e.VerificationStatus.Equals("NeedsManualReview", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.ContentWorthinessScore)
            .ThenBy(e => e.PeakUtc)
            .ToArray();

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
            SkyfieldVerifiedCount = skyfieldVerifiedCount,
            PlanetPairingComputedCount = planetPairingComputedCount,
            MoonPhaseVerifiedCount = moonPhaseVerifiedCount,
            MeteorMoonlightAdjustedCount = meteorMoonlightAdjustedCount,
            Events = verified,
            TopEvents = topEvents,
            EventTypeCounts = verified
                .GroupBy(e => e.EventType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            VerificationSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["inputEventCount"] = inputEventCount,
                ["verifiedEventCount"] = verifiedEventCount,
                ["deduplicatedCount"] = deduplicatedCount,
                ["skyfieldVerifiedCount"] = skyfieldVerifiedCount,
                ["manualReviewCount"] = manualReviewCount,
                ["autoGenerateAllowedCount"] = autoGenerateAllowedCount,
                ["highPriorityCount"] = highPriorityCount,
                ["moonPhaseVerifiedCount"] = moonPhaseVerifiedCount,
                ["planetPairingComputedCount"] = planetPairingComputedCount,
                ["meteorMoonlightAdjustedCount"] = meteorMoonlightAdjustedCount
            },
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
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
            skyfieldVerifiedCount,
            manualReviewCount,
            autoGenerateAllowedCount,
            planetPairingComputedCount,
            moonPhaseVerifiedCount,
            meteorMoonlightAdjustedCount,
            generatedFiles)
        { HighPriorityCount = highPriorityCount, Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    private async Task<SkyfieldAccuracyResult> TryComputeSkyfieldAccuracyAsync(AstronomyEventVerificationRequest request, IReadOnlyList<AstronomyEventPreviewItem> events, CancellationToken cancellationToken)
    {
        var region = ResolveRegion(request.RegionId);
        var result = new SkyfieldAccuracyResult();
        var moonPhases = await skyfieldAccuracyProvider.VerifyMoonPhasesAsync(request.Year, region, cancellationToken);
        var planetPairings = await skyfieldAccuracyProvider.ComputePlanetPairingsAsync(request.Year, region, cancellationToken);
        var meteorMoonlight = await skyfieldAccuracyProvider.AdjustMeteorMoonlightAsync(events, region, cancellationToken);

        result.MoonPhases.AddRange(moonPhases.MoonPhases);
        result.PlanetPairings.AddRange(planetPairings.PlanetPairings);
        result.MeteorMoonlight.AddRange(meteorMoonlight.MeteorMoonlight);
        result.Warnings.AddRange(moonPhases.Warnings);
        result.Warnings.AddRange(planetPairings.Warnings);
        result.Warnings.AddRange(meteorMoonlight.Warnings);
        return result;
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
                if (duplicate.Event.EventType.Equals("FullMoon", StringComparison.OrdinalIgnoreCase)) named.Aliases.Add("Full Moon");
                else named.SpecialTags.Add(duplicate.Event.EventType);
                named.SpecialTags.UnionWith(duplicate.SpecialTags);
                named.ContentWorthinessScore = Math.Max(named.ContentWorthinessScore, duplicate.Event.ContentWorthinessScore);
                named.VisibilityScore = Math.Max(named.VisibilityScore, duplicate.Event.VisibilityScore);
                named.RarityScore = Math.Max(named.RarityScore, duplicate.Event.RarityScore);
                named.PublicInterestScore = Math.Max(named.PublicInterestScore, duplicate.Event.PublicInterestScore);
                foreach (var warning in duplicate.Event.Warnings) named.Warnings.Add(warning);
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

    private static AstronomyEventVerifiedItem ToVerifiedEvent(AstronomyEventVerificationDraft draft, SkyfieldAccuracyResult skyfield, string regionId)
    {
        var sourceType = draft.Event.SourceType;
        var eventType = draft.Event.EventType;
        var verificationStatus = ResolveVerificationStatus(sourceType, eventType);
        var verificationSource = ResolveVerificationSource(sourceType, eventType);
        var peakUtc = draft.Event.PeakUtc;
        var startUtc = draft.Event.StartUtc;
        var endUtc = draft.Event.EndUtc;
        var localPeakTime = draft.Event.LocalPeakTime;
        var warnings = draft.Warnings.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? verificationNotes = null;
        var skyfieldComputed = default(bool?);
        var moonPhaseVerified = default(bool?);
        var phaseType = default(string);

        if (IsMoonPhaseEvent(eventType) && TryFindMoonPhase(skyfield, eventType, draft.Event.PeakUtc, out var moonPhase))
        {
            peakUtc = moonPhase.PeakUtc;
            startUtc = peakUtc.AddHours(-Math.Max(1, (draft.Event.PeakUtc - draft.Event.StartUtc).TotalHours));
            endUtc = peakUtc.AddHours(Math.Max(1, (draft.Event.EndUtc - draft.Event.PeakUtc).TotalHours));
            localPeakTime = moonPhase.LocalPeakTime;
            verificationStatus = "Verified";
            verificationSource = "Skyfield";
            verificationNotes = "Exact lunar phase instant computed with Skyfield almanac; preview mean-cycle timing was not silently promoted.";
            warnings.RemoveWhere(w => w.Contains("approximate", StringComparison.OrdinalIgnoreCase) || w.Contains("mean lunar", StringComparison.OrdinalIgnoreCase) || w.Contains("verify exact phase", StringComparison.OrdinalIgnoreCase));
            skyfieldComputed = true;
            moonPhaseVerified = true;
            phaseType = moonPhase.Phase;
        }
        else if (IsMoonPhaseEvent(eventType))
        {
            warnings.Add("Skyfield exact lunar phase instant unavailable; this moon timing remains Approximate.");
        }

        var visibilityType = ResolveVisibilityType(draft.Event);
        var localVisibilityConfirmed = visibilityType is "Local" or "Regional" && draft.Event.VisibilityScore >= 60;
        if (IsEclipse(eventType))
        {
            visibilityType = ResolveEclipseVisibilityType(draft.Event, regionId);
            localVisibilityConfirmed = false;
            verificationStatus = "NeedsManualReview";
            verificationSource = draft.Event.SourceType.Equals("ManualSeed", StringComparison.OrdinalIgnoreCase) ? "ManualSeed" : verificationSource;
            warnings.Add("Eclipse retained for manual review because exact local circumstances were not computed by this verification pass.");
        }

        var visibilityScore = Math.Clamp(draft.VisibilityScore, 0, 100);
        var moonIlluminationPercent = default(double?);
        var moonInterference = default(string);
        var bestViewingWindowLocal = default(string);
        var radiantVisibilityNote = default(string);
        if (IsMeteorShower(eventType))
        {
            var meteor = FindMeteorMoonlight(skyfield, draft.Event.PeakUtc);
            if (meteor is not null)
            {
                moonIlluminationPercent = Math.Round(meteor.MoonIlluminationPercent, 1);
                moonInterference = meteor.MoonInterference;
                bestViewingWindowLocal = meteor.BestViewingWindowLocal;
                radiantVisibilityNote = meteor.RadiantVisibilityNote;
                visibilityScore = Math.Clamp(visibilityScore + meteor.VisibilityScoreAdjustment, 0, 100);
            }
            else
            {
                warnings.Add("Moonlight impact could not be computed for this meteor shower; visibility score was not adjusted.");
            }
        }

        var publishPriority = ResolvePublishPriority(draft.Event, eventType, visibilityType);
        var autoGenerateAllowed = verificationStatus != "NeedsManualReview" && publishPriority != "Low" && visibilityType != "NotLocallyVisible";
        if (IsMoonPhaseEvent(eventType) && verificationSource != "Skyfield") autoGenerateAllowed = false;
        if (IsEclipse(eventType)) autoGenerateAllowed = false;
        var contentStrategy = ResolveContentStrategy(eventType, visibilityType, autoGenerateAllowed);

        return new AstronomyEventVerifiedItem
        {
            EventId = draft.Event.EventId,
            EventType = draft.Event.EventType,
            Title = draft.Event.Title,
            ShortTitle = draft.Event.ShortTitle,
            StartUtc = startUtc,
            PeakUtc = peakUtc,
            EndUtc = endUtc,
            LocalPeakTime = localPeakTime,
            VisibilityRegion = draft.Event.VisibilityRegion,
            PrimaryObjects = draft.Event.PrimaryObjects,
            SecondaryObjects = draft.Event.SecondaryObjects,
            SkyDirectionHint = draft.Event.SkyDirectionHint,
            ContentWorthinessScore = Math.Clamp(draft.ContentWorthinessScore, 0, 100),
            VisibilityScore = visibilityScore,
            RarityScore = Math.Clamp(draft.RarityScore, 0, 100),
            PublicInterestScore = Math.Clamp(draft.PublicInterestScore, 0, 100),
            Aliases = draft.Aliases.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray(),
            SpecialTags = draft.SpecialTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray(),
            RecommendedContentTypes = ResolveRecommendedContentTypes(eventType, publishPriority, autoGenerateAllowed, contentStrategy),
            RecommendedPublishWindow = draft.Event.RecommendedPublishWindow,
            SourceType = draft.Event.SourceType,
            SourceNotes = draft.Event.SourceNotes,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            VerificationStatus = verificationStatus,
            VerificationSource = verificationSource,
            VerificationNotes = verificationNotes ?? ResolveVerificationNotes(verificationStatus, verificationSource, eventType),
            VisibilityType = visibilityType,
            LocalVisibilityConfirmed = localVisibilityConfirmed,
            LocalVisibilityNotes = ResolveLocalVisibilityNotes(draft.Event, visibilityType, localVisibilityConfirmed),
            PublishPriority = publishPriority,
            AutoGenerateAllowed = autoGenerateAllowed,
            ContentStrategy = contentStrategy,
            MoonIlluminationPercent = moonIlluminationPercent,
            MoonInterference = moonInterference,
            BestViewingWindowLocal = bestViewingWindowLocal,
            RadiantVisibilityNote = radiantVisibilityNote,
            SkyfieldComputed = skyfieldComputed,
            MoonPhaseVerified = moonPhaseVerified,
            PhaseType = phaseType
        };
    }

    private static AstronomyEventVerifiedItem ToPlanetPairingEvent(SkyfieldPlanetPairing pairing, string regionId)
    {
        var score = PairingScore(pairing.AngularSeparationDegrees);
        var publishPriority = score >= 85 ? "High" : "Medium";
        var eventType = pairing.AngularSeparationDegrees <= 3 ? "PlanetPairing" : "Conjunction";
        var title = $"{pairing.PrimaryObject} and {pairing.SecondaryObject} Close Pairing";
        var eventId = $"skyfield-planet-pairing-{pairing.PrimaryObject}-{pairing.SecondaryObject}-{pairing.PeakUtc:yyyyMMddHHmm}".ToLowerInvariant();
        var item = new AstronomyEventVerifiedItem
        {
            EventId = eventId,
            EventType = eventType,
            Title = title,
            ShortTitle = $"{pairing.PrimaryObject}-{pairing.SecondaryObject}",
            StartUtc = pairing.PeakUtc.AddHours(-4),
            PeakUtc = pairing.PeakUtc,
            EndUtc = pairing.PeakUtc.AddHours(4),
            LocalPeakTime = pairing.BestViewingLocalTime,
            VisibilityRegion = regionId,
            PrimaryObjects = [pairing.PrimaryObject, pairing.SecondaryObject],
            SecondaryObjects = [],
            SkyDirectionHint = string.IsNullOrWhiteSpace(pairing.SkyDirectionHint) ? "Skyfield-computed close bright-planet pairing; use local altitudes and direction from the verification fields." : pairing.SkyDirectionHint,
            ContentWorthinessScore = score,
            VisibilityScore = Math.Clamp((int)Math.Round(Math.Min(pairing.ObjectAltitudesDegrees.Values.DefaultIfEmpty(8).Min() * 1.8, 95)), 0, 100),
            RarityScore = pairing.AngularSeparationDegrees <= 1.5 ? 86 : pairing.AngularSeparationDegrees <= 3 ? 76 : 62,
            PublicInterestScore = pairing.InvolvesBrightPlanet ? 92 : 78,
            Aliases = [pairing.Quality],
            SpecialTags = ["SkyfieldComputed", "BrightPlanets"],
            RecommendedContentTypes = score >= 70 ? ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"] : [],
            RecommendedPublishWindow = new RecommendedPublishWindow(pairing.PeakUtc.AddDays(-7), pairing.PeakUtc.AddHours(-2)),
            SourceType = "Computed",
            SourceNotes = "Generated by verification endpoint from Skyfield apparent topocentric alt/az separation search.",
            Warnings = [],
            VerificationStatus = "Verified",
            VerificationSource = "Skyfield",
            VerificationNotes = "Both planets met altitude and twilight constraints from the requested region.",
            VisibilityType = "Local",
            LocalVisibilityConfirmed = true,
            LocalVisibilityNotes = "Both planets altitude >= 8° and Sun altitude <= -6° at best viewing sample.",
            PublishPriority = publishPriority,
            AutoGenerateAllowed = score >= 70,
            ContentStrategy = "LocalViewingGuide",
            AngularSeparationDegrees = Math.Round(pairing.AngularSeparationDegrees, 2),
            ObjectAltitudesDegrees = pairing.ObjectAltitudesDegrees.ToDictionary(k => k.Key, v => Math.Round(v.Value, 1), StringComparer.OrdinalIgnoreCase),
            SunAltitudeDegrees = Math.Round(pairing.SunAltitudeDegrees, 1),
            BestViewingLocalTime = pairing.BestViewingLocalTime,
            SkyfieldComputed = true
        };
        item.RecommendedContentTypes = ResolveRecommendedContentTypes(item.EventType, item.PublishPriority, item.AutoGenerateAllowed, item.ContentStrategy);
        return item;
    }

    private static int PairingScore(double separation) => separation <= 1.5 ? 92 : separation <= 3 ? 84 : 72;

    private static bool TryFindMoonPhase(SkyfieldAccuracyResult skyfield, string eventType, DateTimeOffset approximatePeak, out SkyfieldMoonPhase phase)
    {
        var desired = eventType.Equals("NewMoon", StringComparison.OrdinalIgnoreCase) ? "NewMoon" : "FullMoon";
        phase = skyfield.MoonPhases
            .Where(p => p.Phase.Equals(desired, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => (p.PeakUtc - approximatePeak).Duration())
            .FirstOrDefault()!;
        return phase is not null && (phase.PeakUtc - approximatePeak).Duration() <= TimeSpan.FromHours(48);
    }

    private static SkyfieldMeteorMoonlight? FindMeteorMoonlight(SkyfieldAccuracyResult skyfield, DateTimeOffset approximatePeak) =>
        skyfield.MeteorMoonlight
            .OrderBy(m => (m.PeakUtc - approximatePeak).Duration())
            .FirstOrDefault(m => (m.PeakUtc - approximatePeak).Duration() <= TimeSpan.FromDays(2));

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

    private static string ResolveEclipseVisibilityType(AstronomyEventPreviewItem e, string regionId)
    {
        if (regionId.Equals("IN-RJ-UDAIPUR", StringComparison.OrdinalIgnoreCase) && e.EventType.Equals("SolarEclipse", StringComparison.OrdinalIgnoreCase)) return "NotLocallyVisible";
        if (e.Warnings.Any(w => w.Contains("not a local", StringComparison.OrdinalIgnoreCase) || w.Contains("below horizon", StringComparison.OrdinalIgnoreCase))) return "NotLocallyVisible";
        return e.VisibilityScore >= 70 ? "Regional" : "Global";
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

    private RegionScheduleOptions ResolveRegion(string regionId)
    {
        var normalized = regionId.Trim().ToLowerInvariant();
        if (normalized is "in-rj-udaipur" or "india-udaipur")
        {
            return new RegionScheduleOptions { RegionId = regionId, DisplayName = "Udaipur, India", Latitude = 24.5854, Longitude = 73.7125, Timezone = "Asia/Kolkata", Language = "en", Enabled = true };
        }

        return new RegionScheduleOptions { RegionId = regionId, DisplayName = regionId, Latitude = 0, Longitude = 0, Timezone = "UTC", Language = "en", Enabled = true };
    }

    private string BuildOutputDirectory(string regionId, int year)
    {
        var root = string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
        return Path.Combine(root, "assets", SanitizePathSegment(regionId), "event-discovery", year.ToString(CultureInfo.InvariantCulture));
    }

    private static void Validate(AstronomyEventVerificationRequest request)
    {
        if (request.Year is < 1900 or > 2100) throw new ArgumentOutOfRangeException(nameof(request.Year), "year must be between 1900 and 2100.");
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("regionId is required.", nameof(request));
    }

    private static bool IsMoonPhaseEvent(string eventType) => eventType.Equals("FullMoon", StringComparison.OrdinalIgnoreCase) || eventType.Equals("NewMoon", StringComparison.OrdinalIgnoreCase) || eventType.Equals("NamedFullMoon", StringComparison.OrdinalIgnoreCase) || eventType.Equals("BlueMoon", StringComparison.OrdinalIgnoreCase) || eventType.Equals("Supermoon", StringComparison.OrdinalIgnoreCase);
    private static bool IsMeteorShower(string eventType) => eventType.Contains("Meteor", StringComparison.OrdinalIgnoreCase);
    private static bool IsEclipse(string eventType) => eventType.Contains("Eclipse", StringComparison.OrdinalIgnoreCase);
    private static bool IsPlanetOnlyConjunction(AstronomyEventVerifiedItem e) => e.EventType.Equals("Conjunction", StringComparison.OrdinalIgnoreCase) && e.PrimaryObjects.All(o => PlanetNames.Contains(o, StringComparer.OrdinalIgnoreCase));
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
        public int SkyfieldVerifiedCount { get; init; }
        public int ManualReviewCount { get; init; }
        public int AutoGenerateAllowedCount { get; init; }
        public int HighPriorityCount { get; init; }
        public int MoonPhaseVerifiedCount { get; init; }
        public int PlanetPairingComputedCount { get; init; }
        public int MeteorMoonlightAdjustedCount { get; init; }
        public IReadOnlyList<AstronomyEventVerifiedItem> Events { get; init; } = [];
        public IReadOnlyList<AstronomyEventVerifiedItem> TopEvents { get; init; } = [];
        public IReadOnlyDictionary<string, int> EventTypeCounts { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> VerificationSummary { get; init; } = new Dictionary<string, int>();
        public IReadOnlyList<string> Warnings { get; init; } = [];
        public DateTimeOffset GeneratedUtc { get; init; }
    }

    private sealed class AstronomyEventVerifiedItem
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ShortTitle { get; set; } = string.Empty;
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset PeakUtc { get; set; }
        public DateTimeOffset EndUtc { get; set; }
        public string LocalPeakTime { get; set; } = string.Empty;
        public string VisibilityRegion { get; set; } = string.Empty;
        public IReadOnlyList<string> PrimaryObjects { get; set; } = [];
        public IReadOnlyList<string> SecondaryObjects { get; set; } = [];
        public string SkyDirectionHint { get; set; } = string.Empty;
        public int ContentWorthinessScore { get; set; }
        public int VisibilityScore { get; set; }
        public int RarityScore { get; set; }
        public int PublicInterestScore { get; set; }
        public IReadOnlyList<string> Aliases { get; set; } = [];
        public IReadOnlyList<string> SpecialTags { get; set; } = [];
        public IReadOnlyList<string> RecommendedContentTypes { get; set; } = [];
        public RecommendedPublishWindow RecommendedPublishWindow { get; set; } = new(DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        public string SourceType { get; set; } = string.Empty;
        public string SourceNotes { get; set; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; set; } = [];
        public string VerificationStatus { get; set; } = string.Empty;
        public string VerificationSource { get; set; } = string.Empty;
        public string VerificationNotes { get; set; } = string.Empty;
        public string VisibilityType { get; set; } = string.Empty;
        public bool LocalVisibilityConfirmed { get; set; }
        public string LocalVisibilityNotes { get; set; } = string.Empty;
        public string PublishPriority { get; set; } = string.Empty;
        public bool AutoGenerateAllowed { get; set; }
        public string ContentStrategy { get; set; } = string.Empty;
        public double? AngularSeparationDegrees { get; set; }
        public IReadOnlyDictionary<string, double>? ObjectAltitudesDegrees { get; set; }
        public double? SunAltitudeDegrees { get; set; }
        public string? BestViewingLocalTime { get; set; }
        public bool? SkyfieldComputed { get; set; }
        public bool? MoonPhaseVerified { get; set; }
        public string? PhaseType { get; set; }
        public double? MoonIlluminationPercent { get; set; }
        public string? MoonInterference { get; set; }
        public string? BestViewingWindowLocal { get; set; }
        public string? RadiantVisibilityNote { get; set; }
    }

}
