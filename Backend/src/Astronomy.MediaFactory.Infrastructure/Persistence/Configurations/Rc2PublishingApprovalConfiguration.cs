using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class Rc2PublishingApprovalConfiguration : IEntityTypeConfiguration<Rc2PublishingApproval>
{
    public void Configure(EntityTypeBuilder<Rc2PublishingApproval> entity)
    {
        entity.ToTable("rc2_publishing_approvals");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.PublishingPackageId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Phase20AuthorityChecksum).HasMaxLength(128).IsRequired();
        entity.Property(x => x.DecisionSource).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Decision).IsRequired();
        entity.Property(x => x.DecisionUtc).IsRequired();
        entity.Property(x => x.CreatedUtc).IsRequired();
        entity.Property(x => x.UpdatedUtc).IsRequired();

        entity.HasIndex(x => x.PlanId);
        entity.HasIndex(x => new { x.PlanId, x.Phase20AuthorityChecksum, x.PublishingPackageId }).IsUnique();
        entity.HasOne<ContentGenerationPlan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
