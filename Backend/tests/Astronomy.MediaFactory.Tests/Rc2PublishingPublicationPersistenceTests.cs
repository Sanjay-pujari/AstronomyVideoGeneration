using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2PublishingPublicationPersistenceTests
{
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
    }
}
