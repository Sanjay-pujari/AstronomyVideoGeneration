using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyQuestionAnswerConfiguration : IEntityTypeConfiguration<AstronomyQuestionAnswer>
{
    public void Configure(EntityTypeBuilder<AstronomyQuestionAnswer> builder)
    {
        builder.ToTable("astronomy_question_answers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuestionType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.QuestionText).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(240).IsRequired();
        builder.Property(x => x.AnswerText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Ignore(x => x.UpdatedUtc);

        builder.HasOne(x => x.AstronomyQuestionAnswerSet)
            .WithMany(x => x.Answers)
            .HasForeignKey(x => x.QuestionAnswerSetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.QuestionAnswerSetId);
        builder.HasIndex(x => x.QuestionType);
        builder.HasIndex(x => x.DisplayOrder);
    }
}
