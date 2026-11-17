using System.Globalization;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace AgentGatewayConsole.Web.Services;

public interface ISettingsProvider
{
    Task<int> GetIntAsync(string key, int defaultValue, CancellationToken ct = default);
}

/// <summary>
/// DB AppSettings tablosundan ayar okur; 60 sn bellek önbelleği (webds GeneralSettingsProvider deseni).
/// Değer DB'de değişince restart gerekmez, en geç 60 sn içinde etkinleşir.
/// </summary>
public class SettingsProvider : ISettingsProvider
{
    private const string CacheKey = "AppSettings:All";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly LogService _log;

    public SettingsProvider(IServiceScopeFactory scopeFactory, IMemoryCache cache, LogService log)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _log = log;
    }

    public async Task<int> GetIntAsync(string key, int defaultValue, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : defaultValue;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
            return cached;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAppSettingsRepository>();
            var all = await repo.GetAllAsDictionaryAsync(ct);
            _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)all, CacheDuration);
            return all;
        }
        catch (Exception ex)
        {
            // DB erişilemezse varsayılanlarla devam edilir; kısa süre sonra tekrar denenir.
            _log.LogError("Ayarlar okunamadı, varsayılanlar kullanılıyor", ex, nameof(SettingsProvider));
            return new Dictionary<string, string>();
        }
    }
}
