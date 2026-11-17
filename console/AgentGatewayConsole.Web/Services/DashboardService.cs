using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;

namespace AgentGatewayConsole.Web.Services;

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(DashboardRange range, CancellationToken ct = default);
    Task<IReadOnlyList<TimelineBucket>> GetTimelineAsync(DashboardRange range, CancellationToken ct = default);
    Task<IReadOnlyList<UsageItem>> GetModelUsageAsync(DashboardRange range, CancellationToken ct = default);
    Task<IReadOnlyList<UsageItem>> GetToolUsageAsync(DashboardRange range, CancellationToken ct = default);
    DateTime GetRangeStartUtc(DashboardRange range);
}

/// <summary>Koruma Özeti ekranının hesaplamaları.</summary>
public class DashboardService : IDashboardService
{
    private const int TopItems = 8;

    private readonly IGatewayRequestLogRepository _repository;
    private readonly TimeProvider _time;

    public DashboardService(IGatewayRequestLogRepository repository, TimeProvider time)
    {
        _repository = repository;
        _time = time;
    }

    public DateTime GetRangeStartUtc(DashboardRange range)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return range switch
        {
            DashboardRange.LastHour => now.AddHours(-1),
            DashboardRange.Last7Days => now.AddDays(-7),
            _ => now.AddHours(-24)
        };
    }

    public Task<DashboardSummary> GetSummaryAsync(DashboardRange range, CancellationToken ct = default)
        => _repository.GetSummaryAsync(GetRangeStartUtc(range), ct);

    public async Task<IReadOnlyList<UsageItem>> GetModelUsageAsync(DashboardRange range, CancellationToken ct = default)
        => await _repository.GetModelUsageAsync(GetRangeStartUtc(range), TopItems, ct);

    public async Task<IReadOnlyList<UsageItem>> GetToolUsageAsync(DashboardRange range, CancellationToken ct = default)
        => await _repository.GetToolUsageAsync(GetRangeStartUtc(range), TopItems, ct);

    /// <summary>
    /// Zaman serisini sabit sayıda kovaya böler: 1 sa → 12×5 dk, 24 sa → 24×1 sa, 7 gün → 28×6 sa.
    /// Boş kovalar da döner (grafikte boşluk oluşmaması için).
    /// </summary>
    public async Task<IReadOnlyList<TimelineBucket>> GetTimelineAsync(DashboardRange range, CancellationToken ct = default)
    {
        var (bucketSize, count) = range switch
        {
            DashboardRange.LastHour => (TimeSpan.FromMinutes(5), 12),
            DashboardRange.Last7Days => (TimeSpan.FromHours(6), 28),
            _ => (TimeSpan.FromHours(1), 24)
        };

        var now = _time.GetUtcNow().UtcDateTime;
        // Son kova şimdiki zamanı içerecek şekilde kova sınırına hizala
        var lastStart = new DateTime(now.Ticks - now.Ticks % bucketSize.Ticks, DateTimeKind.Utc);
        var firstStart = lastStart - bucketSize * (count - 1);

        var points = await _repository.GetTimelinePointsAsync(firstStart, ct);
        var buckets = new (long Requests, long Rejected, long Masked)[count];
        foreach (var p in points)
        {
            var index = (int)((p.OccurredAtUtc - firstStart).Ticks / bucketSize.Ticks);
            if (index < 0 || index >= count)
                continue;
            buckets[index].Requests++;
            if (p.Outcome == GuardrailOutcome.Reject) buckets[index].Rejected++;
            if (p.Outcome == GuardrailOutcome.Mask) buckets[index].Masked++;
        }

        return Enumerable.Range(0, count)
            .Select(i => new TimelineBucket(firstStart + bucketSize * i, buckets[i].Requests, buckets[i].Rejected, buckets[i].Masked))
            .ToList();
    }
}
