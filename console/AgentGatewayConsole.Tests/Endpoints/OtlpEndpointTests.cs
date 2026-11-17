using System.Net;
using System.Net.Http.Headers;
using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Services;
using Google.Protobuf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;

namespace AgentGatewayConsole.Tests.Endpoints;

/// <summary>
/// Gerçek HTTP hattı üzerinden OTLP alıcısı (TestServer + InMemory veritabanı).
/// </summary>
public class OtlpEndpointTests : IClassFixture<OtlpEndpointTests.ConsoleFactory>
{
    private readonly ConsoleFactory _factory;

    public OtlpEndpointTests(ConsoleFactory factory) => _factory = factory;

    public class ConsoleFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"otlp-endpoint-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // TestServer'da yerel port 0 olduğu için port izolasyonu kapatılır (ayrı testte sınanır).
            builder.UseSetting("Settings:EnforcePortIsolation", "false");
            builder.UseSetting("ConnectionStrings:Default", "");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ConsoleDbContext>>();
                services.AddDbContext<ConsoleDbContext>(o => o.UseInMemoryDatabase(_dbName));
            });
        }
    }

    private static ByteArrayContent Protobuf(IMessage message)
    {
        var content = new ByteArrayContent(message.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        return content;
    }

    private static ExportLogsServiceRequest RejectedLlmRequest(string marker)
    {
        var record = new LogRecord { ObservedTimeUnixNano = (ulong)(DateTime.UtcNow - DateTime.UnixEpoch).Ticks * 100UL };
        record.Attributes.Add(new KeyValue { Key = "protocol", Value = new AnyValue { StringValue = "llm" } });
        record.Attributes.Add(new KeyValue { Key = "gen_ai.request.model", Value = new AnyValue { StringValue = marker } });
        record.Attributes.Add(new KeyValue { Key = "http.response.status_code", Value = new AnyValue { IntValue = 400 } });
        record.Attributes.Add(new KeyValue
        {
            Key = "agw.ai.guardrails",
            Value = new AnyValue { StringValue = "[{\"phase\": \"request\", \"guard\": \"regex\", \"action\": \"reject\"}]" }
        });
        var scope = new ScopeLogs();
        scope.LogRecords.Add(record);
        var resource = new ResourceLogs();
        resource.ScopeLogs.Add(scope);
        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resource);
        return request;
    }

    [Fact]
    public async Task PostLogs_ValidProtobuf_Returns200AndStoresRecord()
    {
        var client = _factory.CreateClient();
        var marker = $"model-{Guid.NewGuid():N}";

        var response = await client.PostAsync("/v1/logs", Protobuf(RejectedLlmRequest(marker)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
        // Yanıt geçerli bir ExportLogsServiceResponse olmalı
        ExportLogsServiceResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsoleDbContext>();
        var stored = await db.GatewayRequestLogs.SingleAsync(x => x.RequestModel == marker);
        Assert.Equal(GuardrailOutcome.Reject, stored.GuardrailOutcome);
        Assert.Equal(400, stored.StatusCode);
    }

    [Fact]
    public async Task PostLogs_CapturedBody_StoresOnlyMaskedContent()
    {
        var client = _factory.CreateClient();
        var marker = $"model-{Guid.NewGuid():N}";
        var request = RejectedLlmRequest(marker);
        request.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Attributes.Add(new KeyValue
        {
            Key = ContentDetailBuilder.BodyAttribute,
            Value = new AnyValue { StringValue = TestContent.CopilotChainBody }
        });

        var response = await client.PostAsync("/v1/logs", Protobuf(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsoleDbContext>();
        var stored = await db.GatewayRequestLogs.Include(x => x.Content).SingleAsync(x => x.RequestModel == marker);
        Assert.Equal("TcKimlik,Email", stored.DetectedDataTypes);
        Assert.NotNull(stored.Content);
        Assert.Contains("<TC_KIMLIK_NO>", stored.Content!.DetailJson);
        // Ham değer veritabanının hiçbir alanına girmemeli.
        Assert.DoesNotContain("12345678901", stored.Content.DetailJson);
        Assert.DoesNotContain("12345678901", stored.AttributesJson);
    }

    [Fact]
    public async Task PostLogs_InvalidBody_Returns400()
    {
        var client = _factory.CreateClient();
        var content = new ByteArrayContent(new byte[] { 0xFF, 0xFF, 0xFF, 0x01, 0x02 });
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        var response = await client.PostAsync("/v1/logs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLogs_JsonContentType_Returns415()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/v1/logs", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_UiRoute_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task PostLogs_PortIsolationOnUiPort_Returns404()
    {
        // İzolasyon açıkken TestServer'ın yerel portu (0) OTLP portu değildir → /v1/logs reddedilmeli.
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("Settings:EnforcePortIsolation", "true")).CreateClient();

        var response = await client.PostAsync("/v1/logs", Protobuf(RejectedLlmRequest("isolated")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
