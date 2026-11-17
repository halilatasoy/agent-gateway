namespace AgentGatewayConsole.Web.Services;

/// <summary>
/// Uygulama logu için tek giriş noktası (webds LogService imzası, Serilog üzerinde hafif uyarlama).
/// Konteynerde stdout'a yazar; Rancher'daki "View Logs" ekranından okunur.
/// </summary>
public class LogService
{
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public virtual void LogError(string message, Exception? exception, string category)
        => _logger.LogError(exception, "[{Category}] {Message}", category, message);

    public virtual void LogWarning(string message, string category)
        => _logger.LogWarning("[{Category}] {Message}", category, message);

    public virtual void LogInformation(string message, string category)
        => _logger.LogInformation("[{Category}] {Message}", category, message);
}
