using System.Text.Json;
using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Controllers;
using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using static AgentGatewayConsole.Tests.Helpers.TestData;

namespace AgentGatewayConsole.Tests.Controllers;

public class ControllerTests
{
    private static JsonElement ToJson(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        return JsonSerializer.SerializeToElement(json.Value);
    }

    private static Mock<ISettingsProvider> Settings()
    {
        var mock = new Mock<ISettingsProvider>();
        mock.Setup(s => s.GetIntAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, int d, CancellationToken _) => d);
        return mock;
    }

    [Fact]
    public async Task DashboardGetData_ValidRange_ReturnsSuccessWithSummary()
    {
        var db = TestDbContextFactory.Create();
        db.GatewayRequestLogs.Add(Log(DateTime.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();
        var dashboard = new DashboardService(new GatewayRequestLogRepository(db), TimeProvider.System);
        var controller = new DashboardController(dashboard, Settings().Object, TestMessageServiceFactory.Create(), TestLog.Create());

        var json = ToJson(await controller.GetData(DashboardRange.LastHour));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.GetProperty("data").GetProperty("summary").GetProperty("TotalRequests").GetInt64());
    }

    [Fact]
    public async Task DashboardGetData_ServiceThrows_ReturnsErrorMessage()
    {
        var dashboard = new Mock<IDashboardService>();
        dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<DashboardRange>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var controller = new DashboardController(dashboard.Object, Settings().Object, TestMessageServiceFactory.Create(), TestLog.Create());

        var json = ToJson(await controller.GetData());

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("message").GetString()));
    }

    private static ProtectedDataController ProtectedController(ConsoleDbContext db, string language = "tr")
    {
        var repo = new GatewayRequestLogRepository(db);
        return new ProtectedDataController(repo, new GatewayRequestContentRepository(db), new DashboardService(repo, TimeProvider.System),
            Settings().Object, TestMessageServiceFactory.Create(language), TestLog.Create());
    }

    [Fact]
    public async Task ProtectedDataGetDetail_InvalidId_ReturnsNotFoundMessage()
    {
        var db = TestDbContextFactory.Create();
        var controller = ProtectedController(db);

        var json = ToJson(await controller.GetDetail(999));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("Kayıt bulunamadı.", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ProtectedDataGetDetail_ExistingId_ReturnsPrettyGuardrails()
    {
        var db = TestDbContextFactory.Create();
        var log = Log(DateTime.UtcNow, outcome: Web.Data.Db.Entities.GuardrailOutcome.Reject);
        log.GuardrailsRaw = "[{\"phase\": \"request\", \"action\": \"reject\"}]";
        db.GatewayRequestLogs.Add(log);
        await db.SaveChangesAsync();
        var controller = ProtectedController(db);

        var json = ToJson(await controller.GetDetail(log.Id));

        Assert.True(json.GetProperty("success").GetBoolean());
        var data = json.GetProperty("data");
        Assert.Equal("Reject", data.GetProperty("outcome").GetString());
        Assert.Contains("\n", data.GetProperty("guardrails").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task ProtectedDataGetDetail_WithCapturedContent_ReturnsFlowAndDataTypes()
    {
        var db = TestDbContextFactory.Create();
        var mapper = new OtlpLogMapper(Microsoft.Extensions.Options.Options.Create(new IngestOptions()), TestContent.Builder());
        var record = new OpenTelemetry.Proto.Logs.V1.LogRecord();
        record.Attributes.Add(new OpenTelemetry.Proto.Common.V1.KeyValue
        {
            Key = ContentDetailBuilder.BodyAttribute,
            Value = new OpenTelemetry.Proto.Common.V1.AnyValue { StringValue = TestContent.CopilotChainBody }
        });
        var log = mapper.Map(record);
        log.GuardrailOutcome = Web.Data.Db.Entities.GuardrailOutcome.Reject;
        db.GatewayRequestLogs.Add(log);
        await db.SaveChangesAsync();
        var controller = ProtectedController(db);

        var json = ToJson(await controller.GetDetail(log.Id));

        var data = json.GetProperty("data");
        Assert.Equal(new[] { "TcKimlik", "Email" }, data.GetProperty("dataTypes").EnumerateArray().Select(x => x.GetString()).ToArray());
        var messages = data.GetProperty("content").GetProperty("Messages");
        Assert.Equal(6, messages.GetArrayLength());
        Assert.Contains(messages.EnumerateArray(), m => m.GetProperty("ToolName").GetString() == "view");
        Assert.DoesNotContain("12345678901", json.GetRawText());
    }

    [Fact]
    public async Task ProtectedDataGetList_DetectedTypes_ReturnedAsArray()
    {
        var db = TestDbContextFactory.Create();
        var log = Log(DateTime.UtcNow.AddMinutes(-1), outcome: Web.Data.Db.Entities.GuardrailOutcome.Mask);
        log.DetectedDataTypes = "Email,Phone";
        db.GatewayRequestLogs.Add(log);
        await db.SaveChangesAsync();
        var controller = ProtectedController(db);

        var json = ToJson(await controller.GetList(DashboardRange.LastHour));

        var row = Assert.Single(json.GetProperty("data").EnumerateArray());
        Assert.Equal(new[] { "Email", "Phone" }, row.GetProperty("dataTypes").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task ProtectedDataGetList_RepositoryThrows_ReturnsEmptyDataWithMessage()
    {
        var repo = new Mock<IGatewayRequestLogRepository>();
        repo.Setup(r => r.GetProtectedEventsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var controller = new ProtectedDataController(repo.Object, new Mock<IGatewayRequestContentRepository>().Object,
            new DashboardService(repo.Object, TimeProvider.System), Settings().Object, TestMessageServiceFactory.Create(), TestLog.Create());

        var json = ToJson(await controller.GetList());

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.GetProperty("data").GetArrayLength());
    }
}
