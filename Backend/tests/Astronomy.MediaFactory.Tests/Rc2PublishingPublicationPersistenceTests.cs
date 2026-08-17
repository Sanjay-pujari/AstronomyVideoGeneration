using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2PublishingPublicationPersistenceTests
{
    private const string InstagramCheckpointMigrationId =
        "20260814010000_AddRc2InstagramPublicationCheckpoints";

    [Fact]
    public void Model_maps_publication_identity_indexes_and_enum_storage()
    {
        using var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        var entity = db.Model.FindEntityType(typeof(Rc2PublishingPublication));

        Assert.NotNull(entity);
        Assert.Equal("rc2_publishing_publications", entity.GetTableName());
        Assert.Equal(nameof(Rc2PublishingPublication.Id), Assert.Single(entity.FindPrimaryKey()!.Properties).Name);

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Rc2PublishingPublication.IdempotencyKey)]));
        Assert.Contains(entity.GetIndexes(), index =>
            !index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Rc2PublishingPublication.PlanId)]));
        Assert.Contains(entity.GetIndexes(), index =>
            !index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(Rc2PublishingPublication.PlanId),
                nameof(Rc2PublishingPublication.Target)
            ]));

        Assert.Equal(typeof(int), entity.FindProperty(nameof(Rc2PublishingPublication.Status))!
            .GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(typeof(int), entity.FindProperty(nameof(Rc2PublishingPublication.Target))!
            .GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
        Assert.Null(entity.FindProperty(nameof(Rc2PublishingPublication.FailureMessage))!.GetMaxLength());
        Assert.Equal("text", entity.FindProperty(nameof(Rc2PublishingPublication.FailureMessage))!.GetColumnType());
        Assert.Equal(128, entity.FindProperty(nameof(Rc2PublishingPublication.FailureCode))!.GetMaxLength());
    }

    [Fact]
    public void Npgsql_model_maps_every_publication_property_to_the_expected_physical_type()
    {
        using var db = CreateNpgsqlContext();
        var entity = db.Model.FindEntityType(typeof(Rc2PublishingPublication))!;
        var table = StoreObjectIdentifier.Table("rc2_publishing_publications", null);
        var expected = new Dictionary<string, (string Type, bool Nullable)>
        {
            [nameof(Rc2PublishingPublication.Id)] = ("uuid", false),
            [nameof(Rc2PublishingPublication.PlanId)] = ("uuid", false),
            [nameof(Rc2PublishingPublication.PublishingPackageId)] = ("character varying(128)", false),
            [nameof(Rc2PublishingPublication.Phase20AuthorityChecksum)] = ("character varying(128)", false),
            [nameof(Rc2PublishingPublication.Target)] = ("integer", false),
            [nameof(Rc2PublishingPublication.RoleOrMediaType)] = ("character varying(256)", false),
            [nameof(Rc2PublishingPublication.IdempotencyKey)] = ("character varying(64)", false),
            [nameof(Rc2PublishingPublication.Status)] = ("integer", false),
            [nameof(Rc2PublishingPublication.AttemptCount)] = ("integer", false),
            [nameof(Rc2PublishingPublication.LastAttemptUtc)] = ("timestamp with time zone", true),
            [nameof(Rc2PublishingPublication.RemotePublicationId)] = ("character varying(256)", true),
            [nameof(Rc2PublishingPublication.RemotePostId)] = ("character varying(256)", true),
            [nameof(Rc2PublishingPublication.RemoteContainerId)] = ("character varying(256)", true),
            [nameof(Rc2PublishingPublication.RemoteUrl)] = ("character varying(2048)", true),
            [nameof(Rc2PublishingPublication.PublicMediaBlobName)] = ("character varying(1024)", true),
            [nameof(Rc2PublishingPublication.PublicMediaExpiresUtc)] = ("timestamp with time zone", true),
            [nameof(Rc2PublishingPublication.MediaPrepared)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.PublicMediaStaged)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.ContainerReady)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.PublishRequested)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.VideoCreatedUtc)] = ("timestamp with time zone", true),
            [nameof(Rc2PublishingPublication.VideoUploadCompleted)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.ThumbnailCompleted)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.CaptionCompleted)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.RemoteVerificationCompleted)] = ("boolean", false),
            [nameof(Rc2PublishingPublication.LastCompletedStep)] = ("integer", false),
            [nameof(Rc2PublishingPublication.FailureCode)] = ("character varying(128)", true),
            [nameof(Rc2PublishingPublication.FailureMessage)] = ("text", true),
            [nameof(Rc2PublishingPublication.CreatedUtc)] = ("timestamp with time zone", false),
            [nameof(Rc2PublishingPublication.UpdatedUtc)] = ("timestamp with time zone", false)
        };

        Assert.Equal(expected.Keys.Order(), entity.GetProperties().Select(property => property.Name).Order());
        foreach (var (propertyName, physical) in expected)
        {
            var property = entity.FindProperty(propertyName)!;
            Assert.Equal(propertyName, property.GetColumnName(table));
            Assert.Equal(physical.Type, property.GetColumnType(table));
            Assert.Equal(physical.Nullable, property.IsNullable);
        }
    }

    [Fact]
    public void Instagram_checkpoint_migration_adds_all_fields_with_safe_defaults_and_reverses_them()
    {
        using var db = CreateNpgsqlContext();
        var migrations = db.GetService<IMigrationsAssembly>();
        var migrationType = migrations.Migrations[InstagramCheckpointMigrationId];
        var migration = migrations.CreateMigration(migrationType, db.Database.ProviderName!);
        var additions = migration.UpOperations.OfType<AddColumnOperation>()
            .ToDictionary(operation => operation.Name);

        Assert.Equal(7, additions.Count);
        AssertCheckpoint(additions, nameof(Rc2PublishingPublication.MediaPrepared), "boolean", false, false);
        AssertCheckpoint(additions, nameof(Rc2PublishingPublication.PublicMediaStaged), "boolean", false, false);
        AssertCheckpoint(additions, nameof(Rc2PublishingPublication.PublicMediaBlobName), "character varying(1024)", true, null);
        AssertCheckpoint(additions, nameof(Rc2PublishingPublication.PublicMediaExpiresUtc), "timestamp with time zone", true, null);
        AssertCheckpoint(additions, nameof(Rc2PublishingPublication.RemoteContainerId), "character varying(256)", true, null);
        AssertCheckpoint(additions, nameof(Rc2PublishingPublication.ContainerReady), "boolean", false, false);
        AssertCheckpoint(additions, nameof(Rc2PublishingPublication.PublishRequested), "boolean", false, false);

        var removals = migration.DownOperations.OfType<DropColumnOperation>().ToArray();
        Assert.Equal(additions.Keys.Order(), removals.Select(operation => operation.Name).Order());
        Assert.All(removals, operation =>
            Assert.Equal("rc2_publishing_publications", operation.Table));
    }

    private static MediaFactoryDbContext CreateNpgsqlContext() =>
        new(new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseNpgsql("Host=localhost;Database=rc2_model_certification;Username=unused;Password=unused")
            .Options);

    private static void AssertCheckpoint(
        IReadOnlyDictionary<string, AddColumnOperation> additions,
        string name,
        string columnType,
        bool nullable,
        object? defaultValue)
    {
        var operation = additions[name];
        Assert.Equal("rc2_publishing_publications", operation.Table);
        Assert.Equal(columnType, operation.ColumnType);
        Assert.Equal(nullable, operation.IsNullable);
        Assert.Equal(defaultValue, operation.DefaultValue);
    }
}
