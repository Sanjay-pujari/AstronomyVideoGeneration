using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyContentOpportunityConfiguration : IEntityTypeConfiguration<AstronomyContentOpportunity>
{
    public void Configure(EntityTypeBuilder<AstronomyContentOpportunity> builder)
    {
        builder.ToTable("astronomy_content_opportunities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ContentCategory).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(240).IsRequired();
        builder.Property(x => x.Angle).HasMaxLength(1000);
        builder.Property(x => x.AudienceSegment).HasMaxLength(120);
        builder.Property(x => x.Status).HasMaxLength(60).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired(false);

        builder.Property(x => x.PriorityScore).HasPrecision(5, 2);
        builder.Property(x => x.SelectedEventObjectIdsJson)
            .HasColumnName("selected_event_object_ids_json")
            .HasColumnType("jsonb");
        builder.Property(x => x.SelectedObjectNamesJson)
            .HasColumnName("selected_object_names_json")
            .HasColumnType("jsonb");
        builder.Property(x => x.VisualStrategyJson).HasColumnType("jsonb");
        builder.Property(x => x.NarrationStrategyJson).HasColumnType("jsonb");
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasOne(x => x.AstronomyEventIntelligence)
            .WithMany(x => x.ContentOpportunities)
            .HasForeignKey(x => x.AstronomyEventIntelligenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ContentCategory);
        builder.HasIndex(x => x.PriorityScore);
        builder.HasIndex(x => x.Status);
    }
}
