using AgentGatewayConsole.Web.Data.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentGatewayConsole.Web.Data.Db.Repositories;

public class GatewayRequestContentRepository : Repository<GatewayRequestContent>, IGatewayRequestContentRepository
{
    public GatewayRequestContentRepository(ConsoleDbContext dbContext) : base(dbContext) { }

    public Task<GatewayRequestContent?> GetByLogIdAsync(long logId, CancellationToken ct = default)
        => DbContext.GatewayRequestContents.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayRequestLogId == logId, ct);
}
