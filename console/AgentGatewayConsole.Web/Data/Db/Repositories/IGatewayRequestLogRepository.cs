using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Models;

namespace AgentGatewayConsole.Web.Data.Db.Repositories;

public interface IGatewayRequestLogRepository : IRepository<GatewayRequestLog>
{
    Task<DashboardSummary> GetSummaryAsync(DateTime fromUtc, CancellationToken ct = default);
    Task<List<TimelinePoint>> GetTimelinePointsAsync(DateTime fromUtc, CancellationToken ct = default);
    Task<List<UsageItem>> GetModelUsageAsync(DateTime fromUtc, int take, CancellationToken ct = default);
    Task<List<UsageItem>> GetToolUsageAsync(DateTime fromUtc, int take, CancellationToken ct = default);
    Task<List<GatewayRequestLog>> GetProtectedEventsAsync(DateTime fromUtc, int take, CancellationToken ct = default);
    Task<PagedResult<GatewayRequestLog>> GetTrafficPageAsync(TrafficQuery query, CancellationToken ct = default);
    Task<List<string>> GetDistinctModelsAsync(CancellationToken ct = default);

    /// <summary>Kesim zamanından eski kayıtları partiler hâlinde siler; silinen kayıt sayısını döner.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);
}
