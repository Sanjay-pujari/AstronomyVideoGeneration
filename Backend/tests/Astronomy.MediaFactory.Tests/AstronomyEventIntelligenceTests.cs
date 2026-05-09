using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyEventIntelligenceTests
{
    [Fact]
    public async Task Discovery_FindsMoonPhaseEvents()
    {
        using var fixture = EventServiceFixture.Create(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        var events = await fixture.Discovery.RefreshAsync(30, CancellationToken.None);

        Assert.Contains(events, e => e.EventType is "moon_phase" or "full_moon");
        Assert.True(File.Exists(Path.Combine(fixture.WorkingDirectory, "astronomy-events-cache.json")));
        Assert.True(File.Exists(Path.Combine(fixture.WorkingDirectory, "astronomy-event-scoring-report.json")));
    }

    [Fact]
    public async Task Discovery_FindsMeteorShowerEvents()
    {
        using var fixture = EventServiceFixture.Create(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        var events = await fixture.Discovery.RefreshAsync(30, CancellationToken.None);

        Assert.Contains(events, e => e.EventType == "meteor_shower" && e.Title.Contains("Perseids", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scoring_ComputesWeightedContentOpportunityScore()
    {
        var service = new AstronomyEventScoringService();
        var evt = new AstronomyEvent
        {
            EventId = "test-high",
            EventType = "meteor_shower",
            Title = "Test shower",
            StartUtc = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            RarityScore = 0.8,
            VisibilityScore = 0.7,
            AudienceInterestScore = 0.9
        };

        var scored = service.Score(evt, new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(0.8 * 0.35 + 0.7 * 0.30 + 0.9 * 0.25 + 1.0 * 0.10, scored.ContentOpportunityScore, 3);
        Assert.Equal(1.0, scored.TimingUrgencyScore);
    }

    [Fact]
    public async Task Top_FiltersLowScoreEvents()
    {
        using var fixture = EventServiceFixture.Create(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), minimumScore: 0.8);

        var top = await fixture.Discovery.GetTopAsync(30, CancellationToken.None);

        Assert.NotEmpty(top);
        Assert.All(top, e => Assert.True(e.ContentOpportunityScore >= 0.8));
    }

    [Fact]
    public async Task Api_ReturnsUpcomingEvents()
    {
        using var fixture = EventServiceFixture.Create(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(fixture.Discovery);
        var app = builder.Build();
        app.MapGet("/api/events/upcoming", async (int? days, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
            Results.Ok(await events.GetUpcomingAsync(days, ct)));

        await app.StartAsync();
        var response = await app.GetTestClient().GetAsync("/api/events/upcoming?days=30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AstronomyEvent[]>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!);

        await app.StopAsync();
    }

    private sealed class EventServiceFixture : IDisposable
    {
        private EventServiceFixture(string workingDirectory, IAstronomyEventDiscoveryService discovery)
        {
            WorkingDirectory = workingDirectory;
            Discovery = discovery;
        }

        public string WorkingDirectory { get; }
        public IAstronomyEventDiscoveryService Discovery { get; }

        public static EventServiceFixture Create(DateTimeOffset now, double minimumScore = 0.65)
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), "astronomy-events-tests", Guid.NewGuid().ToString("N"));
            var options = Options.Create(new AstronomyEventsOptions
            {
                LookAheadDays = 30,
                MinimumContentOpportunityScore = minimumScore,
                Sources = new AstronomyEventSourceOptions
                {
                    MoonPhases = true,
                    MeteorShowers = true,
                    PlanetaryConjunctions = true,
                    Eclipses = true,
                    Comets = false,
                    IssPasses = false
                }
            });
            var maintenance = Options.Create(new MaintenanceOptions { WorkingDirectory = workingDirectory });
            var scoring = new AstronomyEventScoringService();
            var discovery = new AstronomyEventDiscoveryService(options, maintenance, scoring, new FixedTimeProvider(now), NullLogger<AstronomyEventDiscoveryService>.Instance);
            return new EventServiceFixture(workingDirectory, discovery);
        }

        public void Dispose()
        {
            if (Directory.Exists(WorkingDirectory))
                Directory.Delete(WorkingDirectory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
