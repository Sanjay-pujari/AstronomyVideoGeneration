using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2PublishingControllerActivationTests
{
    [Fact]
    public async Task Single_public_constructor_resolves_and_all_routes_reach_their_services_without_provider_calls()
    {
        var constructors = typeof(Rc2PublishingController).GetConstructors();
        var constructor = Assert.Single(constructors);
        Assert.Equal(
            [typeof(IRc2PublishingControlService), typeof(IRc2PublishingExecutionService),
                typeof(ILogger<Rc2PublishingController>)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));

        var control = new RecordingControlService();
        var execution = new RecordingExecutionService();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRc2PublishingControlService>(_ => control);
        services.AddScoped<IRc2PublishingExecutionService>(_ => execution);
        services.AddTransient<Rc2PublishingController>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        var controller = scope.ServiceProvider.GetRequiredService<Rc2PublishingController>();
        var planId = Guid.NewGuid();

        Assert.IsType<OkObjectResult>(await controller.Package(new(planId, false), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Approve(
            new(planId, Rc2PublishingApprovalStatus.Approved), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Status(planId, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Video(
            new(planId, [Rc2PublishingTarget.YouTubeLong], Rc2PublishMode.Now, true), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Media(
            new(planId, [Rc2PublishingMediaType.Hero], [Rc2PublishingTarget.InstagramPost], true),
            CancellationToken.None));

        Assert.Equal(1, control.PackageCalls);
        Assert.Equal(1, control.ApproveCalls);
        Assert.Equal(1, control.StatusCalls);
        Assert.Equal(1, execution.VideoCalls);
        Assert.Equal(1, execution.MediaCalls);
        Assert.True(execution.AllRequestsWereDryRuns);
        Assert.Equal(0, execution.ExternalProviderCalls);
    }

    private sealed class RecordingControlService : IRc2PublishingControlService
    {
        public int PackageCalls { get; private set; }
        public int ApproveCalls { get; private set; }
        public int StatusCalls { get; private set; }

        public Task<Rc2PublishingPackageResponse> CreateOrRefreshPackageAsync(Guid planId, bool overwriteExisting,
            CancellationToken cancellationToken)
        {
            PackageCalls++;
            return Task.FromResult(new Rc2PublishingPackageResponse(planId, "en", "Succeeded", "package", "checksum",
                true, true, true, false, false, Rc2PublishingApprovalStatus.Pending, 1, []));
        }

        public Task<Rc2PublishingApprovalResponse> SetApprovalAsync(Guid planId, Rc2PublishingApprovalStatus decision,
            CancellationToken cancellationToken)
        {
            ApproveCalls++;
            return Task.FromResult(new Rc2PublishingApprovalResponse(planId, "package", "checksum", decision,
                true, true, true, true, true, DateTimeOffset.UtcNow));
        }

        public Task<Rc2PublishingStatusResponse> GetStatusAsync(Guid planId, CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(new Rc2PublishingStatusResponse(planId, "title", "en", "global",
                new(true, true, "checksum", true),
                new(true, "Succeeded", "package", "checksum", true, true, false, false,
                    Rc2PublishingApprovalStatus.Pending, 1), new Dictionary<string, int>(), [],
                new Dictionary<Rc2PublishingTarget, Rc2TargetStatus>()));
        }
    }

    private sealed class RecordingExecutionService : IRc2PublishingExecutionService
    {
        public int VideoCalls { get; private set; }
        public int MediaCalls { get; private set; }
        public int ExternalProviderCalls => 0;
        public bool AllRequestsWereDryRuns { get; private set; } = true;

        public Task<Rc2PublishingExecutionResponse> PublishVideoAsync(Rc2PublishVideoRequest request,
            CancellationToken ct)
        {
            VideoCalls++;
            AllRequestsWereDryRuns &= request.DryRun;
            return Task.FromResult(Response(request.PlanId, "Video"));
        }

        public Task<Rc2PublishingExecutionResponse> PublishMediaAsync(Rc2PublishMediaRequest request,
            CancellationToken ct)
        {
            MediaCalls++;
            AllRequestsWereDryRuns &= request.DryRun;
            return Task.FromResult(Response(request.PlanId, "Media"));
        }

        private static Rc2PublishingExecutionResponse Response(Guid planId, string requestType) =>
            new(planId, "package", "checksum", requestType, "Succeeded", []);
    }
}

internal sealed class NonPublishingExecutionService : IRc2PublishingExecutionService
{
    public Task<Rc2PublishingExecutionResponse> PublishVideoAsync(Rc2PublishVideoRequest request,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<Rc2PublishingExecutionResponse> PublishMediaAsync(Rc2PublishMediaRequest request,
        CancellationToken ct) => throw new NotSupportedException();
}
