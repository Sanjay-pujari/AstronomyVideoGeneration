using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.Certification;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class OrionContentGenerationPlanSeederTests
{
    [Fact]
    public async Task Orion_seed_inserts_one_constellation_plan_and_is_idempotent()
    {
        await using var db = CreateDb();
        var seeder = CreateSeeder(db);
        SeedExistingProductionPlans(db, 41);
        var before = await SnapshotNonConstellationPlans(db);

        var first = await seeder.SeedAsync(CancellationToken.None);
        var second = await seeder.SeedAsync(CancellationToken.None);

        first.Inserted.Should().BeTrue();
        second.Inserted.Should().BeFalse();
        var rows = await db.ContentGenerationPlans.Where(p => p.PrimaryAstronomyEventTypeCode == "CONSTELLATION").ToArrayAsync();
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(OrionContentGenerationPlanSeeder.OrionPlanId);
        rows[0].SourceExternalEventId.Should().Be(OrionContentGenerationPlanSeeder.OrionSourceExternalEventId);
        rows[0].Title.Should().Be("Orion: How to Find the Hunter Constellation");
        rows[0].PlanStatus.Should().Be("ReadyForManualRun");
        (await SnapshotNonConstellationPlans(db)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Persisted_orion_plan_can_be_read_and_resolves_to_constellation_family()
    {
        await using var db = CreateDb();
        var seeder = CreateSeeder(db);
        await seeder.SeedAsync(CancellationToken.None);

        var plan = await db.ContentGenerationPlans.AsNoTracking().SingleAsync(p => p.Id == OrionContentGenerationPlanSeeder.OrionPlanId);
        var registry = CreateFamilyRegistry();
        registry.Resolve(plan.PrimaryAstronomyEventTypeCode!).FamilyId.Should().Be("CONSTELLATION");
        seeder.ValidateFixture(await seeder.LoadFixtureAsync(CancellationToken.None));
        plan.RequestedOutputTypesJson.Should().Contain("Long");
        plan.RequestedOutputTypesJson.Should().Contain("Short");
        plan.PlannedObjectNamesJson.Should().Contain("Orion");
        plan.Status.Should().Be("Planned");
        plan.CompletedUtc.Should().BeNull();
        plan.PipelineRunId.Should().BeNull();
    }

    [Fact]
    public async Task Conflicting_existing_constellation_plan_fails_clearly_without_partial_insert()
    {
        await using var db = CreateDb();
        db.ContentGenerationPlans.Add(new ContentGenerationPlan
        {
            Title = "Conflicting Orion",
            ContentCategoryCode = "AstronomyEducation",
            RegionId = "GLOBAL",
            Language = "en",
            PrimaryAstronomyEventTypeCode = "CONSTELLATION",
            SourceExternalEventId = "different-orion-id"
        });
        await db.SaveChangesAsync();
        var beforeCount = await db.ContentGenerationPlans.CountAsync();

        var act = () => CreateSeeder(db).SeedAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Conflicting Orion/CONSTELLATION content generation plan exists*");
        (await db.ContentGenerationPlans.CountAsync()).Should().Be(beforeCount);
        (await db.ContentGenerationPlans.AnyAsync(p => p.Id == OrionContentGenerationPlanSeeder.OrionPlanId)).Should().BeFalse();
    }

    [Fact]
    public async Task Fixture_validation_failure_rolls_back_without_partial_row()
    {
        await using var db = CreateDb();
        var badFixturePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(badFixturePath, JsonSerializer.Serialize(new OrionPlanFixture(
            "bad-schema",
            OrionContentGenerationPlanSeeder.OrionPlanId,
            "Orion",
            "AstronomyEducation",
            "MeteorShower",
            "Ori",
            OrionContentGenerationPlanSeeder.OrionSourceExternalEventId,
            "en",
            "GLOBAL",
            "LongAndShort",
            "Planned",
            "ReadyForManualRun",
            ["Long", "Short"],
            ["Orion"],
            "bad")));
        var seeder = CreateSeeder(db);
        var fixture = await seeder.LoadFromPathAsync(badFixturePath, CancellationToken.None);

        var act = () => Task.Run(() => seeder.ValidateFixture(fixture));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CONSTELLATION*");
        (await db.ContentGenerationPlans.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task No_other_constellation_plan_is_inserted()
    {
        await using var db = CreateDb();
        await CreateSeeder(db).SeedAsync(CancellationToken.None);

        var constellationPlans = await db.ContentGenerationPlans.Where(p => p.PrimaryAstronomyEventTypeCode == "CONSTELLATION").Select(p => p.Title).ToArrayAsync();

        constellationPlans.Should().Equal("Orion: How to Find the Hunter Constellation");
    }

    private static OrionContentGenerationPlanSeeder CreateSeeder(MediaFactoryDbContext db) => new(db, CreateFamilyRegistry());

    private static IFamilyCertificationProfileRegistry CreateFamilyRegistry()
    {
        using var provider = new ServiceCollection().AddCgA1CertificationFoundation().BuildServiceProvider();
        return provider.GetRequiredService<IFamilyCertificationProfileRegistry>();
    }

    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static void SeedExistingProductionPlans(MediaFactoryDbContext db, int count)
    {
        for (var i = 0; i < count; i++)
        {
            db.ContentGenerationPlans.Add(new ContentGenerationPlan
            {
                Title = $"Existing MeteorShower Plan {i:00}",
                ContentCategoryCode = "RareEventAlert",
                PrimaryAstronomyEventTypeCode = "MeteorShower",
                SourceExternalEventId = $"meteor-{i:00}",
                RegionId = "GLOBAL",
                Language = "en",
                PlannedFormat = "ShortVideo",
                Status = "Draft",
                PlanStatus = "Draft"
            });
        }
        db.SaveChanges();
    }

    private static async Task<IReadOnlyList<(Guid Id, string? Title, string? EventType)>> SnapshotNonConstellationPlans(MediaFactoryDbContext db)
        => await db.ContentGenerationPlans.AsNoTracking()
            .Where(p => p.PrimaryAstronomyEventTypeCode != "CONSTELLATION")
            .OrderBy(p => p.SourceExternalEventId)
            .Select(p => new ValueTuple<Guid, string?, string?>(p.Id, p.Title, p.PrimaryAstronomyEventTypeCode))
            .ToArrayAsync();
}
