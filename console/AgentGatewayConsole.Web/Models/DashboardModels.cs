using AgentGatewayConsole.Web.Data.Db.Entities;

namespace AgentGatewayConsole.Web.Models;

/// <summary>Özet ekranındaki zaman aralığı seçenekleri.</summary>
public enum DashboardRange
{
    LastHour,
    Last24Hours,
    Last7Days
}

public record DashboardSummary(
    long TotalRequests,
    long LlmRequests,
    long McpToolCalls,
    long InputTokens,
    long OutputTokens,
    long Rejected,
    long Masked,
    long FailedRequests,
    double FailureRate);

public record TimelineBucket(DateTime StartUtc, long Requests, long Rejected, long Masked);

public record UsageItem(string Name, long Count, long Tokens);

/// <summary>Zaman serisi için hafif projeksiyon (tüm satırı çekmemek için).</summary>
public record TimelinePoint(DateTime OccurredAtUtc, GuardrailOutcome Outcome);

/// <summary>Trafik Kaydı ekranı için server-side DataTables sorgusu.</summary>
public class TrafficQuery
{
    public int Start { get; set; }
    public int Length { get; set; } = 25;
    public string? Search { get; set; }
    public string? Protocol { get; set; }

    /// <summary>"2xx" | "4xx" | "5xx" veya boş.</summary>
    public string? StatusClass { get; set; }

    public string? Model { get; set; }
    public GuardrailOutcome? Outcome { get; set; }

    /// <summary>Sıralanacak alan adı (beyaz listeden) ve yön.</summary>
    public string? OrderBy { get; set; }
    public bool Descending { get; set; } = true;
}

public record PagedResult<T>(long TotalCount, long FilteredCount, IReadOnlyList<T> Items);
