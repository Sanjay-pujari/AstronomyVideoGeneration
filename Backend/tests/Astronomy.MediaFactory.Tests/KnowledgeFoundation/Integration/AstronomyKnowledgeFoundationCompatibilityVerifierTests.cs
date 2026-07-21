using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Integration;
public sealed class AstronomyKnowledgeFoundationCompatibilityVerifierTests{[Fact]public void Production_foundation_is_compatible_and_stable(){var f=new KnowledgeFoundationIntegrationFixture();var a=f.Verifier.Verify();var b=f.Verifier.Verify();Assert.True(a.IsCompatible);Assert.Empty(a.Issues);Assert.Equal(a.Issues,b.Issues);} }
