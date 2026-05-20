using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanningService(MediaFactoryDbContext db, IContentVarietyGuard varietyGuard) : IContentPlanningService
{
    public async Task<GenerateContentPlanResponse> GeneratePlanAsync(GenerateContentPlanRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContentCategoryCode)) throw new ArgumentException("ContentCategoryCode is required.");
        if (string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("Language is required.");
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("RegionId is required.");
        if (string.IsNullOrWhiteSpace(request.RegionName)) throw new ArgumentException("RegionName is required.");

        var category = await db.ContentCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Code == request.ContentCategoryCode, cancellationToken);
        if (category is null || !category.Enabled) throw new KeyNotFoundException($"Content category '{request.ContentCategoryCode}' is not found or disabled.");

        var template = await db.ContentIdeaTemplates.AsNoTracking()
            .Where(x => x.ContentCategoryCode == request.ContentCategoryCode && x.Enabled && x.Language == request.Language)
            .OrderByDescending(x => x.Priority)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.ContentIdeaTemplates.AsNoTracking()
                .Where(x => x.ContentCategoryCode == request.ContentCategoryCode && x.Enabled && x.Language == "en")
                .OrderByDescending(x => x.Priority)
                .FirstOrDefaultAsync(cancellationToken);

        var style = await db.ContentCategoryStyleSettings.AsNoTracking()
            .Where(x => x.ContentCategoryCode == request.ContentCategoryCode && x.Enabled && x.Language == request.Language)
            .OrderByDescending(x => x.Priority)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.ContentCategoryStyleSettings.AsNoTracking()
                .Where(x => x.ContentCategoryCode == request.ContentCategoryCode && x.Enabled && x.Language == "en")
                .OrderByDescending(x => x.Priority)
                .FirstOrDefaultAsync(cancellationToken);

        var objectCode = string.Empty;
        var objectName = string.Empty;
        if (!string.IsNullOrWhiteSpace(request.PrimaryCelestialObjectCode))
        {
            var celestialObject = await db.CelestialObjects.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code == request.PrimaryCelestialObjectCode && x.Enabled, cancellationToken);
            if (celestialObject is not null)
            {
                objectCode = celestialObject.Code;
                objectName = celestialObject.Name;
            }
        }

        var eventCode = string.Empty;
        var eventName = string.Empty;
        if (!string.IsNullOrWhiteSpace(request.PrimaryAstronomyEventTypeCode))
        {
            var eventType = await db.AstronomyEventTypes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code == request.PrimaryAstronomyEventTypeCode && x.Enabled, cancellationToken);
            if (eventType is not null)
            {
                eventCode = eventType.Code;
                eventName = eventType.DisplayName;
            }
        }

        var scheduleDate = request.ScheduledUtc ?? DateTime.UtcNow;
        var placeholders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ObjectName"] = objectName,
            ["ObjectCode"] = objectCode,
            ["EventName"] = eventName,
            ["EventCode"] = eventCode,
            ["RegionName"] = request.RegionName,
            ["RegionId"] = request.RegionId,
            ["Date"] = scheduleDate.ToString("yyyy-MM-dd"),
            ["Language"] = request.Language,
            ["ContentCategoryCode"] = request.ContentCategoryCode
        };

        var titleTemplate = template?.TitleTemplate ?? "{ContentCategoryCode} for {RegionName}";
        var title = ApplyTemplate(titleTemplate, placeholders);
        var topic = template is null ? null : ApplyTemplate(template.TopicTemplate, placeholders);

        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = request.ContentCategoryCode,
            Title = title,
            Language = request.Language,
            RegionId = request.RegionId,
            ScheduledUtc = request.ScheduledUtc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(request.ScheduledUtc.Value, DateTimeKind.Utc)),
            Status = "Planned",
            PrimaryCelestialObjectCode = request.PrimaryCelestialObjectCode,
            PrimaryAstronomyEventTypeCode = request.PrimaryAstronomyEventTypeCode,
            HookStyleCode = style?.HookStyleCode,
            NarrationStyleCode = style?.NarrationStyleCode,
            ThumbnailStyleCode = style?.ThumbnailStyleCode,
            GeneratedByAi = request.GeneratedByAi,
            Priority = 100,
            PlanningReason = $"Template={(template?.TemplateCode ?? "default")}({template?.Language ?? "n/a"}); Topic={topic ?? "n/a"}; Style={(style is null ? "none" : $"{style.HookStyleCode}/{style.NarrationStyleCode}/{style.ThumbnailStyleCode}")}; Object={objectCode}; Event={eventCode}; GeneratedByAi={request.GeneratedByAi};"
        };

        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);
        return new GenerateContentPlanResponse(plan.Id, "Planned", plan.Title, plan.PlanningReason);
    }

    public async Task<ContentGenerationPlan> GenerateDailyPlanAsync(
        string contentCategoryCode,
        string language,
        string regionId,
        DateTimeOffset scheduledUtc,
        string? primaryCelestialObjectCode,
        CancellationToken cancellationToken)
    {
        var category = await db.ContentCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Code == contentCategoryCode, cancellationToken);
        if (category is null || !category.Enabled) throw new KeyNotFoundException($"Content category '{contentCategoryCode}' is not found or disabled.");

        var style = await db.ContentCategoryStyleSettings.AsNoTracking()
            .Where(x => x.ContentCategoryCode == contentCategoryCode && x.Enabled && x.Language == language)
            .OrderBy(x => x.Priority).FirstOrDefaultAsync(cancellationToken)
            ?? await db.ContentCategoryStyleSettings.AsNoTracking().Where(x => x.ContentCategoryCode == contentCategoryCode && x.Enabled).OrderBy(x => x.Priority).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"No enabled style settings found for category '{contentCategoryCode}'.");

        var blocked = await varietyGuard.GetBlockedItemsAsync(contentCategoryCode, scheduledUtc, cancellationToken);
        var planningNotes = new List<string>();

        var selectedObject = await SelectCelestialObjectAsync(contentCategoryCode, primaryCelestialObjectCode, blocked, planningNotes, cancellationToken);
        var selectedHook = ChooseStyle(style.HookStyleCode, "HookStyle", blocked, planningNotes);
        var selectedNarration = ChooseStyle(style.NarrationStyleCode, "NarrationStyle", blocked, planningNotes);
        var selectedThumbnail = ChooseStyle(style.ThumbnailStyleCode, "ThumbnailStyle", blocked, planningNotes);
        var selectedTemplate = await SelectIdeaTemplateAsync(contentCategoryCode, language, cancellationToken);
        var resolvedTemplateValues = await BuildTemplateValuesAsync(selectedObject, regionId, scheduledUtc, language, cancellationToken);
        var generatedTitle = selectedTemplate is null ? null : ApplyTemplate(selectedTemplate.TitleTemplate, resolvedTemplateValues);
        if (selectedTemplate is not null)
        {
            planningNotes.Add($"Generated title using idea template '{selectedTemplate.TemplateCode}' ({selectedTemplate.Language}).");
        }
        else
        {
            planningNotes.Add($"No enabled idea template found for category '{contentCategoryCode}' and language '{language}'.");
        }

        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = contentCategoryCode,
            Title = generatedTitle,
            Language = language,
            RegionId = regionId,
            ScheduledUtc = scheduledUtc,
            PrimaryCelestialObjectCode = selectedObject,
            HookStyleCode = selectedHook,
            NarrationStyleCode = selectedNarration,
            ThumbnailStyleCode = selectedThumbnail,
            Priority = style.Priority,
            Status = "Planned",
            GeneratedByAi = false,
            PlanningReason = planningNotes.Count == 0 ? null : string.Join(" ", planningNotes)
        };

        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);
        return plan;
    }

    private async Task<ContentIdeaTemplate?> SelectIdeaTemplateAsync(string categoryCode, string language, CancellationToken cancellationToken)
        => await db.ContentIdeaTemplates.AsNoTracking()
            .Where(x => x.ContentCategoryCode == categoryCode && x.Enabled && x.Language == language)
            .OrderBy(x => x.Priority)
            .FirstOrDefaultAsync(cancellationToken)
        ?? await db.ContentIdeaTemplates.AsNoTracking()
            .Where(x => x.ContentCategoryCode == categoryCode && x.Enabled)
            .OrderBy(x => x.Priority)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Dictionary<string, string>> BuildTemplateValuesAsync(string? selectedObjectCode, string regionId, DateTimeOffset scheduledUtc, string language, CancellationToken cancellationToken)
    {
        var objectName = selectedObjectCode;
        if (!string.IsNullOrWhiteSpace(selectedObjectCode))
        {
            var celestialObject = await db.CelestialObjects.AsNoTracking().FirstOrDefaultAsync(x => x.Code == selectedObjectCode, cancellationToken);
            objectName = celestialObject?.Name ?? selectedObjectCode;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ObjectName"] = objectName ?? "Night Sky",
            ["EventName"] = "Sky Event",
            ["RegionName"] = regionId,
            ["Date"] = scheduledUtc.UtcDateTime.ToString("yyyy-MM-dd"),
            ["Language"] = language
        };
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> values)
    {
        var output = template;
        foreach (var entry in values)
        {
            output = output.Replace($"{{{entry.Key}}}", entry.Value, StringComparison.OrdinalIgnoreCase);
        }

        return output;
    }

    private async Task<string?> SelectCelestialObjectAsync(string categoryCode, string? preferredObjectCode, IReadOnlyCollection<ContentVarietyBlockedItem> blocked, List<string> planningNotes, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preferredObjectCode) && !IsBlocked(blocked, "CelestialObject", preferredObjectCode)) return preferredObjectCode;

        var enabledObjects = await db.CelestialObjects.AsNoTracking().Where(x => x.Enabled).OrderByDescending(x => x.VisibilityPriority).ThenByDescending(x => x.ViralityScore).ToListAsync(cancellationToken);
        var candidate = enabledObjects.FirstOrDefault(x => !IsBlocked(blocked, "CelestialObject", x.Code));
        if (candidate is not null)
        {
            if (!string.IsNullOrWhiteSpace(preferredObjectCode)) planningNotes.Add($"Preferred celestial object '{preferredObjectCode}' is blocked by variety rule; selected '{candidate.Code}' instead.");
            return candidate.Code;
        }

        var fallback = enabledObjects.FirstOrDefault();
        if (fallback is not null)
        {
            planningNotes.Add($"Variety override: all enabled celestial objects are blocked for '{categoryCode}', selected highest-priority '{fallback.Code}'.");
            return fallback.Code;
        }

        return preferredObjectCode;
    }

    private static string ChooseStyle(string styleCode, string styleType, IReadOnlyCollection<ContentVarietyBlockedItem> blocked, List<string> planningNotes)
    {
        if (!IsBlocked(blocked, styleType, styleCode)) return styleCode;
        planningNotes.Add($"Variety override: style '{styleCode}' ({styleType}) is currently blocked by cooldown and was kept due to no alternate style selection flow.");
        return styleCode;
    }

    private static bool IsBlocked(IReadOnlyCollection<ContentVarietyBlockedItem> blocked, string type, string key)
        => blocked.Any(x => x.RuleType == type && x.RuleKey.Equals(key, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyCollection<ContentGenerationPlan>> GetPendingPlansAsync(string? status, CancellationToken cancellationToken)
    {
        var query = db.ContentGenerationPlans.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        return await query.OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
    }

    public Task<ContentGenerationPlan?> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken) => db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<ContentPlanningPipelineRequestPreview> BuildPipelineRequestPreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Content generation plan '{id}' was not found.");

        var allowedStatus = string.Equals(plan.Status, "Planned", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.Status, "ReadyForManualRun", StringComparison.OrdinalIgnoreCase);
        if (!allowedStatus)
        {
            throw new InvalidOperationException($"Pipeline request preview is only allowed for Planned or ReadyForManualRun plans. Current status is '{plan.Status}'.");
        }

        var category = await db.ContentCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Code == plan.ContentCategoryCode, cancellationToken);
        var warnings = new List<string>();
        if (category is null || !category.Enabled)
        {
            warnings.Add("unsupported category");
        }

        var style = await db.ContentCategoryStyleSettings.AsNoTracking()
            .Where(x => x.ContentCategoryCode == plan.ContentCategoryCode && x.Enabled && x.Language == plan.Language)
            .OrderBy(x => x.Priority)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.ContentCategoryStyleSettings.AsNoTracking()
                .Where(x => x.ContentCategoryCode == plan.ContentCategoryCode && x.Enabled && x.Language == "en")
                .OrderBy(x => x.Priority)
                .FirstOrDefaultAsync(cancellationToken);

        var hookStyle = plan.HookStyleCode ?? style?.HookStyleCode;
        var narrationStyle = plan.NarrationStyleCode ?? style?.NarrationStyleCode;
        var thumbnailStyle = plan.ThumbnailStyleCode ?? style?.ThumbnailStyleCode;
        if (string.IsNullOrWhiteSpace(hookStyle) || string.IsNullOrWhiteSpace(narrationStyle) || string.IsNullOrWhiteSpace(thumbnailStyle))
        {
            warnings.Add("missing style setting");
        }

        string? celestialObjectName = null;
        if (!string.IsNullOrWhiteSpace(plan.PrimaryCelestialObjectCode))
        {
            var celestialObject = await db.CelestialObjects.AsNoTracking().FirstOrDefaultAsync(x => x.Code == plan.PrimaryCelestialObjectCode && x.Enabled, cancellationToken);
            celestialObjectName = celestialObject?.Name;
            if (celestialObject is null) warnings.Add("missing celestial object");
        }

        string? eventTypeName = null;
        if (!string.IsNullOrWhiteSpace(plan.PrimaryAstronomyEventTypeCode))
        {
            var eventType = await db.AstronomyEventTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Code == plan.PrimaryAstronomyEventTypeCode && x.Enabled, cancellationToken);
            eventTypeName = eventType?.DisplayName;
            if (eventType is null) warnings.Add("missing astronomy event type");
        }

        if (plan.ScheduledUtc is null) warnings.Add("missing scheduledUtc");

        var pipelineRequest = new Dictionary<string, object?>
        {
            ["contentCategoryCode"] = plan.ContentCategoryCode,
            ["title"] = plan.Title,
            ["language"] = plan.Language,
            ["regionId"] = plan.RegionId,
            ["locationName"] = plan.RegionId,
            ["scheduledUtc"] = plan.ScheduledUtc,
            ["primaryCelestialObjectCode"] = plan.PrimaryCelestialObjectCode,
            ["primaryCelestialObjectName"] = celestialObjectName,
            ["primaryAstronomyEventTypeCode"] = plan.PrimaryAstronomyEventTypeCode,
            ["primaryAstronomyEventTypeName"] = eventTypeName,
            ["hookStyleCode"] = hookStyle,
            ["narrationStyleCode"] = narrationStyle,
            ["thumbnailStyleCode"] = thumbnailStyle,
            ["generatedByAi"] = plan.GeneratedByAi
        };

        return new ContentPlanningPipelineRequestPreview(plan.Id, plan.ContentCategoryCode, plan.Status, plan.Title, pipelineRequest, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
    public async Task<ContentGenerationPlan?> MarkPlanReadyForManualRunAsync(Guid id, CancellationToken cancellationToken){var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (plan is null) return null; plan.Status = "ReadyForManualRun"; plan.Touch(); await db.SaveChangesAsync(cancellationToken); return plan;}
    public Task<bool> MarkPlanAsInProgressAsync(Guid id, CancellationToken cancellationToken) => UpdatePlanStatusAsync(id, "InProgress", cancellationToken);
    public Task<bool> MarkPlanAsCompletedAsync(Guid id, CancellationToken cancellationToken) => UpdatePlanStatusAsync(id, "Completed", cancellationToken);
    public Task<bool> MarkPlanAsFailedAsync(Guid id, CancellationToken cancellationToken) => UpdatePlanStatusAsync(id, "Failed", cancellationToken);
    public async Task<ManualExecutionStartResponse?> StartManualExecutionAsync(Guid id, CancellationToken cancellationToken)
    {
        var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (plan is null) return null;

        var allowed = string.Equals(plan.Status, "Planned", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.Status, "ReadyForManualRun", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            throw new InvalidOperationException($"Manual execution can only be started for Planned or ReadyForManualRun plans. Current status is '{plan.Status}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var execution = new ContentPipelineExecution
        {
            Id = Guid.NewGuid(),
            ContentGenerationPlanId = plan.Id,
            PipelineRunId = null,
            ContentCategoryCode = plan.ContentCategoryCode,
            StartedUtc = now,
            FinishedUtc = null,
            Status = "InProgress",
            ErrorMessage = null,
            OutputFolder = null,
            LongVideoPath = null,
            ShortVideoPath = null,
            ThumbnailLongPath = null,
            ThumbnailShortPath = null,
            PublishingCompleted = false,
            AnalyticsInitialized = false,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        plan.Status = "InProgress";
        plan.UpdatedUtc = now;
        db.ContentPipelineExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);

        return new ManualExecutionStartResponse(plan.Id, execution.Id, execution.Status);
    }

    public async Task<ContentPipelineExecution?> CompleteExecutionAsync(Guid executionId, CompleteContentPlanningExecutionRequest request, CancellationToken cancellationToken)
    {
        var execution = await db.ContentPipelineExecutions.FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken);
        if (execution is null) return null;

        var now = DateTimeOffset.UtcNow;
        execution.Status = "Completed";
        execution.FinishedUtc = now;
        execution.UpdatedUtc = now;
        execution.PipelineRunId = request.PipelineRunId ?? execution.PipelineRunId;
        execution.OutputFolder = request.OutputFolder ?? execution.OutputFolder;
        execution.LongVideoPath = request.LongVideoPath ?? execution.LongVideoPath;
        execution.ShortVideoPath = request.ShortVideoPath ?? execution.ShortVideoPath;
        execution.ThumbnailLongPath = request.ThumbnailLongPath ?? execution.ThumbnailLongPath;
        execution.ThumbnailShortPath = request.ThumbnailShortPath ?? execution.ThumbnailShortPath;
        execution.PublishingCompleted = request.PublishingCompleted;
        execution.AnalyticsInitialized = request.AnalyticsInitialized;

        if (execution.ContentGenerationPlanId.HasValue)
        {
            var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(x => x.Id == execution.ContentGenerationPlanId.Value, cancellationToken);
            if (plan is not null)
            {
                plan.Status = "Completed";
                plan.UpdatedUtc = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<ContentPipelineExecution?> FailExecutionAsync(Guid executionId, FailContentPlanningExecutionRequest request, CancellationToken cancellationToken)
    {
        var execution = await db.ContentPipelineExecutions.FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken);
        if (execution is null) return null;

        var now = DateTimeOffset.UtcNow;
        execution.Status = "Failed";
        execution.ErrorMessage = request.ErrorMessage;
        execution.FinishedUtc = now;
        execution.UpdatedUtc = now;

        if (execution.ContentGenerationPlanId.HasValue)
        {
            var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(x => x.Id == execution.ContentGenerationPlanId.Value, cancellationToken);
            if (plan is not null)
            {
                plan.Status = "Failed";
                plan.UpdatedUtc = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<IReadOnlyCollection<ContentPipelineExecution>> GetExecutionsAsync(string? status, CancellationToken cancellationToken)
    {
        var query = db.ContentPipelineExecutions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        return await query.OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
    }

    public Task<ContentPipelineExecution?> GetExecutionByIdAsync(Guid executionId, CancellationToken cancellationToken)
        => db.ContentPipelineExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken);

    private async Task<bool> UpdatePlanStatusAsync(Guid id, string status, CancellationToken cancellationToken){var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (plan is null) return false; plan.Status = status; plan.Touch(); await db.SaveChangesAsync(cancellationToken); return true;}
}
