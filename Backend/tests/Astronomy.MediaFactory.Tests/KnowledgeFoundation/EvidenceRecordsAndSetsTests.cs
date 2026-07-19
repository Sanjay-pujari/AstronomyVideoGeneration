using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class EvidenceRecordsAndSetsTests
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Observed = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evidence_roles_are_stable_and_guarded()
    {
        Assert.Equal(["Primary", "Supporting", "Contradicting", "Contextual", "Qualifying"], Enum.GetNames<KnowledgeEvidenceRole>());
        Assert.Equal(Enum.GetValues<KnowledgeEvidenceRole>().Length, Enum.GetValues<KnowledgeEvidenceRole>().Distinct().Count());
        foreach (var value in Enum.GetValues<KnowledgeEvidenceRole>()) Assert.Equal(value, EvidenceFoundationEnumGuard.RequireDefined(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceFoundationEnumGuard.RequireDefined((KnowledgeEvidenceRole)999));
    }

    [Fact]
    public void Minimal_valid_evidence_record_preserves_contract_state()
    {
        var record = CreateRecord(title: " Synthetic lunar distance ", summary: " Summary ", externalIdentifiers: [new("doi", "10.1/A"), new("catalog", "B")], tags: [new("Moon"), new("Distance")]);

        Assert.Equal(new EvidenceId("evidence.synthetic.one"), record.Id);
        Assert.Equal(AstronomyEvidenceType.Observation, record.Type);
        Assert.Equal(EvidenceFoundationStatus.Draft, record.Status);
        Assert.Equal(CreateSource(), record.Source);
        Assert.Equal(new EvidenceAttribution(), record.Attribution);
        Assert.Equal(new EvidenceTemporalMetadata(observedAtUtc: Observed), record.TemporalMetadata);
        Assert.Equal(new KnowledgeAuditMetadata(Created, "author"), record.Audit);
        Assert.Equal("Synthetic lunar distance", record.Title);
        Assert.Equal("Summary", record.Summary);
        Assert.Equal(["catalog:B", "doi:10.1/A"], record.ExternalIdentifiers.Select(identifier => identifier.ToString()));
        Assert.Equal(["distance", "moon"], record.Tags.Select(tag => tag.Value));
    }

    [Fact]
    public void Evidence_record_rejects_invalid_required_fields()
    {
        Assert.Throws<ArgumentException>(() => CreateRecord(id: default));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRecord(type: (AstronomyEvidenceType)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateRecord(status: (EvidenceFoundationStatus)999));
        Assert.Throws<ArgumentNullException>(() => new AstronomyEvidenceRecord(new EvidenceId("evidence.synthetic.one"), AstronomyEvidenceType.Observation, EvidenceFoundationStatus.Draft, null!, new EvidenceTemporalMetadata(), new KnowledgeAuditMetadata(Created, "author")));
        Assert.Throws<ArgumentNullException>(() => new AstronomyEvidenceRecord(new EvidenceId("evidence.synthetic.one"), AstronomyEvidenceType.Observation, EvidenceFoundationStatus.Draft, CreateSource(), null!, new KnowledgeAuditMetadata(Created, "author")));
        Assert.Throws<ArgumentNullException>(() => new AstronomyEvidenceRecord(new EvidenceId("evidence.synthetic.one"), AstronomyEvidenceType.Observation, EvidenceFoundationStatus.Draft, CreateSource(), new EvidenceTemporalMetadata(), null!));
    }

    [Fact]
    public void Evidence_record_optional_metadata_is_trimmed_bounded_and_control_free()
    {
        Assert.Null(CreateRecord(title: "   ", summary: "").Title);
        Assert.Null(CreateRecord(title: "   ", summary: "").Summary);
        Assert.Equal("Case Preserved", CreateRecord(title: " Case Preserved ").Title);
        Assert.Throws<ArgumentException>(() => CreateRecord(title: new string('a', AstronomyEvidenceRecord.MaxTitleLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateRecord(summary: new string('a', AstronomyEvidenceRecord.MaxSummaryLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateRecord(title: "bad\u0001"));
        Assert.Throws<ArgumentException>(() => CreateRecord(summary: "bad\u0001"));
    }

    [Fact]
    public void Evidence_record_collections_are_copied_ordered_unique_and_read_only()
    {
        var ids = new List<EvidenceExternalIdentifier> { new("doi", "B"), new("doi", "A") };
        var tags = new List<KnowledgeTag> { new("Beta"), new("alpha") };
        var record = CreateRecord(externalIdentifiers: ids, tags: tags);
        ids.Add(new EvidenceExternalIdentifier("catalog", "C"));
        tags.Add(new KnowledgeTag("gamma"));

        Assert.Equal(["doi:A", "doi:B"], record.ExternalIdentifiers.Select(identifier => identifier.ToString()));
        Assert.Equal(["alpha", "beta"], record.Tags.Select(tag => tag.Value));
        Assert.Throws<NotSupportedException>(() => ((IList<EvidenceExternalIdentifier>)record.ExternalIdentifiers).Add(new EvidenceExternalIdentifier("x", "y")));
        Assert.Throws<NotSupportedException>(() => ((IList<KnowledgeTag>)record.Tags).Add(new KnowledgeTag("delta")));
        Assert.Throws<ArgumentException>(() => CreateRecord(externalIdentifiers: [new("doi", "A"), new("DOI", "A")]));
        Assert.Throws<ArgumentException>(() => CreateRecord(tags: [new("Alpha"), new("alpha")]));
        Assert.Throws<ArgumentException>(() => CreateRecord(externalIdentifiers: [new("doi", "A"), null!]));
        Assert.Throws<ArgumentException>(() => CreateRecord(tags: [new("alpha"), null!]));
    }

    [Fact]
    public void Evidence_record_identity_is_evidence_id_only_and_equality_is_deliberate()
    {
        var first = CreateRecord(title: "A");
        var sameIdDifferentMetadata = CreateRecord(title: "B", status: EvidenceFoundationStatus.Verified);
        var differentId = CreateRecord(id: new EvidenceId("evidence.synthetic.two"));

        Assert.True(first.HasSameEvidenceIdentityAs(sameIdDifferentMetadata));
        Assert.False(first.HasSameEvidenceIdentityAs(differentId));
        Assert.Equal(first, sameIdDifferentMetadata);
        Assert.NotEqual(first, differentId);
    }

    [Fact]
    public void Association_preserves_statement_version_evidence_role_and_note()
    {
        var association = CreateAssociation(note: " Primary basis ");
        Assert.Equal(new KnowledgeId("knowledge.synthetic.one"), association.KnowledgeId);
        Assert.Equal(new KnowledgeVersion(2), association.KnowledgeVersion);
        Assert.Equal(new EvidenceId("evidence.synthetic.one"), association.EvidenceId);
        Assert.Equal(KnowledgeEvidenceRole.Primary, association.Role);
        Assert.Equal("Primary basis", association.Note);
    }

    [Fact]
    public void Association_rejects_invalid_state_and_optional_note()
    {
        Assert.Throws<ArgumentException>(() => CreateAssociation(knowledgeId: default));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAssociation(knowledgeVersion: default));
        Assert.Throws<ArgumentException>(() => CreateAssociation(evidenceId: default));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAssociation(role: (KnowledgeEvidenceRole)999));
        Assert.Null(CreateAssociation(note: " ").Note);
        Assert.Throws<ArgumentException>(() => CreateAssociation(note: new string('a', KnowledgeStatementEvidenceReference.MaxNoteLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateAssociation(note: "bad\u0001"));
    }

    [Fact]
    public void Evidence_set_enforces_owner_order_duplicates_primary_rule_and_immutability()
    {
        var input = new List<KnowledgeStatementEvidenceReference>
        {
            CreateAssociation(evidenceId: new EvidenceId("evidence.z"), role: KnowledgeEvidenceRole.Contradicting),
            CreateAssociation(evidenceId: new EvidenceId("evidence.a"), role: KnowledgeEvidenceRole.Supporting),
            CreateAssociation(evidenceId: new EvidenceId("evidence.m"), role: KnowledgeEvidenceRole.Primary)
        };
        var snapshot = input.ToArray();
        var set = new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), input);
        input.Add(CreateAssociation(evidenceId: new EvidenceId("evidence.new"), role: KnowledgeEvidenceRole.Contextual));

        Assert.Equal(snapshot, input.Take(snapshot.Length));
        Assert.Equal(["evidence.a:Supporting", "evidence.m:Primary", "evidence.z:Contradicting"], set.Associations.Select(a => $"{a.EvidenceId.Value}:{a.Role}"));
        Assert.Contains(set.Associations, a => a.Role == KnowledgeEvidenceRole.Supporting);
        Assert.Contains(set.Associations, a => a.Role == KnowledgeEvidenceRole.Contradicting);
        Assert.Throws<NotSupportedException>(() => ((IList<KnowledgeStatementEvidenceReference>)set.Associations).Add(CreateAssociation(evidenceId: new EvidenceId("evidence.x"))));

        var noPrimary = new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), [CreateAssociation(role: KnowledgeEvidenceRole.Contextual)]);
        Assert.DoesNotContain(noPrimary.Associations, a => a.Role == KnowledgeEvidenceRole.Primary);
        _ = new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), [CreateAssociation(role: KnowledgeEvidenceRole.Primary)]);
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), [CreateAssociation(evidenceId: new EvidenceId("evidence.a"), role: KnowledgeEvidenceRole.Primary), CreateAssociation(evidenceId: new EvidenceId("evidence.b"), role: KnowledgeEvidenceRole.Primary)]));
    }

    [Fact]
    public void Evidence_set_rejects_owner_mismatch_null_entries_duplicate_evidence_and_invalid_owner()
    {
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeStatementEvidenceSet(default, new KnowledgeVersion(2), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), default, []));
        Assert.Throws<ArgumentNullException>(() => new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), null!));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), [null!]));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), [CreateAssociation(knowledgeId: new KnowledgeId("knowledge.other"))]));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), [CreateAssociation(knowledgeVersion: new KnowledgeVersion(3))]));
        Assert.Throws<ArgumentException>(() => new AstronomyKnowledgeStatementEvidenceSet(new KnowledgeId("knowledge.synthetic.one"), new KnowledgeVersion(2), [CreateAssociation(evidenceId: new EvidenceId("evidence.same"), role: KnowledgeEvidenceRole.Supporting), CreateAssociation(evidenceId: new EvidenceId("evidence.same"), role: KnowledgeEvidenceRole.Contradicting)]));
    }

    [Fact]
    public void Task_2_2b_scope_excludes_future_architecture_dependencies()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Evidence"));
        var text = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        foreach (var forbidden in new[] { "ConfidenceScore", "ConfidenceAssessment", "EvidenceValidator", "EvidenceValidationCodes", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "DateTimeOffset.UtcNow" })
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
    }

    private static AstronomyEvidenceRecord CreateRecord(EvidenceId? id = null, AstronomyEvidenceType type = AstronomyEvidenceType.Observation, EvidenceFoundationStatus status = EvidenceFoundationStatus.Draft, AstronomyEvidenceSourceReference? source = null, EvidenceTemporalMetadata? temporalMetadata = null, KnowledgeAuditMetadata? audit = null, string? title = null, string? summary = null, IEnumerable<EvidenceExternalIdentifier>? externalIdentifiers = null, IEnumerable<KnowledgeTag>? tags = null)
        => new(id ?? new EvidenceId("evidence.synthetic.one"), type, status, source ?? CreateSource(), temporalMetadata ?? new EvidenceTemporalMetadata(observedAtUtc: Observed), audit ?? new KnowledgeAuditMetadata(Created, "author"), title: title, summary: summary, externalIdentifiers: externalIdentifiers, tags: tags);

    private static AstronomyEvidenceSourceReference CreateSource() => new("source.synthetic", AstronomyEvidenceSourceType.Observatory, "Synthetic Observatory", new Uri("https://example.test/source"));

    private static KnowledgeStatementEvidenceReference CreateAssociation(KnowledgeId? knowledgeId = null, KnowledgeVersion? knowledgeVersion = null, EvidenceId? evidenceId = null, KnowledgeEvidenceRole role = KnowledgeEvidenceRole.Primary, string? note = null)
        => new(knowledgeId ?? new KnowledgeId("knowledge.synthetic.one"), knowledgeVersion ?? new KnowledgeVersion(2), evidenceId ?? new EvidenceId("evidence.synthetic.one"), role, note);
}
