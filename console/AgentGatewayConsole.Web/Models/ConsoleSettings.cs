namespace AgentGatewayConsole.Web.Models;

/// <summary>appsettings.json → "Settings" bölümü (değişince restart gerekir).</summary>
public class ConsoleSettings
{
    public string Language { get; set; } = "tr";
    public int UiPort { get; set; } = 8080;
    public int OtlpPort { get; set; } = 4318;
    public long OtlpMaxBodyBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// true: /v1/logs yalnızca OTLP portundan, arayüz yalnızca UI portundan erişilebilir.
    /// Ingress UI portunu dışarı açtığı için sahte log gönderimini engeller. Testlerde kapatılır.
    /// </summary>
    public bool EnforcePortIsolation { get; set; } = true;
}

/// <summary>appsettings.json → "Ingest" bölümü.</summary>
public class IngestOptions
{
    /// <summary>
    /// Saklanmayacak nitelik önekleri (istem/yanıt içeriği). Panel yeni bir hassas veri deposuna dönüşmesin.
    /// </summary>
    public List<string> ExcludedAttributePrefixes { get; set; } = new();
}
