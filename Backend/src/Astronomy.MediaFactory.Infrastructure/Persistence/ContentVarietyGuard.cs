using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentVarietyGuard(MediaFactoryDbContext db) : IContentVarietyGuard
{
    public async Task<bool> CanUseCelestialObjectAsync(string categoryCode, string objectCode, DateTimeOffset date, CancellationToken cancellationToken)
        => !(await GetBlockedItemsAsync(categoryCode, date, cancellationToken)).Any(x => x.RuleType == "CelestialObject" && x.RuleKey == objectCode);

    public async Task<bool> CanUseStyleAsync(string categoryCode, string styleCode, string styleType, DateTimeOffset date, CancellationToken cancellationToken)
        => !(await GetBlockedItemsAsync(categoryCode, date, cancellationToken)).Any(x => x.RuleType == styleType && x.RuleKey == styleCode);

    public async Task<IReadOnlyCollection<ContentVarietyBlockedItem>> GetBlockedItemsAsync(string categoryCode, DateTimeOffset date, CancellationToken cancellationToken)
    {
        var rules = await db.ContentVarietyRules.AsNoTracking()
            .Where(x => x.Enabled && x.ContentCategoryCode == categoryCode)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        var recent = await db.ContentGenerationPlans.AsNoTracking()
            .Where(x => x.ContentCategoryCode == categoryCode
                        && (x.Status == "Planned" || x.Status == "InProgress" || x.Status == "Completed")
                        && x.ScheduledUtc.HasValue
                        && x.ScheduledUtc.Value <= date)
            .ToListAsync(cancellationToken);

        var blocked = new List<ContentVarietyBlockedItem>();
        foreach (var rule in rules)
        {
            var weeklyCount = rule.MaxUsagePerWeek.HasValue
                ? CountMatches(recent.Where(x => x.ScheduledUtc >= date.AddDays(-7)), rule)
                : 0;
            if (rule.MaxUsagePerWeek.HasValue && weeklyCount >= rule.MaxUsagePerWeek.Value)
            {
                blocked.Add(new ContentVarietyBlockedItem(rule.RuleType, rule.RuleKey, $"Weekly max usage reached ({weeklyCount}/{rule.MaxUsagePerWeek.Value})."));
                continue;
            }

            var monthlyCount = rule.MaxUsagePerMonth.HasValue
                ? CountMatches(recent.Where(x => x.ScheduledUtc >= date.AddDays(-30)), rule)
                : 0;
            if (rule.MaxUsagePerMonth.HasValue && monthlyCount >= rule.MaxUsagePerMonth.Value)
            {
                blocked.Add(new ContentVarietyBlockedItem(rule.RuleType, rule.RuleKey, $"Monthly max usage reached ({monthlyCount}/{rule.MaxUsagePerMonth.Value})."));
                continue;
            }

            if (rule.CooldownDays <= 0) continue;
            var cooldownStart = date.AddDays(-rule.CooldownDays);
            var inCooldown = recent
                .Where(x => x.ScheduledUtc >= cooldownStart)
                .Any(x => MatchesRule(x, rule));
            if (inCooldown)
            {
                blocked.Add(new ContentVarietyBlockedItem(rule.RuleType, rule.RuleKey, $"Cooldown active for {rule.CooldownDays} days."));
            }
        }

        return blocked;
    }

    private static int CountMatches(IEnumerable<ContentGenerationPlan> plans, ContentVarietyRule rule) =>
        plans.Count(x => MatchesRule(x, rule));

    private static bool MatchesRule(ContentGenerationPlan plan, ContentVarietyRule rule)
        => rule.RuleType switch
        {
            "CelestialObject" => string.Equals(plan.PrimaryCelestialObjectCode, rule.RuleKey, StringComparison.OrdinalIgnoreCase),
            "HookStyle" => string.Equals(plan.HookStyleCode, rule.RuleKey, StringComparison.OrdinalIgnoreCase),
            "NarrationStyle" => string.Equals(plan.NarrationStyleCode, rule.RuleKey, StringComparison.OrdinalIgnoreCase),
            "ThumbnailStyle" => string.Equals(plan.ThumbnailStyleCode, rule.RuleKey, StringComparison.OrdinalIgnoreCase),
            "Category" => string.Equals(plan.ContentCategoryCode, rule.RuleKey, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
