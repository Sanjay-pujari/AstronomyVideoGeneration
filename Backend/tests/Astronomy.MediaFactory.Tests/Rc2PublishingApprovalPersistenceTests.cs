using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2PublishingApprovalPersistenceTests
{
    [Fact]
    public void Model_maps_approval_identity_audit_indexes_and_restrictive_plan_relationship()
    {
        using var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        var entity = db.Model.FindEntityType(typeof(Rc2PublishingApproval));

        Assert.NotNull(entity);
        Assert.Equal("rc2_publishing_approvals", entity.GetTableName());
        Assert.Equal(nameof(Rc2PublishingApproval.Id), Assert.Single(entity.FindPrimaryKey()!.Properties).Name);

        foreach (var propertyName in new[]
                 {
                     nameof(Rc2PublishingApproval.PlanId),
                     nameof(Rc2PublishingApproval.Phase20AuthorityChecksum),
                     nameof(Rc2PublishingApproval.PublishingPackageId),
                     nameof(Rc2PublishingApproval.Decision),
                     nameof(Rc2PublishingApproval.DecisionUtc),
                     nameof(Rc2PublishingApproval.DecisionSource),
                     nameof(Rc2PublishingApproval.CreatedUtc),
                     nameof(Rc2PublishingApproval.UpdatedUtc)
                 })
            Assert.NotNull(entity.FindProperty(propertyName));

        Assert.Contains(entity.GetIndexes(), index =>
            !index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Rc2PublishingApproval.PlanId)]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(Rc2PublishingApproval.PlanId),
                nameof(Rc2PublishingApproval.Phase20AuthorityChecksum),
                nameof(Rc2PublishingApproval.PublishingPackageId)
            ]));

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(typeof(ContentGenerationPlan), foreignKey.PrincipalEntityType.ClrType);
    }

    [Fact]
    public async Task Persistence_failure_returns_controlled_error_without_database_details()
    {
        var controller = new Rc2PublishingController(
            new FailingPublishingControlService(),
            NullLogger<Rc2PublishingController>.Instance);

        var result = await controller.Approve(
            new Rc2SetPublishingApprovalRequest(Guid.NewGuid(), Rc2PublishingApprovalStatus.Approved),
            CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        var payload = System.Text.Json.JsonSerializer.Serialize(error.Value);
        Assert.Contains("RC2_PUBLISH_PERSISTENCE_FAILED", payload);
        Assert.DoesNotContain("42P01", payload);
        Assert.DoesNotContain("rc2_publishing_approvals", payload);
    }

    private sealed class FailingPublishingControlService : IRc2PublishingControlService
    {
        public Task<Rc2PublishingPackageResponse> CreateOrRefreshPackageAsync(Guid planId, bool overwriteExisting,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Rc2PublishingApprovalResponse> SetApprovalAsync(Guid planId, Rc2PublishingApprovalStatus decision,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
            "Npgsql 42P01: relation rc2_publishing_approvals does not exist");

        public Task<Rc2PublishingStatusResponse> GetStatusAsync(Guid planId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
