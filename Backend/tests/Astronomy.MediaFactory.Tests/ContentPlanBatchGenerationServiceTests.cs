using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class ContentPlanBatchGenerationServiceTests
{
    private static readonly Guid GeminidsPlanId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
    private static readonly Guid ManualValidationPlanId = Guid.Parse("0742de5a-ca13-4965-9380-a173acfa2428");

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionDryRun_UsesProductionPipelineOnly()
    {
        await using var db = CreateDb();
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = "GEMINIDS-2026",
            ExternalEventId = "geminids-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "AutoGenerate",
            EventType = "MeteorShower",
            Title = "Geminids Meteor Shower Peak",
            Summary = "Geminids",
            StartUtc = new DateTimeOffset(2026, 12, 13, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RarityScore = 9,
            VisibilityScore = 9,
            AudienceInterestScore = 9,
            ContentOpportunityScore = 9
        };
        db.AstronomyEventIntelligences.Add(intelligence);

        var plan = new ContentGenerationPlan
        {
            Title = "Geminids Meteor Shower Peak",
            ContentCategoryCode = "RareEventAlert",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            ScheduledUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            Status = "Planned",
            PlanStatus = "Planned",
            Priority = 1,
            PriorityScore = 9,
            AstronomyEventIntelligenceId = intelligence.Id,
            AstronomyEventIntelligence = intelligence,
            SourceExternalEventId = "geminids-2026",
            RequestedOutputTypesJson = "[\"Short\",\"Long\"]"
        };
        plan.AssignId(GeminidsPlanId);
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();

        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: true,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.UseProductionPipeline);
        Assert.False(response.UsedPlaceholderVisuals);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, response.PlanId);
        Assert.NotNull(response.ProductionPipelineRequest);
        Assert.Equal(CapturingProductionExecutionService.ExpectedSteps, response.PlannedSteps);
        Assert.Equal(CapturingProductionExecutionService.ExpectedSteps.Cast<object>(), response.Steps);
        Assert.DoesNotContain(response.Steps, step => step.ToString() is "GenerateAssetPlans" or "CreateAssetProductionJobs" or "GenerateVisualAssets" or "RenderSceneVideos");
        Assert.Equal(GeminidsPlanId, production.CapturedPlanId);
        Assert.True(production.CapturedDryRun);
        Assert.False(production.CapturedOverwriteExisting);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_PlanIdLanguageMismatch_CreatesAndSelectsSiblingPlan()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db);
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "hi",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: true,
            UseProductionPipeline: true,
            PlanId: GeminidsPlanId), CancellationToken.None);

        var sibling = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
            .SingleAsync(p => p.Language == "hi");

        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, response.RequestedPlanId);
        Assert.Equal(sibling.Id, response.SelectedPlanId);
        Assert.Equal("en", response.RequestedPlanLanguage);
        Assert.Equal("hi", response.RequestedLanguage);
        Assert.True(response.LanguageMismatchDetected);
        Assert.False(response.SiblingPlanFound);
        Assert.True(response.SiblingPlanCreated);
        Assert.Equal("geminids-2026", sibling.SourceExternalEventId);
        Assert.Equal("IN-RJ-UDAIPUR", sibling.RegionId);
        Assert.Equal("[\"Short\",\"Long\"]", sibling.RequestedOutputTypesJson);
        Assert.Equal("hi", sibling.AstronomyEventIntelligence?.Language);
        Assert.Equal("geminids-2026", sibling.AstronomyEventIntelligence?.ExternalEventId);
        Assert.Equal(sibling.Id, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_PlanIdLanguageMismatch_CopiesEventObjectsToSiblingPlan()
    {
        await using var db = CreateDb();
        var sourcePlanId = Guid.NewGuid();
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = "JUPITER-VENUS-CONJUNCTION-2026",
            ExternalEventId = "jupiter-venus-conjunction-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "AutoGenerate",
            EventType = "PLANET_CONJUNCTION",
            Title = "Jupiter and Venus conjunction over Udaipur",
            Summary = "Jupiter and Venus conjunction",
            StartUtc = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RarityScore = 9,
            VisibilityScore = 9,
            AudienceInterestScore = 9,
            ContentOpportunityScore = 9,
            MetadataJson = "{\"eventLock\":{\"source\":\"verified\"}}",
            Objects =
            [
                new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet", ObjectRole = "Primary", CatalogId = "JUPITER", Magnitude = -2.1m, VisibilityScore = 9m, MetadataJson = "{\"source\":\"primaryObjects\"}" },
                new AstronomyEventObject { ObjectName = "Venus", ObjectType = "Planet", ObjectRole = "Primary", CatalogId = "VENUS", Magnitude = -4.1m, VisibilityScore = 9m, MetadataJson = "{\"source\":\"primaryObjects\"}" },
                new AstronomyEventObject { ObjectName = "Moon", ObjectType = "Moon", ObjectRole = "Secondary", CatalogId = "MOON", VisibilityScore = 7m, MetadataJson = "{\"source\":\"secondaryObjects\"}" }
            ]
        };
        db.AstronomyEventIntelligences.Add(intelligence);

        var plan = new ContentGenerationPlan
        {
            Title = "Jupiter and Venus conjunction over Udaipur",
            ContentCategoryCode = "RareEventAlert",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            ScheduledUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            Status = "Planned",
            PlanStatus = "Planned",
            Priority = 1,
            PriorityScore = 9,
            AstronomyEventIntelligenceId = intelligence.Id,
            AstronomyEventIntelligence = intelligence,
            SourceExternalEventId = "jupiter-venus-conjunction-2026",
            RequestedOutputTypesJson = "[\"Short\",\"Long\"]"
        };
        plan.AssignId(sourcePlanId);
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();

        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "hi",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: true,
            UseProductionPipeline: true,
            PlanId: sourcePlanId), CancellationToken.None);

        var sibling = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .SingleAsync(p => p.Language == "hi");
        var request = new ContentPlanProductionRequestMapper().Map(sibling, sibling.AstronomyEventIntelligence!);

        Assert.True(response.Success);
        Assert.True(response.SiblingPlanCreated);
        Assert.Equal("PLANET_CONJUNCTION", sibling.AstronomyEventIntelligence?.EventType);
        Assert.Equal("{\"eventLock\":{\"source\":\"verified\"}}", sibling.AstronomyEventIntelligence?.MetadataJson);
        Assert.Contains("Jupiter", request.PrimaryObjects);
        Assert.Contains("Venus", request.PrimaryObjects);
        Assert.Contains("Moon", request.SecondaryObjects);
        Assert.All(sibling.AstronomyEventIntelligence!.Objects, obj => Assert.False(string.IsNullOrWhiteSpace(obj.MetadataJson)));
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ExistingRequestedLanguageSiblingWithMissingObjects_LinksObjectsFromSourcePlan()
    {
        await using var db = CreateDb();
        var sourceIntelligence = new AstronomyEventIntelligence
        {
            EventCode = "JUPITER-VENUS-CONJUNCTION-2026",
            ExternalEventId = "jupiter-venus-conjunction-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "AutoGenerate",
            EventType = "PLANET_CONJUNCTION",
            Title = "Jupiter and Venus conjunction over Udaipur",
            Summary = "Jupiter and Venus conjunction",
            StartUtc = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RarityScore = 9,
            VisibilityScore = 9,
            AudienceInterestScore = 9,
            ContentOpportunityScore = 9,
            Objects =
            [
                new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet", ObjectRole = "Primary", CatalogId = "JUPITER" },
                new AstronomyEventObject { ObjectName = "Venus", ObjectType = "Planet", ObjectRole = "Secondary", CatalogId = "VENUS" }
            ]
        };
        var siblingIntelligence = new AstronomyEventIntelligence
        {
            EventCode = "JUPITER-VENUS-CONJUNCTION-2026-hi",
            ExternalEventId = sourceIntelligence.ExternalEventId,
            Year = 2026,
            Language = "hi",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "AutoGenerate",
            EventType = "PLANET_CONJUNCTION",
            Title = sourceIntelligence.Title,
            StartUtc = sourceIntelligence.StartUtc,
            PeakUtc = sourceIntelligence.PeakUtc,
            EndUtc = sourceIntelligence.EndUtc,
            RegionId = sourceIntelligence.RegionId,
            RarityScore = 9,
            VisibilityScore = 9,
            AudienceInterestScore = 9,
            ContentOpportunityScore = 9
        };
        db.AstronomyEventIntelligences.AddRange(sourceIntelligence, siblingIntelligence);

        var sourcePlan = new ContentGenerationPlan
        {
            Title = sourceIntelligence.Title,
            ContentCategoryCode = "RareEventAlert",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            ScheduledUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            Status = "Planned",
            PlanStatus = "Planned",
            Priority = 1,
            PriorityScore = 9,
            AstronomyEventIntelligenceId = sourceIntelligence.Id,
            AstronomyEventIntelligence = sourceIntelligence,
            SourceExternalEventId = sourceIntelligence.ExternalEventId,
            RequestedOutputTypesJson = "[\"Short\",\"Long\"]",
            PlannedObjectNamesJson = "[\"Jupiter\",\"Venus\"]",
            PrimaryCelestialObjectCode = "JUPITER"
        };
        var siblingPlan = new ContentGenerationPlan
        {
            Title = sourceIntelligence.Title,
            ContentCategoryCode = "RareEventAlert",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "hi",
            ScheduledUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            Status = "Planned",
            PlanStatus = "Planned",
            Priority = 1,
            PriorityScore = 9,
            AstronomyEventIntelligenceId = siblingIntelligence.Id,
            AstronomyEventIntelligence = siblingIntelligence,
            SourceExternalEventId = sourceIntelligence.ExternalEventId,
            RequestedOutputTypesJson = "[\"Short\",\"Long\"]"
        };
        db.ContentGenerationPlans.AddRange(sourcePlan, siblingPlan);
        await db.SaveChangesAsync();

        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "hi",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: true,
            UseProductionPipeline: true,
            PlanId: siblingPlan.Id), CancellationToken.None);

        var reloadedSibling = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .SingleAsync(p => p.Id == siblingPlan.Id);
        var request = new ContentPlanProductionRequestMapper().Map(reloadedSibling, reloadedSibling.AstronomyEventIntelligence!);

        Assert.True(response.Success);
        Assert.Equal(siblingPlan.Id, response.SelectedPlanId);
        Assert.Equal(["Jupiter"], request.PrimaryObjects);
        Assert.Equal(["Venus"], request.SecondaryObjects);
        Assert.Equal(2, reloadedSibling.AstronomyEventIntelligence!.Objects.Count);
        Assert.Equal("JUPITER", reloadedSibling.PrimaryCelestialObjectCode);
        Assert.Equal("[\"Jupiter\",\"Venus\"]", reloadedSibling.PlannedObjectNamesJson);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ExactManualValidationTitle_DoesNotBypassAutoGenerateAllowed()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db);
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            PlanTitles: ["Planet grouping window over Udaipur, Rajasthan, India"],
            UseProductionPipeline: true), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        var warning = Assert.Single(response.Warnings, warning => warning.RequestedTitle == "Planet grouping window over Udaipur, Rajasthan, India"
            && warning.Matched
            && !warning.Selected
            && warning.Reason.StartsWith("Excluded because linked astronomy event AutoGenerateAllowed was false.", StringComparison.Ordinal));
        Assert.Contains($"planId={ManualValidationPlanId:D}", warning.Reason);
        Assert.Contains("requestedPlanTitle=Planet grouping window over Udaipur, Rajasthan, India", warning.Reason);
        Assert.Contains("shouldBypassAutoGenerateAllowed=false", warning.Reason);
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ManualValidationPlanId_BypassesAutoGenerateAllowedWithWarning()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db, planningReason: "manual validation: Astronomy V1.2 Solar Eclipse validation");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            UseProductionPipeline: true,
            PlanId: ManualValidationPlanId), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(ManualValidationPlanId, response.PlanId);
        Assert.Equal(ManualValidationPlanId, Assert.Single(response.SelectedPlans).ContentGenerationPlanId);
        Assert.Contains(response.Warnings, warning => warning.RequestedTitle == ManualValidationPlanId.ToString("D")
            && warning.Selected
            && warning.Reason.StartsWith("Selected exact planId even though linked event AutoGenerateAllowed=false.", StringComparison.Ordinal));
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal(ManualValidationPlanId, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionCompletedManualValidationPlanId_BypassesAutoGenerateAllowedForExactPlanIdRerun()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            UseProductionPipeline: true,
            PlanId: ManualValidationPlanId,
            ExecutionMode: ContentPlanExecutionMode.RebuildOutputs,
            AllowCompletedPlanRerun: true,
            OverwriteExisting: true), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(ManualValidationPlanId, response.PlanId);
        Assert.Equal(ManualValidationPlanId, Assert.Single(response.SelectedPlans).ContentGenerationPlanId);
        var warning = Assert.Single(response.Warnings, warning => warning.RequestedTitle == ManualValidationPlanId.ToString("D")
            && warning.Matched
            && warning.Selected
            && warning.Reason.StartsWith("Selected exact planId even though linked event AutoGenerateAllowed=false.", StringComparison.Ordinal));
        Assert.Contains($"requestedPlanId={ManualValidationPlanId:D}", warning.Reason);
        Assert.Contains($"selectedPlanId={ManualValidationPlanId:D}", warning.Reason);
        Assert.Contains("exactPlanIdMode=true", warning.Reason);
        Assert.Contains("linkedEventAutoGenerateAllowed=false", warning.Reason);
        Assert.Contains("autoGenerateAllowedBypassed=true", warning.Reason);
        Assert.Contains("bypassReason=planId + useProductionPipeline + allowCompletedPlanRerun", warning.Reason);
        Assert.Contains("matchedDifferentPlanDetected=false", warning.Reason);
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal(ManualValidationPlanId, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_NeedsManualReviewManualValidationPlanId_BypassesWithWarning()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db, verificationStatus: "NeedsManualReview");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            UseProductionPipeline: true,
            PlanId: ManualValidationPlanId), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(ManualValidationPlanId, response.PlanId);
        Assert.Equal(ManualValidationPlanId, Assert.Single(response.SelectedPlans).ContentGenerationPlanId);
        Assert.Contains(response.Warnings, warning => warning.RequestedTitle == ManualValidationPlanId.ToString("D")
            && warning.Selected
            && warning.Reason.StartsWith("Selected exact planId even though linked event AutoGenerateAllowed=false.", StringComparison.Ordinal));
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal("NeedsManualReview", reloadedEvent.VerificationStatus);
        Assert.Equal(ManualValidationPlanId, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_AstronomyVValidationReasonWithoutManualPhrase_DoesNotBypassAutoGenerateAllowed()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db, planningReason: "Astronomy V1.2 Planet Grouping validation");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            UseProductionPipeline: true,
            PlanId: ManualValidationPlanId), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        var warning = Assert.Single(response.Warnings, warning => warning.RequestedTitle == ManualValidationPlanId.ToString("D")
            && warning.Matched
            && !warning.Selected
            && warning.Reason.StartsWith("Excluded because linked astronomy event AutoGenerateAllowed was false.", StringComparison.Ordinal));
        Assert.Contains($"planId={ManualValidationPlanId:D}", warning.Reason);
        Assert.Contains("GeneratedByAi=false", warning.Reason);
        Assert.Contains("PlanningReason=Astronomy V1.2 Planet Grouping validation", warning.Reason);
        Assert.Contains("isExactTarget=true", warning.Reason);
        Assert.Contains("isManualValidationPlan=false", warning.Reason);
        Assert.Contains("shouldBypassAutoGenerateAllowed=false", warning.Reason);
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_PartialManualValidationTitle_DoesNotBypassAutoGenerateAllowed()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db);
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            PlanTitles: ["Planet grouping"],
            UseProductionPipeline: true), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        var warning = Assert.Single(response.Warnings, warning => warning.RequestedTitle == "Planet grouping"
            && warning.Matched
            && !warning.Selected
            && warning.Reason.StartsWith("Excluded because linked astronomy event AutoGenerateAllowed was false.", StringComparison.Ordinal));
        Assert.Contains($"planId={ManualValidationPlanId:D}", warning.Reason);
        Assert.Contains("planTitle=Planet grouping window over Udaipur, Rajasthan, India", warning.Reason);
        Assert.Contains("GeneratedByAi=false", warning.Reason);
        Assert.Contains("PlanningReason=Astronomy V1.2 manual validation", warning.Reason);
        Assert.Contains("requestedPlanTitle=Planet grouping", warning.Reason);
        Assert.Contains("isExactTarget=false", warning.Reason);
        Assert.Contains("isManualValidationPlan=true", warning.Reason);
        Assert.Contains("shouldBypassAutoGenerateAllowed=false", warning.Reason);
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_AiGeneratedManualValidationTitle_DoesNotBypassAutoGenerateAllowed()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db, generatedByAi: true);
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            PlanTitles: ["Planet grouping window over Udaipur, Rajasthan, India"],
            UseProductionPipeline: true), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        var warning = Assert.Single(response.Warnings, warning => warning.RequestedTitle == "Planet grouping window over Udaipur, Rajasthan, India"
            && warning.Matched
            && !warning.Selected
            && warning.Reason.StartsWith("Excluded because linked astronomy event AutoGenerateAllowed was false.", StringComparison.Ordinal));
        Assert.Contains($"planId={ManualValidationPlanId:D}", warning.Reason);
        Assert.Contains("GeneratedByAi=true", warning.Reason);
        Assert.Contains("requestedPlanTitle=Planet grouping window over Udaipur, Rajasthan, India", warning.Reason);
        Assert.Contains("isExactTarget=false", warning.Reason);
        Assert.Contains("isManualValidationPlan=false", warning.Reason);
        Assert.Contains("shouldBypassAutoGenerateAllowed=false", warning.Reason);
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }


    [Fact]
    public async Task GenerateFromPlansAsync_MultipleManualValidationTitles_DoNotBypassAutoGenerateAllowed()
    {
        await using var db = CreateDb();
        var intelligence = SeedManualValidationPlan(db);
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = CreateService(db, legacy, production);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            DryRun: true,
            PlanTitles: ["Planet grouping window over Udaipur, Rajasthan, India", "Planet grouping window"],
            UseProductionPipeline: true), CancellationToken.None);

        var reloadedEvent = await db.AstronomyEventIntelligences.SingleAsync(e => e.Id == intelligence.Id);
        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        Assert.Contains(response.Warnings, warning => warning.RequestedTitle == "Planet grouping window over Udaipur, Rajasthan, India"
            && warning.Matched
            && !warning.Selected
            && warning.Reason.StartsWith("Excluded because linked astronomy event AutoGenerateAllowed was false.", StringComparison.Ordinal));
        Assert.DoesNotContain(response.Warnings, warning => warning.Reason.StartsWith("Selected exact planId even though linked event AutoGenerateAllowed=false.", StringComparison.Ordinal));
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }


    [Fact]
    public async Task GenerateFromPlansAsync_ForwardsOverwriteExistingToProductionPipeline()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db);
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true), CancellationToken.None);

        Assert.True(production.CapturedOverwriteExisting);
    }


    [Fact]
    public async Task GenerateFromPlansAsync_ProductionFailedWithoutRetryMode_IsExcluded()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionFailed", planStatus: "ProductionFailed");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        Assert.Contains(response.Warnings, warning => warning.RequestedTitle == "Geminids Meteor Shower Peak"
            && warning.Reason.Contains("ProductionFailed", StringComparison.OrdinalIgnoreCase)
            && !warning.Reason.Contains("allowed status or planStatus values are Draft, Planned, Approved, ProductionFailed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionFailedWithRetryMode_IsSelectedAndForwardsPhaseOptions()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionFailed", planStatus: "ProductionFailed");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true,
            StartPhaseNo: 1,
            EndPhaseNo: 19,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.UseProductionPipeline);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, response.PlanId);
        Assert.Equal(GeminidsPlanId, production.CapturedPlanId);
        Assert.True(production.CapturedOverwriteExisting);
        Assert.Equal(1, production.CapturedStartPhaseNo);
        Assert.Equal(19, production.CapturedEndPhaseNo);
        Assert.True(production.CapturedRetryFailedOnly);
        Assert.False(legacy.WasCalled);
    }


    [Fact]
    public async Task GenerateFromPlansAsync_ProductionRunningWithoutRecoveryMode_IsExcluded()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var runningExecution = SeedPipelineExecution(db, DateTimeOffset.UtcNow.AddMinutes(-5), "Running");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true,
            StartPhaseNo: 17,
            EndPhaseNo: 19), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        Assert.Contains(response.Warnings, warning => warning.RequestedTitle == "Geminids Meteor Shower Peak"
            && warning.Reason.Contains("ProductionRunning", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionRunningWithRecoveryMode_IsSelectedAndForwardsPhaseOptions()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var runningExecution = SeedPipelineExecution(db, DateTimeOffset.UtcNow.AddMinutes(-5), "Running");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: false,
            StartPhaseNo: 17,
            EndPhaseNo: 19,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true,
            AllowRunningPlanRecovery: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.UseProductionPipeline);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, response.PlanId);
        Assert.Equal(GeminidsPlanId, production.CapturedPlanId);
        Assert.Equal(17, production.CapturedStartPhaseNo);
        Assert.Equal(19, production.CapturedEndPhaseNo);
        Assert.True(production.CapturedRetryFailedOnly);
        var execution = await db.ContentPipelineExecutions.SingleAsync(e => e.Id == runningExecution.Id);
        Assert.Equal("Failed", execution.Status);
        Assert.Equal("Automatically marked failed due to stale running execution.", execution.ErrorMessage);
        Assert.Contains(response.Warnings, warning => warning.Reason.Contains($"Previous execution {runningExecution.Id:D} marked Failed", StringComparison.OrdinalIgnoreCase));
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionRunningRecoveryRequiresAllowRunningPlanRecoveryFlag()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            ExecutionMode: ContentPlanExecutionMode.RecoverRunning), CancellationToken.None));

        Assert.Contains("allowRunningPlanRecovery=true", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
    }



    [Fact]
    public async Task GenerateFromPlansAsync_ProductionCompletedWithRebuildOutputs_IsSelectedAndForwardsRerunOptions()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true,
            ExecutionMode: ContentPlanExecutionMode.RebuildOutputs,
            AllowCompletedPlanRerun: true,
            ArchivePreviousRun: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(ContentPlanExecutionMode.RebuildOutputs, production.CapturedExecutionMode);
        Assert.True(production.CapturedAllowCompletedPlanRerun);
        Assert.True(production.CapturedArchivePreviousRun);
        Assert.Equal(3, production.CapturedStartPhaseNo);
        Assert.Equal(20, production.CapturedEndPhaseNo);
        Assert.Equal(ContentPlanExecutionMode.RebuildOutputs, response.ExecutionMode);
        Assert.True(response.CompletedPlanRerun);
        Assert.True(response.PreviousOutputArchived);
        Assert.Equal("/archive/geminids", response.ArchivePath);
        Assert.False(legacy.WasCalled);
    }


    [Fact]
    public async Task ExecuteContentPlanWithProductionPipelineAsync_RebuildOutputs_ExpandsRequestedRangeForPrerequisites()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "astro-rebuild-range-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ContentPlanProductionExecutionService(
                db,
                new ContentPlanProductionRequestMapper(),
                new ThrowingProductionPipelineExecutionService(),
                Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
                NullLogger<ContentPlanProductionExecutionService>.Instance);

            var response = await service.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(
                GeminidsPlanId,
                DryRun: true,
                OverwriteExisting: true,
                StartPhaseNo: 10,
                EndPhaseNo: 12,
                ExecutionMode: ContentPlanExecutionMode.RebuildOutputs,
                AllowCompletedPlanRerun: true), CancellationToken.None);

            Assert.True(response.Success);
            Assert.Equal(10, response.RequestedStartPhase);
            Assert.Equal(12, response.RequestedEndPhase);
            Assert.Equal(3, response.ExpandedStartPhase);
            Assert.Equal(12, response.ExpandedEndPhase);
            Assert.True(response.DependencyExpansionApplied);
            Assert.Equal(3, response.StartPhaseNo);
            Assert.Equal(12, response.EndPhaseNo);
            Assert.Contains("Expanded rebuild range from 10-12 to 3-12 due to prerequisite dependencies.", response.Warnings);
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }


    [Fact]
    public async Task ExecuteContentPlanWithProductionPipelineAsync_RebuildOutputs_PartialRangeSuccessIgnoresDownstreamCompletion()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "astro-partial-rebuild-success-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ContentPlanProductionExecutionService(
                db,
                new ContentPlanProductionRequestMapper(),
                new PartialRebuildProductionPipelineExecutionService(),
                Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
                NullLogger<ContentPlanProductionExecutionService>.Instance);

            var response = await service.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(
                GeminidsPlanId,
                DryRun: false,
                OverwriteExisting: true,
                StartPhaseNo: 10,
                EndPhaseNo: 12,
                ExecutionMode: ContentPlanExecutionMode.RebuildOutputs,
                AllowCompletedPlanRerun: true), CancellationToken.None);

            Assert.True(response.Success);
            Assert.True(response.PartialPhaseExecution);
            Assert.True(response.PartialPhaseSuccess);
            Assert.True(response.ThumbnailsGenerated);
            Assert.Null(response.HeroGenerated);
            Assert.Null(response.ShortVideoGenerated);
            Assert.Null(response.LongVideoGenerated);
            Assert.Equal(10, response.RequestedStartPhase);
            Assert.Equal(12, response.RequestedEndPhase);
            Assert.Equal(3, response.ExpandedStartPhase);
            Assert.Equal(12, response.ExpandedEndPhase);
            Assert.Equal(Enumerable.Range(10, 3), response.PhaseResults!.Select(p => p.PhaseNo));
            Assert.All(response.PhaseResults!, phase => Assert.Equal(ProductionPhaseStatus.Succeeded, phase.Status));
            var shortVideoCompletion = response.RequestedOutputCompletion!.Single(output => output.OutputType == "ShortVideo");
            Assert.Equal("OutOfScope", shortVideoCompletion.Status);
            Assert.Empty(shortVideoCompletion.SucceededPhases);
            Assert.Equal([16, 18], shortVideoCompletion.RequiredPhases);
            Assert.DoesNotContain("ShortVideo output incomplete outside requested rebuild range.", response.Errors);
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteContentPlanWithProductionPipelineAsync_StartAndEndPhaseRequest_IsPartialSuccessRegardlessOfExecutionMode()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "astro-partial-range-success-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ContentPlanProductionExecutionService(
                db,
                new ContentPlanProductionRequestMapper(),
                new PartialRebuildProductionPipelineExecutionService(),
                Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
                NullLogger<ContentPlanProductionExecutionService>.Instance);

            var response = await service.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(
                GeminidsPlanId,
                DryRun: false,
                OverwriteExisting: true,
                StartPhaseNo: 10,
                EndPhaseNo: 12,
                ExecutionMode: ContentPlanExecutionMode.Normal,
                AllowCompletedPlanRerun: true), CancellationToken.None);

            Assert.True(response.Success);
            Assert.True(response.PartialPhaseExecution);
            Assert.True(response.PartialPhaseSuccess);
            Assert.Equal(10, response.RequestedStartPhase);
            Assert.Equal(12, response.RequestedEndPhase);
            Assert.Equal(10, response.ExpandedStartPhase);
            Assert.Equal(12, response.ExpandedEndPhase);
            Assert.Equal(Enumerable.Range(10, 3), response.PhaseResults!.Select(p => p.PhaseNo));
            Assert.All(response.PhaseResults!, phase => Assert.Equal(ProductionPhaseStatus.Succeeded, phase.Status));
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }


    [Fact]
    public async Task ExecuteContentPlanWithProductionPipelineAsync_RebuildOutputs_DoesNotDeleteQuestionEngineWhenPhase3IsNotRegenerated()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var workingDirectory = Path.Combine(Path.GetTempPath(), "astro-rebuild-preserve-question-engine-" + Guid.NewGuid().ToString("N"));
        try
        {
            var outputRoot = BuildGeminidsOutputRoot(workingDirectory);
            var questionEngineRoot = Path.Combine(outputRoot, "question-engine");
            Directory.CreateDirectory(questionEngineRoot);
            var questionAnswerSetPath = Path.Combine(questionEngineRoot, "question-answer-set.json");
            await File.WriteAllTextAsync(questionAnswerSetPath, "{}", CancellationToken.None);

            var service = new ContentPlanProductionExecutionService(
                db,
                new ContentPlanProductionRequestMapper(),
                new CompleteProductionPipelineExecutionService(),
                Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
                NullLogger<ContentPlanProductionExecutionService>.Instance);

            var response = await service.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(
                GeminidsPlanId,
                DryRun: false,
                OverwriteExisting: true,
                StartPhaseNo: 4,
                EndPhaseNo: 4,
                ExecutionMode: ContentPlanExecutionMode.RebuildOutputs,
                AllowCompletedPlanRerun: true), CancellationToken.None);

            Assert.True(response.Success);
            Assert.Equal(4, response.StartPhaseNo);
            Assert.Equal(4, response.EndPhaseNo);
            Assert.False(response.DependencyExpansionApplied);
            Assert.True(File.Exists(questionAnswerSetPath));
            Assert.DoesNotContain(response.DeletedOutputFolders!, path => path.EndsWith("question-engine", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionPipeline_InvokesVisualIntelligenceAndWritesDiagnosticsUnderPlanOutput()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db);
        var workingDirectory = Path.Combine(Path.GetTempPath(), "astro-visual-intelligence-enabled-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = new ThrowingLegacyPipeline();
            var visualIntelligence = new CapturingVisualIntelligenceOrchestrator(writeDiagnostics: true);
            var production = new ContentPlanProductionExecutionService(
                db,
                new ContentPlanProductionRequestMapper(),
                new CompleteProductionPipelineExecutionService(),
                Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
                NullLogger<ContentPlanProductionExecutionService>.Instance,
                Options.Create(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true, DefaultProvider = ImageProviderType.AzureImage }),
                visualIntelligence);
            var service = CreateService(db, legacy, production);

            var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
                Year: 2026,
                RegionId: "IN-RJ-UDAIPUR",
                Language: "en",
                MaxPlans: 1,
                OnlyHighPriority: true,
                DryRun: false,
                UseProductionPipeline: true,
                OverwriteExisting: true), CancellationToken.None);

            var diagnosticsRoot = Path.Combine(BuildGeminidsOutputRoot(workingDirectory), "diagnostics", "visual-intelligence");
            Assert.True(response.Success);
            Assert.Equal(1, visualIntelligence.CallCount);
            Assert.NotNull(visualIntelligence.LastRequest);
            Assert.Equal(GeminidsPlanId, visualIntelligence.LastRequest!.ContentGenerationPlanId);
            Assert.Equal("MeteorShower", visualIntelligence.LastRequest.EventType);
            Assert.Equal("en", visualIntelligence.LastRequest.Language);
            Assert.Equal("IN-RJ-UDAIPUR", visualIntelligence.LastRequest.Region);
            Assert.Equal(Platform.YouTubeLongForm, visualIntelligence.LastRequest.Platform);
            Assert.Equal(AspectRatio.Landscape16x9, visualIntelligence.LastRequest.AspectRatio);
            Assert.Equal(ImageProviderType.AzureImage, visualIntelligence.LastRequest.RequestedProvider);
            Assert.Equal(BuildGeminidsOutputRoot(workingDirectory), visualIntelligence.LastRequest.RunOutputFolder);
            Assert.True(File.Exists(Path.Combine(diagnosticsRoot, "OrchestrationSummary.json")));
            Assert.True(File.Exists(Path.Combine(diagnosticsRoot, "CDL.json")));
            Assert.True(File.Exists(Path.Combine(diagnosticsRoot, "CreativeDirectionContract.json")));
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteContentPlanWithProductionPipelineAsync_VisualIntelligenceDisabled_IsNoOp()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db);
        var workingDirectory = Path.Combine(Path.GetTempPath(), "astro-visual-intelligence-disabled-" + Guid.NewGuid().ToString("N"));
        try
        {
            var visualIntelligence = new CapturingVisualIntelligenceOrchestrator(writeDiagnostics: true);
            var service = new ContentPlanProductionExecutionService(
                db,
                new ContentPlanProductionRequestMapper(),
                new CompleteProductionPipelineExecutionService(),
                Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
                NullLogger<ContentPlanProductionExecutionService>.Instance,
                Options.Create(new VisualIntelligenceOptions { Enabled = false, WriteDiagnostics = true }),
                visualIntelligence);

            var response = await service.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(GeminidsPlanId, DryRun: false, OverwriteExisting: true), CancellationToken.None);

            Assert.True(response.Success);
            Assert.Equal(0, visualIntelligence.CallCount);
            Assert.False(Directory.Exists(Path.Combine(BuildGeminidsOutputRoot(workingDirectory), "diagnostics", "visual-intelligence")));
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteContentPlanWithProductionPipelineAsync_VisualIntelligenceFailure_IsNonBlocking()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db);
        var workingDirectory = Path.Combine(Path.GetTempPath(), "astro-visual-intelligence-failure-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ContentPlanProductionExecutionService(
                db,
                new ContentPlanProductionRequestMapper(),
                new CompleteProductionPipelineExecutionService(),
                Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
                NullLogger<ContentPlanProductionExecutionService>.Instance,
                Options.Create(new VisualIntelligenceOptions { Enabled = true, WriteDiagnostics = true, UseVisualCreativeDirector = true }),
                new ThrowingVisualIntelligenceOrchestrator());

            var response = await service.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(GeminidsPlanId, DryRun: false, OverwriteExisting: true), CancellationToken.None);

            Assert.True(response.Success);
            Assert.Empty(response.Errors);
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateFromPlansAsync_ProductionCompletedWithoutExplicitFlag_IsRejected()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true,
            ExecutionMode: ContentPlanExecutionMode.RebuildOutputs), CancellationToken.None));

        Assert.Contains("allowCompletedPlanRerun=true", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_StaleProductionRunningExecution_IsRecoveredAndRetryProceeds()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var staleExecution = SeedPipelineExecution(db, DateTimeOffset.UtcNow.AddMinutes(-45), "Running");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            StartPhaseNo: 17,
            EndPhaseNo: 19,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true), CancellationToken.None);

        var plan = await db.ContentGenerationPlans.SingleAsync(p => p.Id == GeminidsPlanId);
        var execution = await db.ContentPipelineExecutions.SingleAsync(e => e.Id == staleExecution.Id);
        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal(GeminidsPlanId, production.CapturedPlanId);
        Assert.Equal("ProductionFailed", plan.Status);
        Assert.Equal("ProductionFailed", plan.PlanStatus);
        Assert.Equal("Failed", execution.Status);
        Assert.NotNull(execution.FinishedUtc);
        Assert.Equal("Automatically marked failed due to stale running execution.", execution.ErrorMessage);
        Assert.Contains(response.Warnings, warning => warning.Reason.Contains($"Previous execution {staleExecution.Id:D} marked Failed", StringComparison.OrdinalIgnoreCase));
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_RecentProductionRunningExecution_BlocksWithoutRecoveryFlag()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionRunning", planStatus: "ProductionRunning");
        var recentExecution = SeedPipelineExecution(db, DateTimeOffset.UtcNow.AddMinutes(-5), "Running");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            StartPhaseNo: 17,
            EndPhaseNo: 19,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true), CancellationToken.None);

        var execution = await db.ContentPipelineExecutions.SingleAsync(e => e.Id == recentExecution.Id);
        Assert.True(response.Success);
        Assert.Equal(0, response.SelectedPlanCount);
        Assert.Equal("Running", execution.Status);
        Assert.Null(execution.FinishedUtc);
        Assert.Equal(Guid.Empty, production.CapturedPlanId);
        Assert.Contains(response.Warnings, warning => warning.Reason.Contains("ProductionRunning", StringComparison.OrdinalIgnoreCase));
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_CompletedAndFailedPlans_AreNotRecovered()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionFailed", planStatus: "ProductionFailed");
        var runningExecution = SeedPipelineExecution(db, DateTimeOffset.UtcNow.AddMinutes(-45), "Running");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            StartPhaseNo: 17,
            EndPhaseNo: 19,
            RetryFailedOnly: true,
            AllowFailedPlanRetry: true), CancellationToken.None);

        var execution = await db.ContentPipelineExecutions.SingleAsync(e => e.Id == runningExecution.Id);
        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal("Running", execution.Status);
        Assert.Null(execution.FinishedUtc);
        Assert.DoesNotContain(response.Warnings, warning => warning.Reason.Contains("Recovered stale running execution", StringComparison.OrdinalIgnoreCase));
        Assert.False(legacy.WasCalled);
    }

    [Fact]
    public async Task GenerateFromPlansAsync_CompletedPlans_AreNotRecovered()
    {
        await using var db = CreateDb();
        SeedGeminidsPlan(db, status: "ProductionCompleted", planStatus: "ProductionCompleted");
        var runningExecution = SeedPipelineExecution(db, DateTimeOffset.UtcNow.AddMinutes(-45), "Running");
        var legacy = new ThrowingLegacyPipeline();
        var production = new CapturingProductionExecutionService();
        var service = new ContentPlanBatchGenerationService(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

        var response = await service.GenerateFromPlansAsync(new BatchGenerateFromPlansRequest(
            Year: 2026,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            MaxPlans: 1,
            OnlyHighPriority: true,
            DryRun: false,
            PlanTitles: ["Geminids Meteor Shower Peak"],
            UseProductionPipeline: true,
            OverwriteExisting: true,
            ExecutionMode: ContentPlanExecutionMode.RebuildOutputs,
            AllowCompletedPlanRerun: true), CancellationToken.None);

        var execution = await db.ContentPipelineExecutions.SingleAsync(e => e.Id == runningExecution.Id);
        Assert.True(response.Success);
        Assert.Equal(1, response.SelectedPlanCount);
        Assert.Equal("Running", execution.Status);
        Assert.Null(execution.FinishedUtc);
        Assert.DoesNotContain(response.Warnings, warning => warning.Reason.Contains("Recovered stale running execution", StringComparison.OrdinalIgnoreCase));
        Assert.False(legacy.WasCalled);
    }

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static ContentPlanBatchGenerationService CreateService(MediaFactoryDbContext db, ThrowingLegacyPipeline legacy, CapturingProductionExecutionService production)
        => new(
            db,
            legacy,
            legacy,
            legacy,
            legacy,
            production,
            new ProductionRunningRecoveryService(db, Options.Create(new ProductionPipelineOptions()), NullLogger<ProductionRunningRecoveryService>.Instance),
            Options.Create(new ProductionPipelineOptions()),
            NullLogger<ContentPlanBatchGenerationService>.Instance);

    private static AstronomyEventIntelligence SeedManualValidationPlan(MediaFactoryDbContext db, bool generatedByAi = false, string planningReason = "Astronomy V1.2 manual validation", string verificationStatus = "Verified", string status = "Draft", string planStatus = "Draft")
    {
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = "PLANET-GROUPING-UDAIPUR-2026",
            ExternalEventId = "planet-grouping-udaipur-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = verificationStatus,
            AutoGenerateAllowed = false,
            ContentStrategy = "ManualValidationCandidate",
            EventType = "PLANET_GROUPING",
            Title = "Planet grouping window over Udaipur, Rajasthan, India",
            Summary = "Manual validation planet grouping",
            StartUtc = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RarityScore = 8,
            VisibilityScore = 8,
            AudienceInterestScore = 8,
            ContentOpportunityScore = 8
        };
        db.AstronomyEventIntelligences.Add(intelligence);

        var plan = new ContentGenerationPlan
        {
            Title = "Planet grouping window over Udaipur, Rajasthan, India",
            ContentCategoryCode = "CosmicStoryShort",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            ScheduledUtc = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
            Status = status,
            PlanStatus = planStatus,
            Priority = 40,
            PriorityScore = 8,
            GeneratedByAi = generatedByAi,
            ManualValidation = !generatedByAi,
            PlanningReason = planningReason,
            AstronomyEventIntelligenceId = intelligence.Id,
            AstronomyEventIntelligence = intelligence,
            SourceExternalEventId = "planet-grouping-udaipur-2026",
            RequestedOutputTypesJson = "[\"ShortVideo\",\"LongVideo\"]"
        };
        plan.AssignId(ManualValidationPlanId);
        db.ContentGenerationPlans.Add(plan);
        db.SaveChanges();
        return intelligence;
    }

    private static void SeedGeminidsPlan(MediaFactoryDbContext db, string status = "Planned", string planStatus = "Planned")
    {
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = "GEMINIDS-2026",
            ExternalEventId = "geminids-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "AutoGenerate",
            EventType = "MeteorShower",
            Title = "Geminids Meteor Shower Peak",
            Summary = "Geminids",
            StartUtc = new DateTimeOffset(2026, 12, 13, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RarityScore = 9,
            VisibilityScore = 9,
            AudienceInterestScore = 9,
            ContentOpportunityScore = 9
        };
        db.AstronomyEventIntelligences.Add(intelligence);

        var plan = new ContentGenerationPlan
        {
            Title = "Geminids Meteor Shower Peak",
            ContentCategoryCode = "RareEventAlert",
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            ScheduledUtc = new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
            Status = status,
            PlanStatus = planStatus,
            Priority = 1,
            PriorityScore = 9,
            AstronomyEventIntelligenceId = intelligence.Id,
            AstronomyEventIntelligence = intelligence,
            SourceExternalEventId = "geminids-2026",
            RequestedOutputTypesJson = "[\"Short\",\"Long\"]"
        };
        plan.AssignId(GeminidsPlanId);
        db.ContentGenerationPlans.Add(plan);
        db.SaveChanges();
    }

    private static string BuildGeminidsOutputRoot(string workingDirectory)
        => Path.Combine(workingDirectory, "plans", "IN-RJ-UDAIPUR", "2026", GeminidsPlanId.ToString("D"));

    private static ContentPipelineExecution SeedPipelineExecution(MediaFactoryDbContext db, DateTimeOffset startedUtc, string status, DateTimeOffset? finishedUtc = null)
    {
        var execution = new ContentPipelineExecution
        {
            ContentGenerationPlanId = GeminidsPlanId,
            ContentCategoryCode = "RareEventAlert",
            StartedUtc = startedUtc,
            FinishedUtc = finishedUtc,
            Status = status,
            OutputFolder = @"D:\AstronomyWorkspace\Astronomy\media-output\plans\IN-RJ-UDAIPUR\2026\2af19a66-3777-47c7-8672-6e9d6245ac1c"
        };
        db.ContentPipelineExecutions.Add(execution);
        db.SaveChanges();
        return execution;
    }

    private sealed class CapturingProductionExecutionService : IContentPlanProductionExecutionService
    {
        public static readonly string[] ExpectedSteps =
        [
            "Question Engine",
            "Scene Engine Short",
            "Scene Engine Long",
            "Hero Engine",
            "Thumbnail Engine",
            "Narration Short",
            "Narration Long",
            "TTS Short",
            "TTS Long",
            "Video Assembly Short",
            "Video Assembly Long"
        ];

        public Guid CapturedPlanId { get; private set; }
        public bool CapturedDryRun { get; private set; }
        public bool CapturedOverwriteExisting { get; private set; }
        public int? CapturedStartPhaseNo { get; private set; }
        public int? CapturedEndPhaseNo { get; private set; }
        public bool CapturedRetryFailedOnly { get; private set; }
        public ContentPlanExecutionMode CapturedExecutionMode { get; private set; }
        public bool CapturedAllowCompletedPlanRerun { get; private set; }
        public bool CapturedArchivePreviousRun { get; private set; }

        public Task<ContentPlanProductionExecutionResult> ExecuteContentPlanAsync(Guid contentGenerationPlanId, bool dryRun, bool overwriteExisting, CancellationToken cancellationToken)
        {
            CapturedPlanId = contentGenerationPlanId;
            CapturedDryRun = dryRun;
            CapturedOverwriteExisting = overwriteExisting;

            var request = new ContentPlanProductionPipelineRequest(
                contentGenerationPlanId,
                "RareEventAlert",
                "Geminids Meteor Shower Peak",
                "Geminids",
                "MeteorShower",
                "IN-RJ-UDAIPUR",
                "en",
                ["Geminids"],
                [],
                null,
                new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
                null,
                new DateTimeOffset(2026, 12, 14, 0, 0, 0, TimeSpan.Zero),
                "geminids-2026",
                null,
                ["Short", "Long"],
                9,
                9,
                9,
                9,
                "Verified",
                null,
                "AutoGenerate",
                null,
                null,
                "IN-RJ-UDAIPUR",
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                []);

            return Task.FromResult(new ContentPlanProductionExecutionResult(
                true,
                dryRun,
                true,
                false,
                1,
                contentGenerationPlanId,
                "Geminids Meteor Shower Peak",
                @"D:\AstronomyWorkspace\Astronomy\media-output\plans\IN-RJ-UDAIPUR\2026\2af19a66-3777-47c7-8672-6e9d6245ac1c",
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                string.Empty,
                string.Empty,
                request,
                ExpectedSteps,
                [],
                [],
                [],
                ExecutionMode: CapturedExecutionMode,
                CompletedPlanRerun: CapturedAllowCompletedPlanRerun,
                PreviousOutputArchived: CapturedArchivePreviousRun,
                ArchivePath: CapturedArchivePreviousRun ? "/archive/geminids" : null,
                DeletedOutputFolders: [],
                StartPhaseNo: CapturedStartPhaseNo,
                EndPhaseNo: CapturedEndPhaseNo));
        }

        public Task<ContentPlanProductionExecutionResult> ExecuteContentPlanWithProductionPipelineAsync(ContentPlanProductionExecutionRequest request, CancellationToken cancellationToken)
        {
            CapturedStartPhaseNo = request.StartPhaseNo;
            CapturedEndPhaseNo = request.EndPhaseNo;
            CapturedRetryFailedOnly = request.RetryFailedOnly;
            CapturedExecutionMode = request.ExecutionMode;
            CapturedAllowCompletedPlanRerun = request.AllowCompletedPlanRerun;
            CapturedArchivePreviousRun = request.ArchivePreviousRun;
            return ExecuteContentPlanAsync(request.ContentGenerationPlanId, request.DryRun, request.OverwriteExisting, cancellationToken);
        }
    }


    private sealed class PartialRebuildProductionPipelineExecutionService : IProductionPipelineExecutionService
    {
        public Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var phaseResults = Enumerable.Range(request.RequestedStartPhaseNo!.Value, request.RequestedEndPhaseNo!.Value - request.RequestedStartPhaseNo!.Value + 1)
                .Select(phaseNo => new ProductionPhaseResult(
                    phaseNo,
                    $"Phase {phaseNo}",
                    ProductionPhaseStatus.Succeeded,
                    now,
                    now,
                    0,
                    [],
                    [],
                    null,
                    [],
                    [],
                    false))
                .ToArray();

            return Task.FromResult(new ProductionPipelineExecutionResult(
                Success: false,
                DryRun: false,
                QuestionEngineCompleted: true,
                ShortScenesGenerated: true,
                LongScenesGenerated: true,
                HeroGenerated: true,
                ThumbnailsGenerated: true,
                ShortNarrationGenerated: false,
                LongNarrationGenerated: false,
                ShortTtsGenerated: false,
                LongTtsGenerated: false,
                ShortVideoGenerated: false,
                LongVideoGenerated: false,
                FinalShortVideoPath: string.Empty,
                FinalLongVideoPath: string.Empty,
                GeneratedFiles: [],
                Warnings: [],
                Errors: [],
                PhaseResults: phaseResults,
                RequestedOutputCompletion:
                [
                    new RequestedOutputCompletion("ShortVideo", true, "OutOfScope", [16, 18], [], [], []),
                    new RequestedOutputCompletion("LongVideo", true, "OutOfScope", [15, 16, 17, 18, 19], [], [], []),
                    new RequestedOutputCompletion("HeroAsset", true, "OutOfScope", [11], [], [], []),
                    new RequestedOutputCompletion("Thumbnail", true, "Succeeded", [12], [12], [], [])
                ]));
        }
    }

    private sealed class CompleteProductionPipelineExecutionService : IProductionPipelineExecutionService
    {
        public Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProductionPipelineExecutionResult(
                Success: true,
                DryRun: false,
                QuestionEngineCompleted: File.Exists(Path.Combine(request.OutputRoot, "question-engine", "question-answer-set.json")),
                ShortScenesGenerated: true,
                LongScenesGenerated: true,
                HeroGenerated: true,
                ThumbnailsGenerated: true,
                ShortNarrationGenerated: true,
                LongNarrationGenerated: true,
                ShortTtsGenerated: true,
                LongTtsGenerated: true,
                ShortVideoGenerated: true,
                LongVideoGenerated: true,
                FinalShortVideoPath: Path.Combine(request.OutputRoot, "video-assembly", "short", "final-video-short.mp4"),
                FinalLongVideoPath: Path.Combine(request.OutputRoot, "video-assembly", "long", "final-video-long.mp4"),
                GeneratedFiles: [],
                Warnings: [],
                Errors: [],
                PhaseResults:
                [
                    new ProductionPhaseResult(
                        20,
                        "Phase 20",
                        ProductionPhaseStatus.Succeeded,
                        now,
                        now,
                        0,
                        [],
                        [],
                        null,
                        [],
                        [],
                        false)
                ],
                StartPhaseNo: request.StartPhaseNo,
                EndPhaseNo: request.EndPhaseNo));
        }
    }

    private sealed class SuccessfulProductionPipelineExecutionService : IProductionPipelineExecutionService
    {
        public Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ProductionPipelineExecutionResult(
                Success: true,
                DryRun: false,
                QuestionEngineCompleted: File.Exists(Path.Combine(request.OutputRoot, "question-engine", "question-answer-set.json")),
                ShortScenesGenerated: false,
                LongScenesGenerated: false,
                HeroGenerated: false,
                ThumbnailsGenerated: false,
                ShortNarrationGenerated: false,
                LongNarrationGenerated: false,
                ShortTtsGenerated: false,
                LongTtsGenerated: false,
                ShortVideoGenerated: false,
                LongVideoGenerated: false,
                FinalShortVideoPath: string.Empty,
                FinalLongVideoPath: string.Empty,
                GeneratedFiles: [],
                Warnings: [],
                Errors: [],
                StartPhaseNo: request.StartPhaseNo,
                EndPhaseNo: request.EndPhaseNo));
    }

    private sealed class ThrowingProductionPipelineExecutionService : IProductionPipelineExecutionService
    {
        public Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Dry-run range expansion should not execute the production pipeline.");
    }

    private sealed class CapturingVisualIntelligenceOrchestrator(bool writeDiagnostics) : IVisualIntelligenceOrchestrator
    {
        public int CallCount { get; private set; }
        public VisualIntelligenceOrchestrationRequest? LastRequest { get; private set; }

        public async Task<VisualIntelligenceOrchestrationResult> OrchestrateAsync(VisualIntelligenceOrchestrationRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            var context = new VisualIntelligenceOrchestrationContext
            {
                CorrelationId = request.CorrelationId ?? Guid.NewGuid().ToString("N"),
                ContentGenerationPlanId = request.ContentGenerationPlanId,
                AstronomyEventIntelligenceId = request.AstronomyEventIntelligenceId,
                EventType = request.EventType,
                EventName = request.EventName,
                Language = request.Language,
                Platform = request.Platform,
                AspectRatio = request.AspectRatio,
                Region = request.Region,
                PrimaryObjects = request.PrimaryObjects,
                SupportingObjects = request.SupportingObjects,
                RunOutputFolder = request.RunOutputFolder,
                RequestedProvider = request.RequestedProvider,
                FeatureFlags = new VisualIntelligenceFlagSnapshot { Enabled = true, WriteDiagnostics = writeDiagnostics, UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true }
            };
            var cdl = new CDL { DocumentId = $"cdl-{context.CorrelationId}" };
            var contract = new CreativeDirectionContract { ContractId = $"cdc-{context.CorrelationId}", Cdl = cdl, TargetPlatform = request.Platform, AspectRatio = request.AspectRatio, Language = request.Language };
            if (writeDiagnostics && !string.IsNullOrWhiteSpace(request.RunOutputFolder))
            {
                var diagnosticsRoot = Path.Combine(request.RunOutputFolder, "diagnostics", "visual-intelligence");
                Directory.CreateDirectory(diagnosticsRoot);
                await File.WriteAllTextAsync(Path.Combine(diagnosticsRoot, "OrchestrationSummary.json"), "{}", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(diagnosticsRoot, "CDL.json"), "{}", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(diagnosticsRoot, "CreativeDirectionContract.json"), "{}", cancellationToken);
            }

            return new VisualIntelligenceOrchestrationResult
            {
                Status = VisualIntelligenceOrchestrationStatus.Success,
                Context = context,
                Cdl = cdl,
                CreativeDirectionContract = contract,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private sealed class ThrowingVisualIntelligenceOrchestrator : IVisualIntelligenceOrchestrator
    {
        public Task<VisualIntelligenceOrchestrationResult> OrchestrateAsync(VisualIntelligenceOrchestrationRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Visual Intelligence test failure.");
    }

    private sealed class ThrowingLegacyPipeline : IAstronomyAssetPlanningService, IAstronomyAssetProductionJobService, IVisualAssetGenerationService, ISceneRenderer
    {
        public bool WasCalled { get; private set; }

        public Task<AstronomyAssetPlanningResult> GenerateAssetPlansAsync(AstronomyAssetPlanningRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }

        public Task<AstronomyAssetProductionJobResult> CreateAssetProductionJobsAsync(AstronomyAssetProductionJobRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }

        public Task<VisualAssetGenerationResponse> GenerateVisualAssetsAsync(VisualAssetGenerationRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }

        public Task<SceneRenderingResponse> RenderScenesAsync(SceneRenderingRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Placeholder planning pipeline must not be called in production mode.");
        }
    }
}
