using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
namespace Astronomy.MediaFactory.Infrastructure.Persistence;
public sealed class EfPipelineRepository : IPipelineRepository
{
    private readonly MediaFactoryDbContext _db;
    public EfPipelineRepository(MediaFactoryDbContext db) { _db = db; }
    public async Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) { await _db.PipelineRuns.AddAsync(run, cancellationToken); await _db.SaveChangesAsync(cancellationToken); return run; }
    public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => _db.PipelineRuns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => await _db.PipelineRuns.OrderByDescending(x => x.CreatedUtc).Take(take).ToListAsync(cancellationToken);
    public async Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => await _db.GeneratedScripts.AddAsync(script, cancellationToken);
    public async Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => await _db.MediaAssets.AddAsync(asset, cancellationToken);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);
}
