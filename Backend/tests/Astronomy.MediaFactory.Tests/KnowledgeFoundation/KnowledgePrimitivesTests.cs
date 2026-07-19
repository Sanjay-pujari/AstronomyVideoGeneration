using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class KnowledgePrimitivesTests
{
    [Fact]
    public void Knowledge_id_normalizes_value_and_rejects_whitespace_tokens()
    {
        var id = new KnowledgeId("  knowledge.moon.identity  ");

        Assert.Equal("knowledge.moon.identity", id.Value);
        Assert.Equal("knowledge.moon.identity", id.ToString());
        Assert.Throws<ArgumentException>(() => new KnowledgeId("knowledge moon"));
    }

    [Fact]
    public void Knowledge_version_starts_at_one_and_formats_semantic_version()
    {
        var version = new KnowledgeVersion(1, 2, 3);

        Assert.Equal("1.2.3", version.ToString());
        Assert.Equal("1.0.0", KnowledgeVersion.Initial.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeVersion(0));
    }

    [Fact]
    public void Astronomy_references_reuse_domain_taxonomy_without_requiring_catalog_or_family_implementations()
    {
        var entity = new AstronomyEntityReference(" moon ", AstronomyEntityKind.Moon, "Moon");
        var family = new AstronomyFamilyReference("solar-system", AstronomyFamilyKind.CelestialObject);

        Assert.Equal("moon", entity.EntityId);
        Assert.Equal(AstronomyEntityKind.Moon, entity.EntityKind);
        Assert.Equal("solar-system", family.FamilyId);
        Assert.Equal(AstronomyFamilyKind.CelestialObject, family.FamilyKind);
    }

    [Fact]
    public void Validity_range_supports_current_open_ended_knowledge_without_lifecycle_transitions()
    {
        var starts = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var range = new KnowledgeValidityRange(starts);

        Assert.True(range.IsOpenEnded);
        Assert.False(range.Contains(starts.AddTicks(-1)));
        Assert.True(range.Contains(starts));
        Assert.True(range.Contains(starts.AddYears(1)));
    }
}
