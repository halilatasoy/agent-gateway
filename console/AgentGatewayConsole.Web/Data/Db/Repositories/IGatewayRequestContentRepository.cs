using AgentGatewayConsole.Web.Data.Db.Entities;

namespace AgentGatewayConsole.Web.Data.Db.Repositories;

public interface IGatewayRequestContentRepository : IRepository<GatewayRequestContent>
{
    /// <summary>Log kaydına ait maskelenmiş içerik; yakalanmadıysa null.</summary>
    Task<GatewayRequestContent?> GetByLogIdAsync(long logId, CancellationToken ct = default);
}
