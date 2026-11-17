using AgentGatewayConsole.Web.Data.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentGatewayConsole.Web.Data.Db.Repositories;

public interface IAppSettingsRepository : IRepository<AppSetting>
{
    Task<Dictionary<string, string>> GetAllAsDictionaryAsync(CancellationToken ct = default);
}

public class AppSettingsRepository : Repository<AppSetting>, IAppSettingsRepository
{
    public AppSettingsRepository(ConsoleDbContext dbContext) : base(dbContext) { }

    public Task<Dictionary<string, string>> GetAllAsDictionaryAsync(CancellationToken ct = default)
        => DbContext.AppSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, ct);
}
