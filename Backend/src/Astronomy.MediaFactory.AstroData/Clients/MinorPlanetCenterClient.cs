namespace Astronomy.MediaFactory.AstroData.Clients;
public sealed class MinorPlanetCenterClient
{
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken) { await Task.Delay(10, cancellationToken); return "MPC client stub ready"; }
}
