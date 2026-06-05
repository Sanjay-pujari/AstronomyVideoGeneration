using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyEventObjectConfiguration : IEntityTypeConfiguration<AstronomyEventObject>
{
    public void Configure(EntityTypeBuilder<AstronomyEventObject> builder)
    {
        builder.ToTable("astronomy_event_objects");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ObjectName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ObjectType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ObjectRole).HasMaxLength(80);
        builder.Property(x => x.CatalogId).HasMaxLength(80);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired(false);

        builder.Property(x => x.Magnitude).HasPrecision(5, 2);
        builder.Property(x => x.VisibilityScore).HasPrecision(5, 2);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasOne(x => x.AstronomyEventIntelligence)
            .WithMany(x => x.Objects)
            .HasForeignKey(x => x.AstronomyEventIntelligenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ObjectName);
        builder.HasIndex(x => x.ObjectType);
    }
}
