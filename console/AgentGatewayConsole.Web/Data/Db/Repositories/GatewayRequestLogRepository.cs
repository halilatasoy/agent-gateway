using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentGatewayConsole.Web.Data.Db.Repositories;

public class GatewayRequestLogRepository : Repository<GatewayRequestLog>, IGatewayRequestLogRepository
{
    private const string ToolCallMethod = "tools/call";

    public GatewayRequestLogRepository(ConsoleDbContext dbContext) : base(dbContext) { }

    private IQueryable<GatewayRequestLog> Since(DateTime fromUtc)
        => DbContext.GatewayRequestLogs.AsNoTracking().Where(x => x.OccurredAtUtc >= fromUtc);

    public async Task<DashboardSummary> GetSummaryAsync(DateTime fromUtc, CancellationToken ct = default)
    {
        var q = Since(fromUtc);

        // Tek sorguda toplamlar (GroupBy sabit anahtar ile)
        var totals = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.LongCount(),
                Llm = g.LongCount(x => x.Protocol == "llm"),
                ToolCalls = g.LongCount(x => x.McpMethod == ToolCallMethod),
                Input = g.Sum(x => x.InputTokens ?? 0),
                Output = g.Sum(x => x.OutputTokens ?? 0),
                Rejected = g.LongCount(x => x.GuardrailOutcome == GuardrailOutcome.Reject),
                Masked = g.LongCount(x => x.GuardrailOutcome == GuardrailOutcome.Mask),
                // Guardrail engellemeleri ayrı sayıldığı için hata oranına katılmaz.
                Failed = g.LongCount(x => x.StatusCode >= 400 && x.GuardrailOutcome != GuardrailOutcome.Reject)
            })
            .FirstOrDefaultAsync(ct);

        if (totals is null)
            return new DashboardSummary(0, 0, 0, 0, 0, 0, 0, 0, 0);

        var rate = totals.Total == 0 ? 0 : Math.Round(totals.Failed * 100.0 / totals.Total, 1);
        return new DashboardSummary(totals.Total, totals.Llm, totals.ToolCalls, totals.Input, totals.Output,
            totals.Rejected, totals.Masked, totals.Failed, rate);
    }

    public Task<List<TimelinePoint>> GetTimelinePointsAsync(DateTime fromUtc, CancellationToken ct = default)
        => Since(fromUtc)
            .Select(x => new TimelinePoint(x.OccurredAtUtc, x.GuardrailOutcome))
            .ToListAsync(ct);

    // Not: Sıralama anonim tip üzerinde yapılır; EF Core constructor ile kurulan record'un
    // özelliğine göre sıralamayı SQL'e çeviremez. Record'a dönüşüm sorgu sonrasında yapılır.
    public async Task<List<UsageItem>> GetModelUsageAsync(DateTime fromUtc, int take, CancellationToken ct = default)
    {
        var rows = await Since(fromUtc)
            .Where(x => x.Protocol == "llm" && x.RequestModel != null)
            .GroupBy(x => x.RequestModel!)
            .Select(g => new { Name = g.Key, Count = g.LongCount(), Tokens = g.Sum(x => (x.InputTokens ?? 0) + (x.OutputTokens ?? 0)) })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => new UsageItem(r.Name, r.Count, r.Tokens)).ToList();
    }

    public async Task<List<UsageItem>> GetToolUsageAsync(DateTime fromUtc, int take, CancellationToken ct = default)
    {
        var rows = await Since(fromUtc)
            .Where(x => x.McpMethod == ToolCallMethod && x.ToolName != null)
            .GroupBy(x => x.ToolName!)
            .Select(g => new { Name = g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => new UsageItem(r.Name, r.Count, 0)).ToList();
    }

    public Task<List<GatewayRequestLog>> GetProtectedEventsAsync(DateTime fromUtc, int take, CancellationToken ct = default)
        => Since(fromUtc)
            .Where(x => x.GuardrailOutcome == GuardrailOutcome.Reject || x.GuardrailOutcome == GuardrailOutcome.Mask)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task<PagedResult<GatewayRequestLog>> GetTrafficPageAsync(TrafficQuery query, CancellationToken ct = default)
    {
        var all = DbContext.GatewayRequestLogs.AsNoTracking();
        var total = await all.LongCountAsync(ct);

        var q = all;
        if (!string.IsNullOrWhiteSpace(query.Protocol))
            q = q.Where(x => x.Protocol == query.Protocol);
        if (!string.IsNullOrWhiteSpace(query.Model))
            q = q.Where(x => x.RequestModel == query.Model);
        if (query.Outcome.HasValue)
            q = q.Where(x => x.GuardrailOutcome == query.Outcome.Value);
        q = query.StatusClass switch
        {
            "2xx" => q.Where(x => x.StatusCode >= 200 && x.StatusCode < 300),
            "4xx" => q.Where(x => x.StatusCode >= 400 && x.StatusCode < 500),
            "5xx" => q.Where(x => x.StatusCode >= 500),
            _ => q
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(x =>
                (x.Path != null && x.Path.Contains(s)) ||
                (x.RequestModel != null && x.RequestModel.Contains(s)) ||
                (x.ToolName != null && x.ToolName.Contains(s)) ||
                (x.McpMethod != null && x.McpMethod.Contains(s)) ||
                (x.ClientAddress != null && x.ClientAddress.Contains(s)));
        }

        var filtered = await q.LongCountAsync(ct);

        // Sıralama yalnızca beyaz listedeki alanlarla (string birleştirme yok)
        q = (query.OrderBy, query.Descending) switch
        {
            ("statusCode", true) => q.OrderByDescending(x => x.StatusCode),
            ("statusCode", false) => q.OrderBy(x => x.StatusCode),
            ("durationMs", true) => q.OrderByDescending(x => x.DurationMs),
            ("durationMs", false) => q.OrderBy(x => x.DurationMs),
            ("protocol", true) => q.OrderByDescending(x => x.Protocol),
            ("protocol", false) => q.OrderBy(x => x.Protocol),
            ("occurredAtUtc", false) => q.OrderBy(x => x.OccurredAtUtc),
            _ => q.OrderByDescending(x => x.OccurredAtUtc)
        };

        var length = Math.Clamp(query.Length, 1, 500);
        var items = await q.Skip(Math.Max(0, query.Start)).Take(length).ToListAsync(ct);
        return new PagedResult<GatewayRequestLog>(total, filtered, items);
    }

    public Task<List<string>> GetDistinctModelsAsync(CancellationToken ct = default)
        => DbContext.GatewayRequestLogs.AsNoTracking()
            .Where(x => x.RequestModel != null)
            .Select(x => x.RequestModel!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        // ExecuteDelete InMemory sağlayıcıda desteklenmediği için partili RemoveRange kullanılır.
        var deleted = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await DbContext.GatewayRequestLogs
                .Where(x => x.OccurredAtUtc < cutoffUtc)
                .OrderBy(x => x.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;
            DbContext.GatewayRequestLogs.RemoveRange(batch);
            deleted += await DbContext.SaveChangesAsync(ct);
            DbContext.ChangeTracker.Clear();
        }
        return deleted;
    }
}
