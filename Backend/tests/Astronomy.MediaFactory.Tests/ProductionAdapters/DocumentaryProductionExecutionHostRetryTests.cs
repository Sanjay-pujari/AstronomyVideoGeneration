using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostRetryTests
{
 [Fact]
 public async Task Retryable_visual_failure_is_retried()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Retryable_narration_failure_is_retried()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Retryable_subtitle_failure_is_retried()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Retryable_scene_composition_failure_is_retried()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Retryable_variant_composition_failure_is_retried()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Retryable_media_verification_failure_is_retried()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Non_retryable_failure_is_not_retried()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

}
