using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Alerting;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SkyAlertSubscriptionTests
{
    [Fact]
    public async Task Subscribe_CreatesSubscriber_And_DuplicateIsBlocked()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var request = Request();

        var created = await service.SubscribeAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, created.SubscriberId);
        Assert.Single(db.AlertSubscribers);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubscribeAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PreferencesUpdate_Works()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var created = await service.SubscribeAsync(Request(), CancellationToken.None);

        var updated = await service.UpdatePreferencesAsync(created.SubscriberId, new AlertPreferenceUpdateRequest(["FullMoon"], "19:15", 0.7, true, true), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("19:15", updated!.Preferences.PreferredAlertTimeLocal);
        Assert.Equal(0.7, updated.Preferences.MinimumEventScore);
        Assert.Contains("FullMoon", updated.Preferences.EventTypes);
    }

    [Fact]
    public async Task UpcomingAlerts_ReturnsCandidates()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var upcoming = await service.GetUpcomingAsync("india-udaipur", CancellationToken.None);

        Assert.NotEmpty(upcoming);
        Assert.All(upcoming, item => Assert.Equal("india-udaipur", item.RegionId));
    }

    [Fact]
    public async Task Unsubscribe_DisablesSubscriber()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var created = await service.SubscribeAsync(Request(), CancellationToken.None);

        var unsubscribed = await service.UnsubscribeAsync(created.SubscriberId, CancellationToken.None);

        Assert.True(unsubscribed);
        Assert.False(db.AlertSubscribers.Single().IsActive);
    }

    [Fact]
    public async Task Generation_CreatesPending_And_PreventsDuplicates()
    {
        var services = CreateServiceProvider();
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
            await CreateService(db).SubscribeAsync(Request(), CancellationToken.None);
        }
        var generator = CreateGenerator(services);

        var first = await generator.GenerateAsync(CancellationToken.None);
        var second = await generator.GenerateAsync(CancellationToken.None);

        Assert.Equal(1, first.NotificationsCreated);
        Assert.Equal(0, second.NotificationsCreated);
        Assert.True(second.DuplicatesSkipped >= 1);
    }

    [Fact]
    public async Task EmailDisabled_KeepsPending()
    {
        var services = CreateServiceProvider();
        Guid subscriberId;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
            var created = await CreateService(db).SubscribeAsync(Request(), CancellationToken.None);
            subscriberId = created.SubscriberId;
            db.AlertNotifications.Add(new AlertNotification { SubscriberId = subscriberId, EventId = "event-1", RegionId = "india-udaipur", Title = "Test", Message = "Test", Status = AlertNotificationStatus.Pending, Channel = AlertNotificationChannel.Email, ScheduledUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        var generator = CreateGenerator(services);

        var result = await generator.SendPendingAsync(CancellationToken.None);

        Assert.Equal(1, result.KeptPending);
        using var verify = services.CreateScope();
        Assert.Equal(AlertNotificationStatus.Pending, verify.ServiceProvider.GetRequiredService<MediaFactoryDbContext>().AlertNotifications.Single().Status);
    }

    private static AlertSubscribeRequest Request() => new("viewer@example.com", "india-udaipur", "en", ["MeteorShower"], "18:00", 0.65);

    private static SkyAlertGenerationService CreateGenerator(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new AlertsOptions()),
        Options.Create(new MaintenanceOptions { WorkingDirectory = Path.GetTempPath() }),
        NullLogger<SkyAlertGenerationService>.Instance);

    private static MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new MediaFactoryDbContext(options);
    }

    private static SkyAlertService CreateService(MediaFactoryDbContext db) => new(db, new FakeEvents(), SchedulerOptions(), LocalizationOptions(), Options.Create(new AlertsOptions()), TimeProvider.System);

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MediaFactoryDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<IAstronomyEventDiscoveryService>(new FakeEvents());
        services.AddSingleton<IEmailSender>(new SmtpEmailSender(Options.Create(new EmailOptions { Enabled = false })));
        return services.BuildServiceProvider();
    }

    private static IOptions<SchedulerOptions> SchedulerOptions() => Options.Create(new SchedulerOptions { Regions = new RegionSchedulingOptions { Items = [new RegionScheduleOptions { RegionId = "india-udaipur", DisplayName = "Udaipur", Latitude = 24, Longitude = 73, Language = "en" }] } });
    private static IOptions<LocalizationOptions> LocalizationOptions() => Options.Create(new LocalizationOptions { SupportedLanguages = ["en", "hi"], DefaultLanguage = "en", FallbackLanguage = "en" });

    private sealed class FakeEvents : IAstronomyEventDiscoveryService
    {
        private static readonly AstronomyEvent[] Events = [new() { EventId = "event-1", EventType = "meteor_shower", Title = "Meteor shower", Description = "Peak tonight", StartUtc = DateTimeOffset.UtcNow.AddHours(6), EndUtc = DateTimeOffset.UtcNow.AddHours(12), PeakUtc = DateTimeOffset.UtcNow.AddHours(8), GlobalVisibility = true, VisibilityRegions = ["Global"], ContentOpportunityScore = 0.9 }];
        public Task<IReadOnlyCollection<AstronomyEvent>> RefreshAsync(int? days, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AstronomyEvent>>(Events);
        public Task<IReadOnlyCollection<AstronomyEvent>> GetUpcomingAsync(int? days, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AstronomyEvent>>(Events);
        public Task<IReadOnlyCollection<AstronomyEvent>> GetTopAsync(int? days, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AstronomyEvent>>(Events);
        public Task<AstronomyEvent?> GetByIdAsync(string eventId, CancellationToken cancellationToken) => Task.FromResult<AstronomyEvent?>(Events.FirstOrDefault(x => x.EventId == eventId));
    }
}
