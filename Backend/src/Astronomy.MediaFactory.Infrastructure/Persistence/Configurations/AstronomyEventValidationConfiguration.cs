using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyEventValidationConfiguration : IEntityTypeConfiguration<AstronomyEventValidation>
{
    public void Configure(EntityTypeBuilder<AstronomyEventValidation> builder)
    {
        builder.ToTable("astronomy_event_validations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ValidationType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ValidatorName).HasMaxLength(160);
        builder.Property(x => x.Message).HasMaxLength(2000);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired(false);

        builder.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
        builder.Property(x => x.EvidenceJson).HasColumnType("jsonb");
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasOne(x => x.AstronomyEventIntelligence)
            .WithMany(x => x.Validations)
            .HasForeignKey(x => x.AstronomyEventIntelligenceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
