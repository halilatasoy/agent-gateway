using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Repositories;

namespace AgentGatewayConsole.Web.Services;

/// <summary>
/// Saklama süresi dolan kayıtları saatte bir siler. Süre DB AppSettings'teki Retention:Days'ten okunur
/// (restart gerektirmez). Hangfire bu tek iş için gereksiz; BackgroundService yeterli.
/// </summary>
public class RetentionCleanupService : BackgroundService
{
    private const int DefaultRetentionDays = 30;
    private const int BatchSize = 5000;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsProvider _settings;
    private readonly LogService _log;
    private readonly TimeProvider _time;

    public RetentionCleanupService(IServiceScopeFactory scopeFactory, ISettingsProvider settings, LogService log, TimeProvider time)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _log = log;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Açılışta veritabanı migration'ının bitmesine zaman tanı.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Tek temizlik turu; testlerden doğrudan çağrılabilir.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var days = await _settings.GetIntAsync(SettingKeys.RetentionDays, DefaultRetentionDays, ct);
            if (days <= 0)
                return 0; // 0 veya negatif: temizlik kapalı
            var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-days);

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IGatewayRequestLogRepository>();
            var deleted = await repo.DeleteOlderThanAsync(cutoff, BatchSize, ct);
            if (deleted > 0)
                _log.LogInformation($"{deleted} kayıt saklama süresi ({days} gün) dolduğu için silindi", nameof(RetentionCleanupService));
            return deleted;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _log.LogError("Saklama temizliği başarısız", ex, nameof(RetentionCleanupService));
            return 0;
        }
    }
}
