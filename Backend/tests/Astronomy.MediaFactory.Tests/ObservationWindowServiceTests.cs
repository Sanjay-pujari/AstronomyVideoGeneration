using Astronomy.MediaFactory.AstroData.Clients;
using Astronomy.MediaFactory.AstroData.Services;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public class ObservationWindowServiceTests
{
    [Fact]
    public async Task BuildNightWindowAsync_ConvertsUdaipurLocalToUtc()
    {
        var fake = new StubSkyfieldClient(new SkyfieldNightPlanResponse
        {
            SunsetLocal = "2026-05-03T18:35:00+05:30",
            SunriseLocal = "2026-05-04T05:59:00+05:30"
        });
        var sut = new ObservationWindowService(fake, NullLogger<ObservationWindowService>.Instance);
        var window = await sut.BuildNightWindowAsync(new ObservationOptions { LocationName = "Udaipur, India", Timezone = "Asia/Kolkata", Latitude = 24.5854, Longitude = 73.7125 }, new DateOnly(2026, 5, 3), CancellationToken.None);
        Assert.Equal("2026-05-03T13:05:00.0000000+00:00", window.NightWindowStartUtc.ToString("O"));
    }
}

sealed class StubSkyfieldClient : ISkyfieldSidecarClient
{
    private readonly SkyfieldNightPlanResponse _response;
    public StubSkyfieldClient(SkyfieldNightPlanResponse response) => _response = response;
    public Task<SkyfieldDailySkyResponse?> GetDailySkyAsync(SkyfieldDailySkyRequest request, CancellationToken cancellationToken) => Task.FromResult<SkyfieldDailySkyResponse?>(null);
    public Task<SkyfieldNightPlanResponse?> GetNightVisibilityPlanAsync(SkyfieldNightPlanRequest request, CancellationToken cancellationToken) => Task.FromResult<SkyfieldNightPlanResponse?>(_response);
}
