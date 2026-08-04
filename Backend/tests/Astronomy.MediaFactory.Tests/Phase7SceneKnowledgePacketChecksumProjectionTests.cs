using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7SceneKnowledgePacketChecksumProjectionTests
{
    [Fact]
    public void ChecksumProjection_DoesNotUseCertifiedClaimContract()
    {
        var type = typeof(Phase7SceneKnowledgePacketCanonicalizer).Assembly
            .GetType("Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint.Phase7ClaimChecksumProjection");

        Assert.NotNull(type);
        Assert.True(type!.IsSealed && !type.IsPublic);
        Assert.DoesNotContain(type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.PropertyType == typeof(CertifiedNarrationClaim));
        Assert.Equal(new[] { "ClaimId", "SemanticIdentity", "Domain", "Text", "SourceIds",
            "KnowledgeReferenceIds", "Confidence", "IsApproximate", "IsLocationDependent",
            "IsDateTimeDependent", "IsCultural", "IsMythological", "IsAstrologyRelated",
            "RequiresQualification", "RequiresHumanReview", "Language", "Disposition",
            "ProvenancePrecision", "SelectionReason", "WeatherDependent", "MoonDependent",
            "Uncertain", "Checksum" }, type.GetProperties().Select(x => x.Name));
    }

    [Fact]
    public void ChecksumProjection_DoesNotModifyAuthorityClaim()
    {
        var claim = Claim(sourceIds: ["source-z", "source-a"], references: ["ref-z", "ref-a"]);
        var packet = Packet(claim);

        _ = Phase7SceneKnowledgePacketCanonicalizer.ComputeChecksum(packet);

        Assert.Same(claim, Assert.Single(packet.RequiredClaims));
        Assert.Equal(new[] { "source-z", "source-a" }, claim.SourceIds);
        Assert.Equal(new[] { "ref-z", "ref-a" }, claim.KnowledgeReferenceIds);
    }

    [Fact] public void ChecksumProjection_PreservesOriginalClaimChecksumAsEvidence() =>
        Assert.NotEqual(Hash(Claim(checksum: "certified-a")), Hash(Claim(checksum: "certified-b")));

    [Fact] public void NestedSourceIdOrdering_DoesNotChangePacketChecksum() =>
        Assert.Equal(Hash(Claim(sourceIds: ["source-b", "source-a"])),
            Hash(Claim(sourceIds: ["source-a", "source-b"])));

    [Fact] public void NestedReferenceIdOrdering_DoesNotChangePacketChecksum() =>
        Assert.Equal(Hash(Claim(references: ["ref-b", "ref-a"])),
            Hash(Claim(references: ["ref-a", "ref-b"])));

    [Fact] public void ChangingClaimSemanticContent_ChangesPacketChecksum() =>
        Assert.NotEqual(Hash(Claim(text: "Orion is visible.")), Hash(Claim(text: "Orion is nearby.")));

    [Fact] public void ChangingCertifiedClaimChecksum_ChangesPacketChecksum() =>
        Assert.NotEqual(Hash(Claim(checksum: "certified-a")), Hash(Claim(checksum: "certified-b")));

    private static string Hash(CertifiedNarrationClaim claim) =>
        Phase7SceneKnowledgePacketCanonicalizer.ComputeChecksum(Packet(claim));

    private static CertifiedNarrationClaim Claim(string text = "Orion is visible.",
        IReadOnlyList<string>? sourceIds = null, IReadOnlyList<string>? references = null,
        string checksum = "certified-checksum") =>
        new("claim-orion", "Astronomy", text, sourceIds ?? ["source-a", "source-b"],
            references ?? ["ref-a", "ref-b"], .99m, false, true, true, false, false, false,
            true, false, "en", checksum)
        {
            SemanticIdentity = "orion.visibility", Disposition = Phase7ClaimDisposition.Required,
            ProvenancePrecision = "Exact", SelectionReason = "CertifiedKnowledge",
            WeatherDependent = true, MoonDependent = true, Uncertain = false
        };

    private static SceneKnowledgePacket Packet(CertifiedNarrationClaim claim) => new(
        "packet-long-existing", "execution", "plan", "event", "Constellation", "en", "profile", "1",
        "Long", "frame-1", "frame-checksum", "scene-1", "scene-checksum", 1, 1, "Hook", "Primary",
        "hook", "question-1", "Where is Orion?", "objective-1", "Locate Orion", [claim], [], [], [],
        [], [], [], new Dictionary<string, string>(), [], new Dictionary<string, string>(), [],
        ["ref-a", "ref-b"], ["source-a", "source-b"], 30, 20, 40, true, true, [], false, [], [],
        new Dictionary<string, string>(), "");
}
