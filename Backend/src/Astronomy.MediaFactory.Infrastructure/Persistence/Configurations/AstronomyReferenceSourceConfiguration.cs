using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyReferenceSourceConfiguration : IEntityTypeConfiguration<AstronomyReferenceSource>
{
    public void Configure(EntityTypeBuilder<AstronomyReferenceSource> builder)
    {
        builder.ToTable("astronomy_reference_sources");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.SourceType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.SourceUrl).HasMaxLength(1000);
        builder.Property(x => x.Citation).HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired(false);

        builder.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
        builder.Property(x => x.EvidenceJson).HasColumnType("jsonb");
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasOne(x => x.AstronomyEventIntelligence)
            .WithMany(x => x.ReferenceSources)
            .HasForeignKey(x => x.AstronomyEventIntelligenceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
