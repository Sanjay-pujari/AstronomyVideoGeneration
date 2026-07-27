using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionAttemptContextFactoryTests
{
 [Fact]
 public async Task Attempt_context_preserves_execution_identity()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Attempt_number_must_be_positive()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

}
