using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using static AgentGatewayConsole.Tests.Helpers.TestData;

namespace AgentGatewayConsole.Tests.Services;

public class DashboardServiceTests
{
    private static async Task<DashboardService> CreateAsync(params GatewayRequestLog[] logs)
    {
        var db = TestDbContextFactory.Create();
        db.GatewayRequestLogs.AddRange(logs);
        await db.SaveChangesAsync();
        return new DashboardService(new GatewayRequestLogRepository(db), new FixedTimeProvider(Now));
    }

    [Theory]
    [InlineData(DashboardRange.LastHour, 12)]
    [InlineData(DashboardRange.Last24Hours, 24)]
    [InlineData(DashboardRange.Last7Days, 28)]
    public async Task GetTimelineAsync_EachRange_ReturnsFixedBucketCount(DashboardRange range, int expected)
    {
        var service = await CreateAsync();

        var buckets = await service.GetTimelineAsync(range);

        Assert.Equal(expected, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(0, b.Requests));
    }

    [Fact]
    public async Task GetTimelineAsync_Last24Hours_PutsRecordsInCorrectBuckets()
    {
        // Kovalar saat başına hizalıdır; son kova "içinde bulunulan saat"tir (12:00–13:00).
        var now = Now.AddMinutes(30); // 12:30
        var db = TestDbContextFactory.Create();
        db.GatewayRequestLogs.AddRange(
            Log(now.AddMinutes(-10), outcome: GuardrailOutcome.Reject),   // 12:20 → son kova
            Log(now.AddMinutes(-20), outcome: GuardrailOutcome.Mask),     // 12:10 → son kova
            Log(now.AddHours(-3)));                                        // 09:30 → 09:00 kovası (sondan 4.)
        await db.SaveChangesAsync();
        var service = new DashboardService(new GatewayRequestLogRepository(db), new FixedTimeProvider(now));

        var buckets = await service.GetTimelineAsync(DashboardRange.Last24Hours);

        var last = buckets[^1];
        Assert.Equal(new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc), last.StartUtc);
        Assert.Equal(2, last.Requests);
        Assert.Equal(1, last.Rejected);
        Assert.Equal(1, last.Masked);
        Assert.Equal(1, buckets[^4].Requests);
        Assert.Equal(3, buckets.Sum(b => b.Requests));
    }

    [Fact]
    public async Task GetTimelineAsync_RecordOlderThanRange_IsIgnored()
    {
        var service = await CreateAsync(Log(Now.AddDays(-2)));

        var buckets = await service.GetTimelineAsync(DashboardRange.Last24Hours);

        Assert.Equal(0, buckets.Sum(b => b.Requests));
    }

    [Theory]
    [InlineData(DashboardRange.LastHour, 1)]
    [InlineData(DashboardRange.Last24Hours, 24)]
    [InlineData(DashboardRange.Last7Days, 168)]
    public async Task GetRangeStartUtc_EachRange_ReturnsExpectedOffset(DashboardRange range, int hours)
    {
        var service = await CreateAsync();

        Assert.Equal(Now.AddHours(-hours), service.GetRangeStartUtc(range));
    }
}
