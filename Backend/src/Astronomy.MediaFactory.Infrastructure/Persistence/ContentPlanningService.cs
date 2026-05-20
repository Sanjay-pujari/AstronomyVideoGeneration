using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanningService(MediaFactoryDbContext db, IContentVarietyGuard varietyGuard) : IContentPlanningService
{
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

        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = contentCategoryCode,
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
        return await query.OrderBy(x => x.ScheduledUtc).ThenBy(x => x.Priority).ToListAsync(cancellationToken);
    }

    public Task<ContentGenerationPlan?> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken) => db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<ContentPlanningPipelineRequestPreview> BuildPipelineRequestPreviewAsync(Guid id, CancellationToken cancellationToken){/* unchanged */
        var plan = await db.ContentGenerationPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException($"Content generation plan '{id}' was not found.");
        if (!string.Equals(plan.Status, "Planned", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Pipeline request preview is only allowed for Planned plans. Current status is '{plan.Status}'.");
        var style = await db.ContentCategoryStyleSettings.AsNoTracking().Where(x => x.ContentCategoryCode == plan.ContentCategoryCode && x.Enabled && x.Language == plan.Language).OrderBy(x => x.Priority).FirstOrDefaultAsync(cancellationToken)
                    ?? await db.ContentCategoryStyleSettings.AsNoTracking().Where(x => x.ContentCategoryCode == plan.ContentCategoryCode && x.Enabled).OrderBy(x => x.Priority).FirstOrDefaultAsync(cancellationToken)
                    ?? throw new KeyNotFoundException($"No enabled style settings found for category '{plan.ContentCategoryCode}'.");
        var request = new ContentPlanningPipelineRunRequest(plan.ContentCategoryCode, plan.Language, plan.RegionId, plan.RegionId, plan.PrimaryCelestialObjectCode, plan.HookStyleCode ?? style.HookStyleCode, plan.NarrationStyleCode ?? style.NarrationStyleCode, plan.ThumbnailStyleCode ?? style.ThumbnailStyleCode, plan.ScheduledUtc);
        return new ContentPlanningPipelineRequestPreview(plan.Id, plan.ContentCategoryCode, request);
    }
    public async Task<ContentGenerationPlan?> MarkPlanReadyForManualRunAsync(Guid id, CancellationToken cancellationToken){var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (plan is null) return null; plan.Status = "ReadyForManualRun"; plan.Touch(); await db.SaveChangesAsync(cancellationToken); return plan;}
    public Task<bool> MarkPlanAsInProgressAsync(Guid id, CancellationToken cancellationToken) => UpdatePlanStatusAsync(id, "InProgress", cancellationToken);
    public Task<bool> MarkPlanAsCompletedAsync(Guid id, CancellationToken cancellationToken) => UpdatePlanStatusAsync(id, "Completed", cancellationToken);
    public Task<bool> MarkPlanAsFailedAsync(Guid id, CancellationToken cancellationToken) => UpdatePlanStatusAsync(id, "Failed", cancellationToken);
    private async Task<bool> UpdatePlanStatusAsync(Guid id, string status, CancellationToken cancellationToken){var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (plan is null) return false; plan.Status = status; plan.Touch(); await db.SaveChangesAsync(cancellationToken); return true;}
}
