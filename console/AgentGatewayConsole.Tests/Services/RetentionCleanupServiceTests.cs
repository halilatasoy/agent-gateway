using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using static AgentGatewayConsole.Tests.Helpers.TestData;

namespace AgentGatewayConsole.Tests.Services;

public class RetentionCleanupServiceTests
{
    private static (RetentionCleanupService Service, ConsoleDbContext Db) Create(int retentionDays)
    {
        var db = TestDbContextFactory.Create();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<IGatewayRequestLogRepository>(sp => new GatewayRequestLogRepository(sp.GetRequiredService<ConsoleDbContext>()));
        var provider = services.BuildServiceProvider();

        var settings = new Mock<ISettingsProvider>();
        settings.Setup(s => s.GetIntAsync(SettingKeys.RetentionDays, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retentionDays);

        var service = new RetentionCleanupService(provider.GetRequiredService<IServiceScopeFactory>(), settings.Object,
            TestLog.Create(), new FixedTimeProvider(Now));
        return (service, db);
    }

    [Fact]
    public async Task RunOnceAsync_RecordsOlderThanRetention_AreDeleted()
    {
        var (service, db) = Create(retentionDays: 30);
        db.GatewayRequestLogs.AddRange(Log(Now.AddDays(-45)), Log(Now.AddDays(-2)));
        await db.SaveChangesAsync();

        var deleted = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Single(db.GatewayRequestLogs);
    }

    [Fact]
    public async Task RunOnceAsync_RetentionDisabled_DeletesNothing()
    {
        var (service, db) = Create(retentionDays: 0);
        db.GatewayRequestLogs.Add(Log(Now.AddDays(-400)));
        await db.SaveChangesAsync();

        var deleted = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Single(db.GatewayRequestLogs);
    }
}
