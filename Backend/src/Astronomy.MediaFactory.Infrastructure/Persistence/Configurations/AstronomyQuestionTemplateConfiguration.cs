using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Configurations;

public sealed class AstronomyQuestionTemplateConfiguration : IEntityTypeConfiguration<AstronomyQuestionTemplate>
{
    public void Configure(EntityTypeBuilder<AstronomyQuestionTemplate> builder)
    {
        builder.ToTable("astronomy_question_templates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.QuestionType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TemplateName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.TemplateText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(16).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Ignore(x => x.UpdatedUtc);

        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => x.QuestionType);
        builder.HasIndex(x => x.Language);
        builder.HasIndex(x => x.IsActive);
    }
}
