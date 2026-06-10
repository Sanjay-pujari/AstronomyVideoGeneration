using System.Diagnostics;
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

        var skyfield = await TryComputeSkyfieldAccuracyAsync(request, cancellationToken);
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
        { HighPriorityCount = highPriorityCount };
    }

    private async Task<SkyfieldAccuracyResult> TryComputeSkyfieldAccuracyAsync(AstronomyEventVerificationRequest request, CancellationToken cancellationToken)
    {
        var region = ResolveRegion(request.RegionId);
        var result = new SkyfieldAccuracyResult();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"astronomy-event-skyfield-{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(scriptPath, SkyfieldScript, cancellationToken);
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add(request.Year.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(region.Latitude.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(region.Longitude.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(region.Timezone);
            process.StartInfo.ArgumentList.Add(FindEphemerisPath() ?? string.Empty);
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                result.Warnings.Add($"Skyfield computation failed; keeping approximate/manual statuses where applicable. {TrimWarning(stderr)}");
                return result;
            }

            var computed = JsonSerializer.Deserialize<SkyfieldAccuracyResult>(stdout, JsonOptions);
            if (computed is null)
            {
                result.Warnings.Add("Skyfield computation returned no usable JSON; keeping approximate/manual statuses where applicable.");
                return result;
            }

            return computed;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or OperationCanceledException or JsonException)
        {
            result.Warnings.Add($"Skyfield computation unavailable; keeping approximate/manual statuses where applicable. {ex.Message}");
            return result;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static string? FindEphemerisPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Backend", "python", "skyfield_sidecar", "de421.bsp"),
            Path.Combine(Directory.GetCurrentDirectory(), "Backend", "python", "skyfield_sidecar", "de421.bsp"),
            Path.Combine(Directory.GetCurrentDirectory(), "python", "skyfield_sidecar", "de421.bsp")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string TrimWarning(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "No stderr details were provided.";
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 240 ? text : text[..240] + "…";
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

        if (IsMoonPhaseEvent(eventType) && TryFindMoonPhase(skyfield, eventType, draft.Event.PeakUtc, out var moonPhase))
        {
            peakUtc = moonPhase.PeakUtc;
            startUtc = peakUtc.AddHours(-Math.Max(1, (draft.Event.PeakUtc - draft.Event.StartUtc).TotalHours));
            endUtc = peakUtc.AddHours(Math.Max(1, (draft.Event.EndUtc - draft.Event.PeakUtc).TotalHours));
            localPeakTime = moonPhase.LocalPeakTime;
            verificationStatus = "Verified";
            verificationSource = "Skyfield";
            verificationNotes = "Exact lunar phase instant computed with Skyfield almanac; preview mean-cycle timing was not silently promoted.";
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
            RadiantVisibilityNote = radiantVisibilityNote
        };
    }

    private static AstronomyEventVerifiedItem ToPlanetPairingEvent(SkyfieldPlanetPairing pairing, string regionId)
    {
        var score = PairingScore(pairing.AngularSeparationDegrees);
        var publishPriority = score >= 85 ? "High" : "Medium";
        var title = $"{pairing.PrimaryObject} and {pairing.SecondaryObject} Close Pairing";
        var eventId = $"skyfield-planet-pairing-{pairing.PrimaryObject}-{pairing.SecondaryObject}-{pairing.PeakUtc:yyyyMMddHHmm}".ToLowerInvariant();
        var item = new AstronomyEventVerifiedItem
        {
            EventId = eventId,
            EventType = "PlanetPairing",
            Title = title,
            ShortTitle = $"{pairing.PrimaryObject}-{pairing.SecondaryObject}",
            StartUtc = pairing.PeakUtc.AddHours(-4),
            PeakUtc = pairing.PeakUtc,
            EndUtc = pairing.PeakUtc.AddHours(4),
            LocalPeakTime = pairing.BestViewingLocalTime,
            VisibilityRegion = regionId,
            PrimaryObjects = [pairing.PrimaryObject, pairing.SecondaryObject],
            SecondaryObjects = [],
            SkyDirectionHint = "Skyfield-computed close bright-planet pairing; use local altitudes and direction from the verification fields.",
            ContentWorthinessScore = score,
            VisibilityScore = Math.Clamp((int)Math.Round(Math.Min(pairing.ObjectAltitudesDegrees.Values.DefaultIfEmpty(8).Min() * 1.8, 95)), 0, 100),
            RarityScore = pairing.AngularSeparationDegrees <= 1.5 ? 86 : pairing.AngularSeparationDegrees <= 3 ? 76 : 62,
            PublicInterestScore = pairing.InvolvesBrightPlanet ? 92 : 78,
            Aliases = [pairing.Quality],
            SpecialTags = ["SkyfieldComputed", "BrightPlanets"],
            RecommendedContentTypes = score >= 70 ? ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"] : [],
            RecommendedPublishWindow = new RecommendedPublishWindow(pairing.PeakUtc.AddDays(-7), pairing.PeakUtc.AddHours(-2)),
            SourceType = "SkyfieldComputed",
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
        return phase is not null && (phase.PeakUtc - approximatePeak).Duration() <= TimeSpan.FromDays(2.5);
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
        public double? MoonIlluminationPercent { get; set; }
        public string? MoonInterference { get; set; }
        public string? BestViewingWindowLocal { get; set; }
        public string? RadiantVisibilityNote { get; set; }
    }

    private sealed class SkyfieldAccuracyResult
    {
        public List<SkyfieldPlanetPairing> PlanetPairings { get; set; } = [];
        public List<SkyfieldMoonPhase> MoonPhases { get; set; } = [];
        public List<SkyfieldMeteorMoonlight> MeteorMoonlight { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    private sealed class SkyfieldPlanetPairing
    {
        public string PrimaryObject { get; set; } = string.Empty;
        public string SecondaryObject { get; set; } = string.Empty;
        public DateTimeOffset PeakUtc { get; set; }
        public double AngularSeparationDegrees { get; set; }
        public Dictionary<string, double> ObjectAltitudesDegrees { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public double SunAltitudeDegrees { get; set; }
        public string BestViewingLocalTime { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public bool InvolvesBrightPlanet { get; set; }
    }

    private sealed class SkyfieldMoonPhase
    {
        public string Phase { get; set; } = string.Empty;
        public DateTimeOffset PeakUtc { get; set; }
        public string LocalPeakTime { get; set; } = string.Empty;
    }

    private sealed class SkyfieldMeteorMoonlight
    {
        public DateTimeOffset PeakUtc { get; set; }
        public double MoonIlluminationPercent { get; set; }
        public string MoonInterference { get; set; } = string.Empty;
        public int VisibilityScoreAdjustment { get; set; }
        public string BestViewingWindowLocal { get; set; } = string.Empty;
        public string RadiantVisibilityNote { get; set; } = string.Empty;
    }

    private const string SkyfieldScript = """
import json, sys, math
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo
try:
    from skyfield import almanac
    from skyfield.api import load, wgs84
except Exception as e:
    raise SystemExit('Skyfield import failed: %s' % e)
year=int(sys.argv[1]); lat=float(sys.argv[2]); lon=float(sys.argv[3]); tz=ZoneInfo(sys.argv[4]); eph_path=sys.argv[5]
ts=load.timescale(); eph=load(eph_path or 'de421.bsp')
earth=eph['earth']; sun=eph['sun']; observer=earth+wgs84.latlon(lat, lon)
planets={'Mercury':'mercury','Venus':'venus','Mars':'mars','Jupiter':'jupiter barycenter','Saturn':'saturn barycenter'}
out={'planetPairings':[], 'moonPhases':[], 'meteorMoonlight':[], 'warnings':[]}

def iso(dt): return dt.astimezone(timezone.utc).isoformat().replace('+00:00','Z')
def local(dt): return dt.astimezone(tz).strftime('%Y-%m-%d %H:%M %z')
def quality(sep): return 'Excellent' if sep <= 1.5 else ('Good' if sep <= 3 else 'Broad grouping')
def moon_illum(dt):
    try: return float(almanac.fraction_illuminated(eph, 'moon', ts.from_datetime(dt))) * 100.0
    except Exception:
        phase=float(almanac.moon_phase(eph, ts.from_datetime(dt)).degrees)
        return (1-math.cos(math.radians(phase)))/2*100
# Moon phases
try:
    t0=ts.utc(year,1,1); t1=ts.utc(year+1,1,1)
    times, phases = almanac.find_discrete(t0, t1, almanac.moon_phases(eph))
    names=['NewMoon','FirstQuarter','FullMoon','LastQuarter']
    for t,p in zip(times,phases):
        if names[int(p)] in ('NewMoon','FullMoon'):
            dt=t.utc_datetime().replace(tzinfo=timezone.utc)
            out['moonPhases'].append({'phase':names[int(p)], 'peakUtc':iso(dt), 'localPeakTime':local(dt)})
except Exception as e:
    out['warnings'].append('Skyfield moon phase computation failed; approximate moon events were not promoted. %s' % e)
# Planet pairings sampled every two hours; close samples are clustered by pair/date.
try:
    samples=[]; start=datetime(year,1,1,tzinfo=timezone.utc); end=datetime(year+1,1,1,tzinfo=timezone.utc); dt=start
    while dt < end:
        t=ts.from_datetime(dt)
        sun_alt=(observer.at(t).observe(sun).apparent()).altaz()[0].degrees
        if sun_alt <= -6:
            apparent={}
            for name,key in planets.items():
                app=observer.at(t).observe(eph[key]).apparent(); alt,az,d=app.altaz()
                if alt.degrees >= 8: apparent[name]=(app, alt.degrees)
            names=list(apparent.keys())
            for i in range(len(names)):
                for j in range(i+1,len(names)):
                    a,b=names[i],names[j]; sep=apparent[a][0].separation_from(apparent[b][0]).degrees
                    if sep <= 6:
                        samples.append((a,b,dt,sep,apparent[a][1],apparent[b][1],sun_alt))
        dt += timedelta(hours=2)
    best={}
    for a,b,dt,sep,aa,bb,sa in samples:
        key=(a,b,dt.astimezone(tz).strftime('%Y-%m-%d'))
        if key not in best or sep < best[key][3]: best[key]=(a,b,dt,sep,aa,bb,sa)
    # de-dupe adjacent local-date clusters for each pair
    chosen=[]
    for pair in sorted(set((k[0],k[1]) for k in best)):
        vals=sorted([v for k,v in best.items() if k[:2]==pair], key=lambda x:x[2])
        cluster=[]
        for v in vals:
            if not cluster or (v[2]-cluster[-1][2]).total_seconds() <= 36*3600: cluster.append(v)
            else:
                chosen.append(min(cluster, key=lambda x:x[3])); cluster=[v]
        if cluster: chosen.append(min(cluster, key=lambda x:x[3]))
    bright={'Venus','Jupiter'}
    for a,b,dt,sep,aa,bb,sa in sorted(chosen, key=lambda x:(x[2],x[3])):
        out['planetPairings'].append({'primaryObject':a,'secondaryObject':b,'peakUtc':iso(dt),'angularSeparationDegrees':sep,'objectAltitudesDegrees':{a:aa,b:bb},'sunAltitudeDegrees':sa,'bestViewingLocalTime':local(dt),'quality':quality(sep),'involvesBrightPlanet':a in bright or b in bright})
except Exception as e:
    out['warnings'].append('Skyfield planet-pairing computation failed; ManualSeed planet events were not replaced. %s' % e)
# Meteor moonlight for common calendar-rule peaks used by preview.
for month,day in [(1,4),(4,22),(5,6),(7,30),(8,12),(10,21),(11,17),(12,14)]:
    dt=datetime(year,month,day,18,0,0,tzinfo=timezone.utc)
    illum=moon_illum(dt)
    interference='Low' if illum < 35 else ('Medium' if illum < 70 else 'High')
    adj=8 if interference=='Low' else (-6 if interference=='Medium' else -18)
    out['meteorMoonlight'].append({'peakUtc':iso(dt),'moonIlluminationPercent':illum,'moonInterference':interference,'visibilityScoreAdjustment':adj,'bestViewingWindowLocal':'Post-midnight to pre-dawn local time when radiant is highest and twilight is absent.','radiantVisibilityNote':'Moonlight estimate computed for the rule-based peak; exact radiant altitude model is not asserted.'})
print(json.dumps(out))
""";
}
