using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.Certification;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class OrionContentGenerationPlanSeeder(MediaFactoryDbContext db, IFamilyCertificationProfileRegistry familyRegistry) : IOrionContentGenerationPlanSeeder
{
    public static readonly Guid OrionPlanId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public const string OrionSourceExternalEventId = "constellation-iau-ori";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<OrionContentGenerationPlanSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        var fixture = await LoadFixtureAsync(cancellationToken);
        ValidateFixture(fixture);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var ownsTransaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null;
            await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

            var existingById = await db.ContentGenerationPlans.SingleOrDefaultAsync(p => p.Id == fixture.PlanId, cancellationToken);
            if (existingById is not null)
            {
                EnsureExistingMatchesFixture(existingById, fixture);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new OrionContentGenerationPlanSeedResult(existingById.Id, existingById.Title ?? string.Empty, existingById.PrimaryAstronomyEventTypeCode ?? string.Empty, false, "Existing Orion plan already matched the canonical fixture.");
            }

            var conflicts = await db.ContentGenerationPlans
                .Where(p => p.SourceExternalEventId == OrionSourceExternalEventId || p.PrimaryAstronomyEventTypeCode == "CONSTELLATION")
                .ToListAsync(cancellationToken);
            if (conflicts.Count > 0)
            {
                throw new InvalidOperationException($"Conflicting Orion/CONSTELLATION content generation plan exists. Expected id '{fixture.PlanId}' and sourceExternalEventId '{OrionSourceExternalEventId}', but found {conflicts.Count} conflicting row(s): {string.Join(", ", conflicts.Select(p => p.Id))}.");
            }

            var plan = CreatePlan(fixture);
            db.ContentGenerationPlans.Add(plan);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new OrionContentGenerationPlanSeedResult(plan.Id, plan.Title ?? string.Empty, plan.PrimaryAstronomyEventTypeCode ?? string.Empty, true, "Inserted canonical Orion constellation content generation plan.");
        });
    }

    public Task<OrionPlanFixture> LoadFixtureAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Production", "Constellations", "Seeds", "orion-content-generation-plan.json");
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Astronomy.MediaFactory.Infrastructure/Production/Constellations/Seeds/orion-content-generation-plan.json"));
        }
        return LoadFromPathAsync(path, cancellationToken);
    }

    public async Task<OrionPlanFixture> LoadFromPathAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Orion content generation plan fixture was not found at '{path}'.", path);
        await using var stream = File.OpenRead(path);
        var fixture = await JsonSerializer.DeserializeAsync<OrionPlanFixture>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Orion content generation plan fixture is empty or invalid JSON.");
        return fixture;
    }

    public void ValidateFixture(OrionPlanFixture fixture)
    {
        if (fixture.PlanId != OrionPlanId) throw new InvalidOperationException($"Orion fixture planId must be '{OrionPlanId}'.");
        if (!string.Equals(fixture.PrimaryAstronomyEventTypeCode, "CONSTELLATION", StringComparison.Ordinal)) throw new InvalidOperationException("Orion fixture must use PrimaryAstronomyEventTypeCode 'CONSTELLATION'.");
        if (string.IsNullOrWhiteSpace(fixture.Title)) throw new InvalidOperationException("Orion fixture title is required.");
        if (string.IsNullOrWhiteSpace(fixture.ContentCategoryCode)) throw new InvalidOperationException("Orion fixture contentCategoryCode is required.");
        if (string.IsNullOrWhiteSpace(fixture.Language)) throw new InvalidOperationException("Orion fixture language is required.");
        if (string.IsNullOrWhiteSpace(fixture.RegionId)) throw new InvalidOperationException("Orion fixture regionId is required.");
        if (string.IsNullOrWhiteSpace(fixture.PlannedFormat)) throw new InvalidOperationException("Orion fixture plannedFormat is required.");
        if (fixture.RequestedOutputTypes.Count == 0) throw new InvalidOperationException("Orion fixture requestedOutputTypes must not be empty.");
        if (fixture.PlannedObjectNames.Count == 0) throw new InvalidOperationException("Orion fixture plannedObjectNames must not be empty.");
        var profile = familyRegistry.Resolve(fixture.PrimaryAstronomyEventTypeCode);
        if (!string.Equals(profile.FamilyId, "CONSTELLATION", StringComparison.Ordinal)) throw new InvalidOperationException("Orion fixture did not resolve to the CONSTELLATION family.");
    }

    private static ContentGenerationPlan CreatePlan(OrionPlanFixture fixture)
    {
        var plan = new ContentGenerationPlan
        {
            Title = fixture.Title,
            ContentCategoryCode = fixture.ContentCategoryCode,
            PrimaryAstronomyEventTypeCode = fixture.PrimaryAstronomyEventTypeCode,
            PrimaryCelestialObjectCode = fixture.PrimaryCelestialObjectCode,
            SourceExternalEventId = fixture.SourceExternalEventId,
            Language = fixture.Language,
            RegionId = fixture.RegionId,
            PlannedFormat = fixture.PlannedFormat,
            Status = fixture.Status,
            PlanStatus = fixture.PlanStatus,
            RequestedOutputTypesJson = JsonSerializer.Serialize(fixture.RequestedOutputTypes, JsonOptions),
            PlannedObjectNamesJson = JsonSerializer.Serialize(fixture.PlannedObjectNames, JsonOptions),
            GeneratedByAi = false,
            ManualValidation = true,
            Priority = 40,
            PlanningReason = fixture.CompatibilityNotes,
            AssetPlanJson = JsonSerializer.Serialize(new { fixture.SchemaVersion, fixture.CompatibilityNotes, seedSource = "orion-content-generation-plan.json", eventSemantics = "evergreen-constellation" }, JsonOptions),
            AssetPlanStatus = "Planned"
        };
        plan.AssignId(fixture.PlanId);
        return plan;
    }

    private static void EnsureExistingMatchesFixture(ContentGenerationPlan plan, OrionPlanFixture fixture)
    {
        if (!string.Equals(plan.SourceExternalEventId, OrionSourceExternalEventId, StringComparison.Ordinal)
            || !string.Equals(plan.PrimaryAstronomyEventTypeCode, "CONSTELLATION", StringComparison.Ordinal)
            || !string.Equals(plan.Title, fixture.Title, StringComparison.Ordinal)
            || !string.Equals(plan.PrimaryCelestialObjectCode, fixture.PrimaryCelestialObjectCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Content generation plan id '{fixture.PlanId}' already exists but does not match the canonical Orion fixture.");
        }
    }
}

public interface IOrionContentGenerationPlanSeeder
{
    Task<OrionContentGenerationPlanSeedResult> SeedAsync(CancellationToken cancellationToken);
}

public sealed record OrionContentGenerationPlanSeedResult(Guid ContentGenerationPlanId, string Title, string PrimaryAstronomyEventTypeCode, bool Inserted, string Message);

public sealed record OrionPlanFixture(
    string SchemaVersion,
    Guid PlanId,
    string Title,
    string ContentCategoryCode,
    string PrimaryAstronomyEventTypeCode,
    string PrimaryCelestialObjectCode,
    string SourceExternalEventId,
    string Language,
    string RegionId,
    string PlannedFormat,
    string Status,
    string PlanStatus,
    IReadOnlyList<string> RequestedOutputTypes,
    IReadOnlyList<string> PlannedObjectNames,
    string CompatibilityNotes);
