using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyEventIntelligenceConfiguration : IEntityTypeConfiguration<AstronomyEventIntelligence>
{
    public void Configure(EntityTypeBuilder<AstronomyEventIntelligence> builder)
    {
        builder.ToTable("astronomy_event_intelligences");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventCode).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ExternalEventId)
            .HasMaxLength(160)
            .IsRequired();
        builder.Property(x => x.Language)
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(x => x.VerificationStatus)
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(x => x.ContentStrategy)
            .HasMaxLength(120);
        builder.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(240).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(1000);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.RegionId).HasMaxLength(80);
        builder.Property(x => x.LocationName).HasMaxLength(160);
        builder.Property(x => x.TimeZone).HasMaxLength(80);
        builder.Property(x => x.RecommendedCategory).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(60).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired(false);

        builder.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
        builder.Property(x => x.RarityScore).HasPrecision(5, 2);
        builder.Property(x => x.VisibilityScore).HasPrecision(5, 2);
        builder.Property(x => x.AudienceInterestScore).HasPrecision(5, 2);
        builder.Property(x => x.TimingUrgencyScore).HasPrecision(5, 2);
        builder.Property(x => x.ContentOpportunityScore).HasPrecision(5, 2);

        builder.Property(x => x.RawDataJson).HasColumnType("jsonb");
        builder.Property(x => x.RulesAppliedJson).HasColumnType("jsonb");
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.EventCode).IsUnique();
        builder.HasIndex(x => new
        {
            x.ExternalEventId,
            x.Year,
            x.RegionId,
            x.Language
        }).IsUnique();
        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => x.StartUtc);
        builder.HasIndex(x => x.PeakUtc);
        builder.HasIndex(x => x.RegionId);
        builder.HasIndex(x => x.RecommendedCategory);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.VerificationStatus);
        builder.HasIndex(x => x.AutoGenerateAllowed);
        builder.HasIndex(x => x.ContentStrategy);
    }
}
