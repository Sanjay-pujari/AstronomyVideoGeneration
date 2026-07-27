using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostArchitectureTests
{
 [Fact]
 public void Production_host_respects_architecture_boundaries()
 {
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

}
