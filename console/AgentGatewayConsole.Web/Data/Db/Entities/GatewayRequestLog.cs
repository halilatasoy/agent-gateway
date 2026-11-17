namespace AgentGatewayConsole.Web.Data.Db.Entities;

/// <summary>
/// agentgateway'in OTLP ile gönderdiği tek bir erişim logu kaydı (metadata ve guardrail kararı).
/// İçerik yalnızca guardrail'in devreye girdiği isteklerde, maskelenerek ve ayrı tabloda
/// (<see cref="GatewayRequestContent"/>) tutulur.
/// Yüksek hacimli log tablosu: soft delete yok, saklama süresi dolunca fiziksel silinir.
/// </summary>
public class GatewayRequestLog
{
    public long Id { get; set; }

    /// <summary>Olayın zamanı (UTC). TimeUnixNano boşsa ObservedTimeUnixNano kullanılır.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>agentgateway protokolü: llm | mcp | http.</summary>
    public string Protocol { get; set; } = "http";

    public string? HttpMethod { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public int? DurationMs { get; set; }
    public string? ClientAddress { get; set; }
    public string? Gateway { get; set; }
    public string? Route { get; set; }

    // LLM alanları
    public string? Provider { get; set; }
    public string? RequestModel { get; set; }
    public string? ResponseModel { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }

    // MCP alanları
    public string? McpMethod { get; set; }
    public string? McpTarget { get; set; }
    public string? ToolName { get; set; }

    /// <summary>Kayıttaki en ağır guardrail kararı: Reject > Mask > Allow > None.</summary>
    public GuardrailOutcome GuardrailOutcome { get; set; }

    /// <summary>Belirleyici kararın aşaması (request | response).</summary>
    public string? GuardrailPhase { get; set; }

    /// <summary>agentgateway'in gönderdiği ham guardrail metni (JSON benzeri string).</summary>
    public string? GuardrailsRaw { get; set; }

    public string? Reason { get; set; }
    public string? Error { get; set; }

    /// <summary>Tüm OTLP nitelikleri (içerik nitelikleri hariç), jsonb.</summary>
    public string? AttributesJson { get; set; }

    /// <summary>
    /// Panelin içerikte bulduğu veri türleri, virgülle ayrılmış (ör. "TcKimlik,Email").
    /// İçerik yakalanmadıysa boştur.
    /// </summary>
    public string? DetectedDataTypes { get; set; }

    /// <summary>Maskelenmiş içerik ayrıntısı; yalnızca içerik yakalanan kayıtlarda vardır.</summary>
    public GatewayRequestContent? Content { get; set; }
}

/// <summary>Guardrail kararı; sayısal değer ağırlık sırasını verir.</summary>
public enum GuardrailOutcome
{
    None = 0,
    Allow = 1,
    Mask = 2,
    Reject = 3
}
