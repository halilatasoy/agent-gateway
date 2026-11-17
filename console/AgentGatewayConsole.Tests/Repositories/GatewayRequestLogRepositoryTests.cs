using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;
using static AgentGatewayConsole.Tests.Helpers.TestData;

namespace AgentGatewayConsole.Tests.Repositories;

public class GatewayRequestLogRepositoryTests
{
    private static async Task<GatewayRequestLogRepository> SeedAsync(params GatewayRequestLog[] logs)
    {
        var db = TestDbContextFactory.Create();
        db.GatewayRequestLogs.AddRange(logs);
        await db.SaveChangesAsync();
        return new GatewayRequestLogRepository(db);
    }

    [Fact]
    public async Task GetSummaryAsync_MixedRecords_CountsRejectedMaskedAndTokens()
    {
        var repo = await SeedAsync(
            Log(Now.AddMinutes(-5), outcome: GuardrailOutcome.Reject, status: 400),
            Log(Now.AddMinutes(-4), outcome: GuardrailOutcome.Mask),
            Log(Now.AddMinutes(-3)),
            Log(Now.AddMinutes(-2), protocol: "mcp", outcome: GuardrailOutcome.None, tool: "echo"),
            Log(Now.AddMinutes(-1), status: 500));

        var s = await repo.GetSummaryAsync(Now.AddHours(-1));

        Assert.Equal(5, s.TotalRequests);
        Assert.Equal(4, s.LlmRequests);
        Assert.Equal(1, s.McpToolCalls);
        Assert.Equal(1, s.Rejected);
        Assert.Equal(1, s.Masked);
        Assert.Equal(40, s.InputTokens);
        Assert.Equal(20, s.OutputTokens);
        // Guardrail engellemesi (400) hata sayılmaz; yalnızca 500 sayılır.
        Assert.Equal(1, s.FailedRequests);
        Assert.Equal(20.0, s.FailureRate);
    }

    [Fact]
    public async Task GetSummaryAsync_RecordsOutsideRange_AreExcluded()
    {
        var repo = await SeedAsync(Log(Now.AddHours(-2), outcome: GuardrailOutcome.Reject), Log(Now.AddMinutes(-10)));

        var s = await repo.GetSummaryAsync(Now.AddHours(-1));

        Assert.Equal(1, s.TotalRequests);
        Assert.Equal(0, s.Rejected);
    }

    [Fact]
    public async Task GetSummaryAsync_NoRecords_ReturnsZeros()
    {
        var repo = await SeedAsync();

        var s = await repo.GetSummaryAsync(Now.AddHours(-1));

        Assert.Equal(0, s.TotalRequests);
        Assert.Equal(0, s.FailureRate);
    }

    [Fact]
    public async Task GetModelUsageAsync_MultipleModels_OrdersByCount()
    {
        var repo = await SeedAsync(Log(Now, model: "a"), Log(Now, model: "b"), Log(Now, model: "b"));

        var usage = await repo.GetModelUsageAsync(Now.AddHours(-1), 10);

        Assert.Equal("b", usage[0].Name);
        Assert.Equal(2, usage[0].Count);
        Assert.Equal(30, usage[0].Tokens);
    }

    [Fact]
    public async Task GetProtectedEventsAsync_MixedOutcomes_ReturnsOnlyRejectAndMaskNewestFirst()
    {
        var repo = await SeedAsync(
            Log(Now.AddMinutes(-3), outcome: GuardrailOutcome.Reject),
            Log(Now.AddMinutes(-2), outcome: GuardrailOutcome.Allow),
            Log(Now.AddMinutes(-1), outcome: GuardrailOutcome.Mask));

        var events = await repo.GetProtectedEventsAsync(Now.AddHours(-1), 100);

        Assert.Equal(2, events.Count);
        Assert.Equal(GuardrailOutcome.Mask, events[0].GuardrailOutcome);
        Assert.Equal(GuardrailOutcome.Reject, events[1].GuardrailOutcome);
    }

    [Fact]
    public async Task GetTrafficPageAsync_FilterByProtocol_ReturnsOnlyMatching()
    {
        var repo = await SeedAsync(Log(Now), Log(Now, protocol: "mcp", tool: "echo"), Log(Now));

        var page = await repo.GetTrafficPageAsync(new TrafficQuery { Protocol = "mcp" });

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(1, page.FilteredCount);
        Assert.Equal("mcp", page.Items.Single().Protocol);
    }

    [Fact]
    public async Task GetTrafficPageAsync_Paging_ReturnsRequestedSliceNewestFirst()
    {
        var logs = Enumerable.Range(0, 30).Select(i => Log(Now.AddMinutes(-i))).ToArray();
        var repo = await SeedAsync(logs);

        var page = await repo.GetTrafficPageAsync(new TrafficQuery { Start = 10, Length = 10 });

        Assert.Equal(10, page.Items.Count);
        Assert.Equal(Now.AddMinutes(-10), page.Items[0].OccurredAtUtc);
    }

    [Fact]
    public async Task GetTrafficPageAsync_StatusClassAndOutcome_CombinesFilters()
    {
        var repo = await SeedAsync(
            Log(Now, status: 400, outcome: GuardrailOutcome.Reject),
            Log(Now, status: 404),
            Log(Now, status: 200));

        var page = await repo.GetTrafficPageAsync(new TrafficQuery { StatusClass = "4xx", Outcome = GuardrailOutcome.Reject });

        Assert.Single(page.Items);
        Assert.Equal(400, page.Items[0].StatusCode);
    }

    [Fact]
    public async Task DeleteOlderThanAsync_OldRecords_AreRemovedAndRecentKept()
    {
        var repo = await SeedAsync(Log(Now.AddDays(-40)), Log(Now.AddDays(-31)), Log(Now.AddDays(-1)));

        var deleted = await repo.DeleteOlderThanAsync(Now.AddDays(-30), batchSize: 1);

        Assert.Equal(2, deleted);
        var remaining = await repo.GetAllAsync();
        Assert.Single(remaining);
    }

    [Fact]
    public async Task DeleteOlderThanAsync_NoOldRecords_DeletesNothing()
    {
        var repo = await SeedAsync(Log(Now.AddDays(-1)));

        var deleted = await repo.DeleteOlderThanAsync(Now.AddDays(-30), batchSize: 100);

        Assert.Equal(0, deleted);
    }
}
