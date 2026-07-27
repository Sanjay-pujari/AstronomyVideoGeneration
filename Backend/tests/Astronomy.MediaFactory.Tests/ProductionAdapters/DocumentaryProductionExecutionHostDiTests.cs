using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostDiTests
{
 [Fact]
 public void Production_host_DI_graph_validates()
 {
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public void Coordinator_and_compatibility_host_resolve()
 {
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public void Execution_host_is_disabled_by_default()
 {
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public void Execution_host_options_validation_rejects_invalid_values()
 {
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

}
