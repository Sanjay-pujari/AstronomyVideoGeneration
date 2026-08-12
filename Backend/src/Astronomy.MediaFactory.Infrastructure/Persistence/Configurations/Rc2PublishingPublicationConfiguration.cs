using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class Rc2PublishingPublicationConfiguration : IEntityTypeConfiguration<Rc2PublishingPublication>
{
    public void Configure(EntityTypeBuilder<Rc2PublishingPublication> entity)
    {
        entity.ToTable("rc2_publishing_publications");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.PublishingPackageId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Phase20AuthorityChecksum).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RoleOrMediaType).HasMaxLength(256).IsRequired();
        entity.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        entity.Property(x => x.RemotePublicationId).HasMaxLength(256);
        entity.Property(x => x.RemoteUrl).HasMaxLength(2048);
        entity.Property(x => x.FailureCode).HasMaxLength(128);
        entity.Property(x => x.FailureMessage).HasMaxLength(1024);
        entity.HasIndex(x => x.IdempotencyKey).IsUnique();
        entity.HasIndex(x => x.PlanId);
        entity.HasIndex(x => new { x.PlanId, x.Target });
        entity.HasOne<ContentGenerationPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}
