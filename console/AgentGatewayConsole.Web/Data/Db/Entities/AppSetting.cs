namespace AgentGatewayConsole.Web.Data.Db.Entities;

/// <summary>
/// Çalışma zamanında değiştirilebilen ayarlar (restart gerektirmez).
/// Örn. Retention:Days, Dashboard:RefreshSeconds.
/// </summary>
public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
