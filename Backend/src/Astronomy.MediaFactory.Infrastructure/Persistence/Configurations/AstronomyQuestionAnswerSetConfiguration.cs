using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyQuestionAnswerSetConfiguration : IEntityTypeConfiguration<AstronomyQuestionAnswerSet>
{
    public void Configure(EntityTypeBuilder<AstronomyQuestionAnswerSet> builder)
    {
        builder.ToTable("astronomy_question_answer_sets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RegionId).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(60).IsRequired();
        builder.Property(x => x.GeneratedUtc).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired(false);

        builder.HasOne(x => x.AstronomyEventIntelligence)
            .WithMany(x => x.QuestionAnswerSets)
            .HasForeignKey(x => x.AstronomyEventIntelligenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.AstronomyEventIntelligenceId);
        builder.HasIndex(x => x.RegionId);
        builder.HasIndex(x => x.Language);
        builder.HasIndex(x => x.Status);
    }
}
