using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyAssetPlanningService(
    MediaFactoryDbContext db,
    ILogger<AstronomyAssetPlanningService> logger) : IAstronomyAssetPlanningService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private const string PlannedStatus = "Planned";
    private static readonly string[] RunnablePlanStatuses = ["Planned", "Approved"];

    private static readonly IReadOnlyDictionary<string, string[]> SceneTemplates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["RareEventAlert"] = ["Hook", "What is happening", "When and where to look", "Why it matters", "CTA / reminder"],
        ["PlanetConjunction"] = ["Hook with object pair", "Sky map / where to look", "Closest approach / peak timing", "Why conjunction happens", "Viewing tips", "Myth/story/cultural context optional", "Recap"],
        ["PlanetGrouping"] = ["Hook", "Objects involved", "Sky direction and timing", "Constellation guide", "Viewing tips", "Weekly relevance"],
        ["WeeklySkyForecast"] = ["Weekly hook", "Top sky highlight", "Moon phase sequence", "Planet visibility", "Grouping/conjunction highlight", "Best viewing nights", "Outro"],
        ["MoonSpecials"] = ["Moon phase hook", "Illumination and timing", "How to observe/photograph", "Cultural/story angle", "Outro"]
    };

    public async Task<AstronomyAssetPlanningResult> GenerateAssetPlansAsync(AstronomyAssetPlanningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var warnings = new List<string>();
        var planIds = request.PlanIds is { Count: > 0 } ? request.PlanIds.Where(x => x != Guid.Empty).ToHashSet() : null;
        var categories = ToSet(request.ContentCategories);
        var formats = ToSet(request.PlannedFormats);

        var schemaSupportsSave = await SchemaSupportsAssetPlanAsync(cancellationToken);
        if (!schemaSupportsSave)
            warnings.Add("content_generation_plans has no suitable asset plan JSON/status fields. Proposed minimal schema: asset_plan_json jsonb nullable; asset_plan_status varchar default 'Planned'. No asset plans were saved.");

        var query = db.ContentGenerationPlans
            .AsQueryable()
            .Where(p => RunnablePlanStatuses.Contains(p.PlanStatus));

        if (!string.IsNullOrWhiteSpace(request.RegionId))
            query = query.Where(p => p.RegionId == request.RegionId);
        if (planIds is { Count: > 0 })
            query = query.Where(p => planIds.Contains(p.Id));
        if (categories is { Count: > 0 })
            query = query.Where(p => categories.Contains(p.ContentCategoryCode));
        if (formats is { Count: > 0 })
            query = query.Where(p => p.PlannedFormat != null && formats.Contains(p.PlannedFormat));
        if (request.MinPriorityScore.HasValue)
            query = query.Where(p => p.PriorityScore >= request.MinPriorityScore.Value);

        query = query.OrderByDescending(p => p.PriorityScore ?? 0m).ThenBy(p => p.ScheduledUtc ?? DateTimeOffset.MaxValue);
        if (request.MaxPlans.HasValue)
            query = query.Take(request.MaxPlans.Value);

        var plans = schemaSupportsSave || !db.Database.IsRelational()
            ? await query.Include(p => p.AstronomyEventIntelligence).ToListAsync(cancellationToken)
            : await LoadPlansWithoutAssetColumnsAsync(query, cancellationToken);

        var assetPlans = new List<AstronomyAssetPlanDto>();
        var savedCount = 0;
        var skippedDuplicates = 0;

        foreach (var plan in plans)
        {
            var existingAssetPlanJson = schemaSupportsSave ? await GetExistingAssetPlanJsonAsync(plan, cancellationToken) : plan.AssetPlanJson;
            var hasReusableExisting = HasAssetRequirements(existingAssetPlanJson);
            if (!request.DryRun && schemaSupportsSave && hasReusableExisting && !request.OverwriteExisting)
            {
                skippedDuplicates++;
                continue;
            }

            if (!request.DryRun && schemaSupportsSave && !string.IsNullOrWhiteSpace(existingAssetPlanJson) && !hasReusableExisting && !request.OverwriteExisting)
                warnings.Add($"Content generation plan '{plan.Id}' had imported AssetPlanJson without asset requirements; generated a compatible default asset plan.");

            var assetPlan = BuildAssetPlan(plan, warnings);
            assetPlans.Add(assetPlan);

            if (!request.DryRun && schemaSupportsSave)
            {
                var json = JsonSerializer.Serialize(assetPlan, JsonOptions);
                await SaveAssetPlanAsync(plan, json, cancellationToken);
                savedCount++;
            }
        }

        logger.LogInformation("Phase 7E asset planning generated {PlanCount} plan(s), {RequirementCount} requirement(s). DryRun={DryRun} Saved={SavedCount}", assetPlans.Count, assetPlans.Sum(p => p.AssetRequirementCount), request.DryRun, savedCount);
        return new AstronomyAssetPlanningResult(plans.Count, assetPlans.Sum(p => p.AssetRequirementCount), savedCount, skippedDuplicates, request.DryRun, assetPlans, warnings);
    }

    private static async Task<List<ContentGenerationPlan>> LoadPlansWithoutAssetColumnsAsync(IQueryable<ContentGenerationPlan> query, CancellationToken cancellationToken)
    {
        var projected = await query
            .Select(p => new
            {
                p.Id,
                p.ContentCategoryCode,
                p.PipelineRunId,
                p.Title,
                p.Language,
                p.RegionId,
                p.ScheduledUtc,
                p.Status,
                p.AstronomyContentOpportunityId,
                p.AstronomyEventIntelligenceId,
                p.SourceEventObjectIdsJson,
                p.RequestedOutputTypesJson,
                p.PlannedObjectNamesJson,
                p.PlanStatus,
                p.PlannedFormat,
                p.PriorityScore,
                p.FinalVideoPath,
                p.ThumbnailPath,
                p.FailureReason,
                p.CompletedUtc,
                p.PrimaryCelestialObjectCode,
                p.PrimaryAstronomyEventTypeCode,
                p.HookStyleCode,
                p.NarrationStyleCode,
                p.ThumbnailStyleCode,
                p.GeneratedByAi,
                p.Priority,
                p.PlanningReason,
                EventLocationName = p.AstronomyEventIntelligence == null ? null : p.AstronomyEventIntelligence.LocationName,
                EventPeakUtc = p.AstronomyEventIntelligence == null ? null : p.AstronomyEventIntelligence.PeakUtc
            })
            .ToListAsync(cancellationToken);

        return projected.Select(p =>
        {
            var plan = new ContentGenerationPlan
            {
                ContentCategoryCode = p.ContentCategoryCode,
                PipelineRunId = p.PipelineRunId,
                Title = p.Title,
                Language = p.Language,
                RegionId = p.RegionId,
                ScheduledUtc = p.ScheduledUtc,
                Status = p.Status,
                AstronomyContentOpportunityId = p.AstronomyContentOpportunityId,
                AstronomyEventIntelligenceId = p.AstronomyEventIntelligenceId,
                SourceEventObjectIdsJson = p.SourceEventObjectIdsJson,
                RequestedOutputTypesJson = p.RequestedOutputTypesJson,
                PlannedObjectNamesJson = p.PlannedObjectNamesJson,
                PlanStatus = p.PlanStatus,
                PlannedFormat = p.PlannedFormat,
                PriorityScore = p.PriorityScore,
                FinalVideoPath = p.FinalVideoPath,
                ThumbnailPath = p.ThumbnailPath,
                FailureReason = p.FailureReason,
                CompletedUtc = p.CompletedUtc,
                PrimaryCelestialObjectCode = p.PrimaryCelestialObjectCode,
                PrimaryAstronomyEventTypeCode = p.PrimaryAstronomyEventTypeCode,
                HookStyleCode = p.HookStyleCode,
                NarrationStyleCode = p.NarrationStyleCode,
                ThumbnailStyleCode = p.ThumbnailStyleCode,
                GeneratedByAi = p.GeneratedByAi,
                Priority = p.Priority,
                PlanningReason = p.PlanningReason,
                AstronomyEventIntelligence = p.EventLocationName is null && p.EventPeakUtc is null ? null : new AstronomyEventIntelligence { LocationName = p.EventLocationName ?? string.Empty, PeakUtc = p.EventPeakUtc }
            };
            plan.AssignId(p.Id);
            return plan;
        }).ToList();
    }

    private static bool HasAssetRequirements(string? assetPlanJson)
    {
        if (string.IsNullOrWhiteSpace(assetPlanJson)) return false;
        try
        {
            var assetPlan = JsonSerializer.Deserialize<AstronomyAssetPlanDto>(assetPlanJson, JsonOptions);
            if (assetPlan?.AssetRequirements is { Count: > 0 }) return true;
            return assetPlan?.SceneAssetGroups?.Any(g => g.AssetRequirements is { Count: > 0 }) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private AstronomyAssetPlanDto BuildAssetPlan(ContentGenerationPlan plan, List<string> warnings)
    {
        var objectNames = ParseStringArray(plan.PlannedObjectNamesJson);
        var requestedOutputs = ParseRequestedOutputTypes(plan.RequestedOutputTypesJson);
        var scenes = ResolveScenes(plan, warnings);
        var groups = new List<AstronomySceneAssetGroupDto>();
        var requirements = new List<AstronomyAssetRequirementDto>();

        for (var i = 0; i < scenes.Count; i++)
        {
            var sceneNumber = i + 1;
            var sceneName = scenes[i];
            var groupRequirements = ResolveAssetTypes(plan.ContentCategoryCode, sceneName)
                .Select((assetType, index) => BuildRequirement(plan, sceneNumber, sceneName, assetType, objectNames, index))
                .ToArray();
            groups.Add(new AstronomySceneAssetGroupDto(sceneNumber, sceneName, groupRequirements));
            requirements.AddRange(groupRequirements);
        }

        AddRequestedOutputRequirements(plan, requestedOutputs, objectNames, groups, requirements);

        return new AstronomyAssetPlanDto(
            plan.Id,
            plan.AstronomyContentOpportunityId,
            plan.AstronomyEventIntelligenceId,
            plan.ContentCategoryCode,
            plan.PlannedFormat,
            plan.RegionId,
            plan.AstronomyEventIntelligence?.LocationName,
            plan.ScheduledUtc,
            plan.AstronomyEventIntelligence?.PeakUtc,
            plan.PlanStatus,
            PlannedStatus,
            groups.Count,
            requirements.Count,
            objectNames,
            groups,
            requirements,
            new
            {
                phase = "Phase7E-asset-planning-engine",
                planningOnly = true,
                noRendering = true,
                noAssetGeneration = true,
                noPipelineExecution = true,
                requestedOutputTypes = requestedOutputs,
                visualSceneStrategy = TryExtractSceneStrategy(plan.PlanningReason)
            });
    }

    private static AstronomyAssetRequirementDto BuildRequirement(ContentGenerationPlan plan, int sceneNumber, string sceneName, string assetType, IReadOnlyList<string> objectNames, int assetIndex)
    {
        var provider = ProviderFor(assetType);
        var expected = ExpectedOutputFor(assetType);
        var prompt = PromptFor(plan, sceneName, assetType, objectNames);
        return new AstronomyAssetRequirementDto(
            sceneNumber,
            sceneName,
            assetType,
            PurposeFor(sceneName, assetType),
            objectNames,
            provider,
            prompt,
            expected,
            Math.Clamp((sceneNumber * 10) + assetIndex, 1, 100),
            PlannedStatus,
            [],
            AstronomyAssetClassificationRules.ResolvePriority(plan.ContentCategoryCode, assetType),
            AstronomyAssetClassificationRules.ResolveExecutionGroup(assetType),
            MetadataFor(plan, sceneName, assetType, objectNames, prompt));
    }

    private static void AddRequestedOutputRequirements(
        ContentGenerationPlan plan,
        IReadOnlyList<string> requestedOutputs,
        IReadOnlyList<string> objectNames,
        List<AstronomySceneAssetGroupDto> groups,
        List<AstronomyAssetRequirementDto> requirements)
    {
        if (requestedOutputs.Count == 0) return;

        var outputRequirements = ResolveRequestedOutputRequirements(plan, requestedOutputs);
        foreach (var outputRequirement in outputRequirements)
        {
            var requirement = BuildRequirement(
                plan,
                outputRequirement.SceneNumber,
                outputRequirement.RequirementCode,
                outputRequirement.AssetType,
                objectNames,
                requirements.Count) with
            {
                MetadataJson = new
                {
                    requirementCode = outputRequirement.RequirementCode,
                    outputType = outputRequirement.OutputType,
                    orientation = outputRequirement.Orientation,
                    aspectRatio = outputRequirement.AspectRatio,
                    planTitle = plan.Title,
                    eventTitle = plan.AstronomyEventIntelligence?.Title,
                    eventShortTitle = plan.AstronomyEventIntelligence?.Summary,
                    locationName = plan.AstronomyEventIntelligence?.LocationName ?? plan.RegionId,
                    scheduledUtc = plan.ScheduledUtc,
                    peakUtc = plan.AstronomyEventIntelligence?.PeakUtc,
                    prompt = PromptFor(plan, outputRequirement.RequirementCode, outputRequirement.AssetType, objectNames)
                }
            };

            requirements.Add(requirement);
            groups.Add(new AstronomySceneAssetGroupDto(outputRequirement.SceneNumber, outputRequirement.RequirementCode, [requirement]));
        }
    }

    private static IReadOnlyList<OutputRequirement> ResolveRequestedOutputRequirements(ContentGenerationPlan plan, IReadOnlyList<string> requestedOutputs)
    {
        var requirements = new List<OutputRequirement>();
        var rareEvent = string.Equals(plan.ContentCategoryCode, "RareEventAlert", StringComparison.OrdinalIgnoreCase);
        if (ContainsOutput(requestedOutputs, "HeroAsset"))
            requirements.Add(new OutputRequirement(80, "hero_landscape", "HeroAsset", "landscape", "16:9", "AiHeroImage"));
        if (ContainsOutput(requestedOutputs, "Thumbnail"))
        {
            requirements.Add(new OutputRequirement(81, "thumbnail_landscape", "Thumbnail", "landscape", "16:9", "ThumbnailConcept"));
            if (rareEvent) requirements.Add(new OutputRequirement(82, "thumbnail_portrait", "Thumbnail", "portrait", "9:16", "ThumbnailConcept"));
        }
        if (ContainsOutput(requestedOutputs, "ShortVideo"))
            requirements.Add(new OutputRequirement(83, "short_scene_portrait", "ShortVideo", "portrait", "9:16", "TextOverlayCard"));
        if (ContainsOutput(requestedOutputs, "LongVideo"))
            requirements.Add(new OutputRequirement(84, "long_scene_landscape", "LongVideo", "landscape", "16:9", "AiCinematicImage"));
        return requirements;
    }

    private static bool ContainsOutput(IReadOnlyList<string> requestedOutputs, string outputType)
        => requestedOutputs.Any(x => string.Equals(x, outputType, StringComparison.OrdinalIgnoreCase));

    private sealed record OutputRequirement(int SceneNumber, string RequirementCode, string OutputType, string Orientation, string AspectRatio, string AssetType);

    private static object MetadataFor(ContentGenerationPlan plan, string sceneName, string assetType, IReadOnlyList<string> objectNames, string prompt)
    {
        var locationName = plan.AstronomyEventIntelligence?.LocationName ?? plan.RegionId;
        return assetType switch
        {
            "StellariumScreenshot" => new
            {
                targetObjects = objectNames,
                locationName,
                regionId = plan.RegionId,
                scheduledUtc = plan.ScheduledUtc,
                peakUtc = plan.AstronomyEventIntelligence?.PeakUtc,
                requiresConstellationLines = sceneName.Contains("Constellation", StringComparison.OrdinalIgnoreCase) || sceneName.Contains("where", StringComparison.OrdinalIgnoreCase),
                requiresLabels = true,
                requiresLandscape = true,
                suggestedOrientation = plan.PlannedFormat?.Equals("Short", StringComparison.OrdinalIgnoreCase) == true ? "portrait-9:16" : "landscape-16:9",
                notes = "Planning only; SSC generation not executed."
            },
            "AiHeroImage" or "AiCinematicImage" => new
            {
                imagePrompt = prompt,
                aspectRatio = plan.PlannedFormat?.Equals("Short", StringComparison.OrdinalIgnoreCase) == true ? "9:16" : "16:9",
                style = assetType == "AiHeroImage" ? "high-retention cinematic astronomy hero image" : "cinematic educational astronomy illustration",
                safetyNote = "No real generation in Phase 7E."
            },
            "NasaAsset" => new
            {
                searchTerms = objectNames.Count > 0 ? objectNames : new[] { plan.ContentCategoryCode, sceneName },
                assetUsagePurpose = PurposeFor(sceneName, assetType),
                fallbackToAiImage = true
            },
            "TextOverlayCard" => new
            {
                titleText = TitleText(sceneName, objectNames),
                subtitleText = SubtitleText(plan, sceneName),
                dataPoints = DataPoints(plan, objectNames)
            },
            "ThumbnailConcept" => new
            {
                thumbnailText = ThumbnailText(plan.Title, objectNames),
                keyObjects = objectNames,
                emotion = EmotionFor(plan.ContentCategoryCode),
                composition = "Large readable text, one dominant celestial focal point, high contrast background, safe margins for mobile crops."
            },
            _ => new
            {
                instruction = prompt,
                planningOnly = true
            }
        };
    }

    private static IReadOnlyList<string> ResolveAssetTypes(string category, string sceneName) => category switch
    {
        "RareEventAlert" => sceneName switch
        {
            "Hook" => ["AiHeroImage", "TextOverlayCard"],
            "What is happening" => ["SkyMapCard"],
            "When and where to look" => ["StellariumScreenshot", "TextOverlayCard"],
            "Why it matters" => ["AiCinematicImage"],
            "CTA / reminder" => ["TextOverlayCard", "ThumbnailConcept"],
            _ => ["TextOverlayCard"]
        },
        "PlanetConjunction" => sceneName switch
        {
            "Hook with object pair" => ["AiHeroImage"],
            "Sky map / where to look" => ["StellariumScreenshot", "ConstellationGuide"],
            "Closest approach / peak timing" => ["SkyMapCard", "TextOverlayCard"],
            "Why conjunction happens" => ["AiCinematicImage"],
            "Viewing tips" => ["TextOverlayCard"],
            "Myth/story/cultural context optional" => ["AiCinematicImage"],
            "Recap" => ["TextOverlayCard"],
            _ => ["TextOverlayCard"]
        },
        "PlanetGrouping" => sceneName switch
        {
            "Hook" => ["AiHeroImage"],
            "Objects involved" => ["NasaAsset"],
            "Sky direction and timing" => ["StellariumScreenshot"],
            "Constellation guide" => ["ConstellationGuide"],
            "Viewing tips" => ["TextOverlayCard"],
            "Weekly relevance" => ["SkyMapCard"],
            _ => ["TextOverlayCard"]
        },
        "WeeklySkyForecast" => sceneName switch
        {
            "Weekly hook" => ["AiHeroImage"],
            "Top sky highlight" => ["StellariumScreenshot"],
            "Moon phase sequence" => ["NasaAsset"],
            "Planet visibility" => ["StellariumScreenshot"],
            "Grouping/conjunction highlight" => ["StellariumScreenshot", "ConstellationGuide"],
            "Best viewing nights" => ["TextOverlayCard"],
            "Outro" => ["TextOverlayCard"],
            _ => ["TextOverlayCard"]
        },
        "MoonSpecials" => sceneName switch
        {
            "Moon phase hook" => ["AiHeroImage"],
            "Illumination and timing" => ["TextOverlayCard"],
            "How to observe/photograph" => ["TextOverlayCard"],
            "Cultural/story angle" => ["AiCinematicImage"],
            "Outro" => ["TextOverlayCard"],
            _ => ["TextOverlayCard"]
        },
        _ => ["TextOverlayCard", "NarrationScriptPlaceholder"]
    };

    private static IReadOnlyList<string> ResolveScenes(ContentGenerationPlan plan, List<string> warnings)
    {
        var sceneStrategy = TryExtractSceneStrategy(plan.PlanningReason);
        if (sceneStrategy.Count > 0)
            return sceneStrategy;

        if (!SceneTemplates.TryGetValue(plan.ContentCategoryCode, out var scenes))
        {
            warnings.Add($"No Phase 7E scene template exists for category '{plan.ContentCategoryCode}'; using generic planning scenes.");
            return ["Hook", "Main explanation", "Viewing guidance", "Recap"];
        }

        if (plan.PlannedFormat?.Equals("Short", StringComparison.OrdinalIgnoreCase) == true && scenes.Length > 5)
            return scenes.Take(5).ToArray();
        return scenes;
    }

    private static IReadOnlyList<string> TryExtractSceneStrategy(string? planningReasonJson)
    {
        if (string.IsNullOrWhiteSpace(planningReasonJson)) return [];
        try
        {
            using var document = JsonDocument.Parse(planningReasonJson);
            if (!document.RootElement.TryGetProperty("visualStrategy", out var visual) || visual.ValueKind != JsonValueKind.Object)
                return [];
            if (!visual.TryGetProperty("sceneStrategy", out var sceneStrategy) || sceneStrategy.ValueKind != JsonValueKind.Array)
                return [];
            return sceneStrategy.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.TryGetProperty("sceneName", out var name) ? name.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseRequestedOutputTypes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            return document.RootElement.EnumerateArray()
                .Select(ReadRequestedOutputType)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ReadRequestedOutputType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var propertyName in new[] { "outputType", "type", "code", "name" })
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private async Task<bool> SchemaSupportsAssetPlanAsync(CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return true;

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
            ? "select count(*) from pragma_table_info('content_generation_plans') where name in ('asset_plan_json','asset_plan_status')"
            : "select count(*) from information_schema.columns where table_name = 'content_generation_plans' and column_name in ('asset_plan_json','asset_plan_status')";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 2;
    }

    private async Task<string?> GetExistingAssetPlanJsonAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return plan.AssetPlanJson;

        return await db.ContentGenerationPlans
            .Where(x => x.Id == plan.Id)
            .Select(x => x.AssetPlanJson)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task SaveAssetPlanAsync(ContentGenerationPlan plan, string assetPlanJson, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.ContentGenerationPlans
                .Where(x => x.Id == plan.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(x => x.AssetPlanJson, assetPlanJson)
                    .SetProperty(x => x.AssetPlanStatus, PlannedStatus), cancellationToken);

            plan.AssetPlanJson = assetPlanJson;
            plan.AssetPlanStatus = PlannedStatus;
            db.Entry(plan).State = EntityState.Unchanged;
            return;
        }

        plan.AssetPlanJson = assetPlanJson;
        plan.AssetPlanStatus = PlannedStatus;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values) => values is { Count: > 0 }
        ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
        : null;

    private static string ProviderFor(string assetType) => assetType switch
    {
        "StellariumScreenshot" => "Stellarium",
        "ConstellationGuide" or "SkyMapCard" or "TextOverlayCard" or "ThumbnailConcept" or "NarrationScriptPlaceholder" => "InternalTemplate",
        "NasaAsset" => "NASA",
        "AiHeroImage" or "AiCinematicImage" => "AI",
        _ => "InternalTemplate"
    };

    private static string ExpectedOutputFor(string assetType) => assetType switch
    {
        "NasaAsset" or "AiHeroImage" or "AiCinematicImage" => "jpg",
        "NarrationScriptPlaceholder" => "text",
        "SkyMapCard" or "ConstellationGuide" => "json",
        _ => "png"
    };

    private static string PromptFor(ContentGenerationPlan plan, string sceneName, string assetType, IReadOnlyList<string> objects)
    {
        var joinedObjects = objects.Count > 0 ? string.Join(", ", objects) : "the planned sky objects";
        return assetType switch
        {
            "StellariumScreenshot" => $"Plan a Stellarium screenshot for {sceneName} showing {joinedObjects} from {plan.RegionId}; do not execute SSC generation.",
            "ConstellationGuide" => $"Create a planning brief for constellation lines, labels, and direction context around {joinedObjects}.",
            "AiHeroImage" or "AiCinematicImage" => $"Draft a no-generation image prompt for {plan.ContentCategoryCode} scene '{sceneName}' featuring {joinedObjects}.",
            "NasaAsset" => $"Plan NASA media search terms and fallback strategy for {sceneName} featuring {joinedObjects}.",
            "ThumbnailConcept" => $"Plan thumbnail text and composition for {plan.Title ?? plan.ContentCategoryCode}.",
            "NarrationScriptPlaceholder" => $"Reserve a narration script placeholder for {sceneName}; do not call TTS.",
            _ => $"Plan an internal {assetType} for {sceneName} featuring {joinedObjects}."
        };
    }

    private static string PurposeFor(string sceneName, string assetType) => assetType switch
    {
        "AiHeroImage" => $"Open scene '{sceneName}' with a strong visual hook.",
        "StellariumScreenshot" => $"Show accurate sky position and viewing geometry for '{sceneName}' in a future rendering phase.",
        "ConstellationGuide" => $"Provide orientation context and nearby constellation references for '{sceneName}'.",
        "TextOverlayCard" => $"Communicate key timing, viewing, or recap text for '{sceneName}'.",
        "ThumbnailConcept" => "Prepare a future thumbnail concept without generating files.",
        _ => $"Support scene '{sceneName}' during future production planning."
    };

    private static string TitleText(string sceneName, IReadOnlyList<string> objects) => objects.Count > 0 ? $"{sceneName}: {string.Join(" + ", objects.Take(3))}" : sceneName;
    private static string SubtitleText(ContentGenerationPlan plan, string sceneName) => $"{plan.RegionId} • {plan.ScheduledUtc:yyyy-MM-dd HH:mm} UTC • {sceneName}";
    private static IReadOnlyList<string> DataPoints(ContentGenerationPlan plan, IReadOnlyList<string> objects) => [$"Format: {plan.PlannedFormat ?? "Unspecified"}", $"Objects: {(objects.Count > 0 ? string.Join(", ", objects) : "Not specified")}", "Status: Planning only"];
    private static string ThumbnailText(string? title, IReadOnlyList<string> objects) => !string.IsNullOrWhiteSpace(title) ? (title.Length <= 42 ? title : title[..39] + "...") : objects.FirstOrDefault() ?? "Sky Event";
    private static string EmotionFor(string category) => category switch
    {
        "RareEventAlert" => "urgency and wonder",
        "PlanetConjunction" => "rare alignment curiosity",
        "PlanetGrouping" => "discoverability and awe",
        "WeeklySkyForecast" => "calm anticipation",
        "MoonSpecials" => "warm lunar wonder",
        _ => "curiosity"
    };

    private static void Validate(AstronomyAssetPlanningRequest request)
    {
        if (request.MinPriorityScore is < 0m or > 10m)
            throw new ArgumentException("MinPriorityScore must be between 0 and 10.");
        if (request.MaxPlans is <= 0)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");
    }
}
