using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyAssetProductionJobConfiguration : IEntityTypeConfiguration<AstronomyAssetProductionJob>
{
    public void Configure(EntityTypeBuilder<AstronomyAssetProductionJob> builder)
    {
        builder.ToTable("astronomy_asset_production_jobs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SceneName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.AssetType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.AssetPurpose).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.PlannedProvider).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ObjectNamesJson).HasColumnType("jsonb");
        builder.Property(x => x.PromptOrInstruction).HasColumnType("text");
        builder.Property(x => x.ExpectedOutputType).HasMaxLength(120);
        builder.Property(x => x.AssetPriority).HasMaxLength(40).IsRequired();
        builder.Property(x => x.AssetExecutionGroup).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.OutputPath).HasColumnType("text");
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.FailureReason).HasColumnType("text");
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired(false);
        builder.Property(x => x.StartedUtc).IsRequired(false);
        builder.Property(x => x.CompletedUtc).IsRequired(false);

        builder.HasOne(x => x.ContentGenerationPlan)
            .WithMany()
            .HasForeignKey(x => x.ContentGenerationPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AstronomyContentOpportunity)
            .WithMany()
            .HasForeignKey(x => x.AstronomyContentOpportunityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.AstronomyEventIntelligence)
            .WithMany()
            .HasForeignKey(x => x.AstronomyEventIntelligenceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ContentGenerationPlanId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.AssetType);
        builder.HasIndex(x => x.AssetPriority);
        builder.HasIndex(x => x.PlannedProvider);
    }
}
