using System.Text.Json;
using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace AgentGatewayConsole.Tests.Services;

public class OtlpLogMapperTests
{
    private static OtlpLogMapper CreateMapper(params string[] excludedPrefixes) => new(Options.Create(new IngestOptions
    {
        ExcludedAttributePrefixes = excludedPrefixes.Length > 0 ? excludedPrefixes.ToList() : new() { "llm.prompt", "llm.completion" }
    }), TestContent.Builder());

    private static KeyValue Str(string key, string value) => new() { Key = key, Value = new AnyValue { StringValue = value } };
    private static KeyValue Int(string key, long value) => new() { Key = key, Value = new AnyValue { IntValue = value } };

    [Fact]
    public void Map_LlmRecordWithOtlpNames_MapsTypedFields()
    {
        var record = new LogRecord { ObservedTimeUnixNano = 1_794_744_000_000_000_000UL };
        record.Attributes.Add(new[]
        {
            Str("protocol", "llm"), Str("http.request.method", "POST"), Str("url.path", "/v1/chat/completions"),
            Int("http.response.status_code", 200), Str("client.address", "10.42.0.1"), Str("gen_ai.provider.name", "ollama"),
            Str("gen_ai.request.model", "qwen3:14b"), Str("gen_ai.response.model", "qwen3:14b"),
            Int("gen_ai.usage.input_tokens", 24), Int("gen_ai.usage.output_tokens", 160), Str("duration", "20786ms")
        });

        var log = CreateMapper().Map(record);

        Assert.Equal("llm", log.Protocol);
        Assert.Equal("POST", log.HttpMethod);
        Assert.Equal("/v1/chat/completions", log.Path);
        Assert.Equal(200, log.StatusCode);
        Assert.Equal("10.42.0.1", log.ClientAddress);
        Assert.Equal("ollama", log.Provider);
        Assert.Equal("qwen3:14b", log.RequestModel);
        Assert.Equal(24, log.InputTokens);
        Assert.Equal(160, log.OutputTokens);
        Assert.Equal(20786, log.DurationMs);
        Assert.Equal(GuardrailOutcome.None, log.GuardrailOutcome);
        Assert.Equal(DateTimeKind.Utc, log.OccurredAtUtc.Kind);
    }

    [Fact]
    public void Map_LegacyStdoutNames_FallsBackToOldKeys()
    {
        var record = new LogRecord();
        record.Attributes.Add(new[] { Str("http.method", "GET"), Str("http.path", "/v1/models"), Str("http.status", "200"), Str("src.addr", "127.0.0.1:5000") });

        var log = CreateMapper().Map(record);

        Assert.Equal("GET", log.HttpMethod);
        Assert.Equal("/v1/models", log.Path);
        Assert.Equal(200, log.StatusCode);
        Assert.Equal("http", log.Protocol);
    }

    [Fact]
    public void Map_McpToolCall_MapsToolFields()
    {
        var record = new LogRecord();
        record.Attributes.Add(new[] { Str("protocol", "mcp"), Str("mcp.method.name", "tools/call"), Str("mcp.target", "everything"), Str("gen_ai.tool.name", "echo") });

        var log = CreateMapper().Map(record);

        Assert.Equal("mcp", log.Protocol);
        Assert.Equal("tools/call", log.McpMethod);
        Assert.Equal("everything", log.McpTarget);
        Assert.Equal("echo", log.ToolName);
    }

    [Fact]
    public void Map_GuardrailRejectString_SetsRejectOutcomeAndPhase()
    {
        var record = new LogRecord();
        record.Attributes.Add(new[]
        {
            Str("agw.ai.guardrails", "[{\"phase\": \"request\", \"guard\": \"regex\", \"action\": \"reject\"}]"),
            Str("reason", "Guardrail"), Str("error", "request rejected by regex guardrail"), Int("http.response.status_code", 400)
        });

        var log = CreateMapper().Map(record);

        Assert.Equal(GuardrailOutcome.Reject, log.GuardrailOutcome);
        Assert.Equal("request", log.GuardrailPhase);
        Assert.Equal("Guardrail", log.Reason);
        Assert.Equal("request rejected by regex guardrail", log.Error);
    }

    [Fact]
    public void Map_TimeUnixNanoZero_UsesObservedTime()
    {
        var observed = new DateTime(2026, 11, 15, 10, 30, 0, DateTimeKind.Utc);
        var nanos = (ulong)(observed - DateTime.UnixEpoch).Ticks * 100UL;
        var record = new LogRecord { TimeUnixNano = 0, ObservedTimeUnixNano = nanos };

        var log = CreateMapper().Map(record);

        Assert.Equal(observed, log.OccurredAtUtc);
    }

    [Fact]
    public void Map_ExcludedContentAttribute_IsNotStored()
    {
        var record = new LogRecord();
        record.Attributes.Add(new[] { Str("protocol", "llm"), Str("llm.prompt", "TC 12345678950"), Str("llm.completion", "gizli") });

        var log = CreateMapper().Map(record);

        var attrs = JsonSerializer.Deserialize<Dictionary<string, object>>(log.AttributesJson!)!;
        Assert.False(attrs.ContainsKey("llm.prompt"));
        Assert.False(attrs.ContainsKey("llm.completion"));
        Assert.True(attrs.ContainsKey("protocol"));
        Assert.DoesNotContain("12345678950", log.AttributesJson);
    }

    [Fact]
    public void Map_CapturedBody_StoresMaskedContentAndNoRawValue()
    {
        var record = new LogRecord();
        record.Attributes.Add(new[]
        {
            Str("protocol", "llm"),
            Str("agw.ai.guardrails", "[{\"phase\": \"request\", \"guard\": \"regex\", \"action\": \"reject\"}]"),
            Str(ContentDetailBuilder.BodyAttribute, TestContent.CopilotChainBody),
            Str(ContentDetailBuilder.ResponseToolCallsAttribute, "[]")
        });

        var log = CreateMapper().Map(record);

        Assert.NotNull(log.Content);
        Assert.Equal("TcKimlik,Email", log.DetectedDataTypes);
        Assert.Contains("<TC_KIMLIK_NO>", log.Content!.DetailJson);
        Assert.DoesNotContain("12345678901", log.Content.DetailJson);
        Assert.DoesNotContain("deneme@ornek.com", log.Content.DetailJson);
        Assert.DoesNotContain("agwc.", log.AttributesJson);
        Assert.DoesNotContain("12345678901", log.AttributesJson);
    }

    [Fact]
    public void Map_CaptureAttributesEmpty_LeavesContentNull()
    {
        // Guardrail devreye girmediğinde gateway'in koşullu ifadesi boş değer gönderir.
        var record = new LogRecord();
        record.Attributes.Add(new[]
        {
            Str("protocol", "llm"), Str(ContentDetailBuilder.BodyAttribute, ""), Str(ContentDetailBuilder.ResponseToolCallsAttribute, "[]")
        });

        var log = CreateMapper().Map(record);

        Assert.Null(log.Content);
        Assert.Null(log.DetectedDataTypes);
    }

    [Fact]
    public void Map_AgwcPrefixNotInConfig_StillExcludedFromAttributes()
    {
        var record = new LogRecord();
        record.Attributes.Add(new[] { Str("protocol", "llm"), Str(ContentDetailBuilder.BodyAttribute, "{\"messages\":[{\"role\":\"user\",\"content\":\"TC 12345678901\"}]}") });

        var log = CreateMapper("gen_ai.prompt").Map(record);

        Assert.DoesNotContain(ContentDetailBuilder.BodyAttribute, log.AttributesJson);
        Assert.DoesNotContain("12345678901", log.AttributesJson);
    }

    [Fact]
    public void Map_RequestWithResource_UsesServiceNameAsGateway()
    {
        var request = new ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs { Resource = new Resource() };
        resourceLogs.Resource.Attributes.Add(Str("service.name", "agentgateway-proxy"));
        var scope = new ScopeLogs();
        scope.LogRecords.Add(new LogRecord());
        scope.LogRecords.Add(new LogRecord());
        resourceLogs.ScopeLogs.Add(scope);
        request.ResourceLogs.Add(resourceLogs);

        var logs = CreateMapper().Map(request).ToList();

        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal("agentgateway-proxy", l.Gateway));
    }

    [Theory]
    [InlineData("[{\"phase\": \"request\", \"action\": \"allow\"}, {\"phase\": \"request\", \"action\": \"mask\"}, {\"phase\": \"response\", \"action\": \"allow\"}]", GuardrailOutcome.Mask, "request")]
    [InlineData("[{\"phase\": \"request\", \"action\": \"allow\"}, {\"phase\": \"response\", \"action\": \"allow\"}]", GuardrailOutcome.Allow, "request")]
    [InlineData("[{\"phase\": \"response\", \"action\": \"mask\"}]", GuardrailOutcome.Mask, "response")]
    [InlineData("", GuardrailOutcome.None, null)]
    [InlineData(null, GuardrailOutcome.None, null)]
    public void ParseGuardrails_VariousInputs_PicksMostSevereAction(string? raw, GuardrailOutcome expected, string? expectedPhase)
    {
        var (outcome, phase) = OtlpLogMapper.ParseGuardrails(raw);

        Assert.Equal(expected, outcome);
        Assert.Equal(expectedPhase, phase);
    }

    [Fact]
    public void ParseGuardrails_InvalidJson_FallsBackToRegex()
    {
        // Rust debug biçimi JSON'dan saparsa (ör. sondaki virgül) regex yedeği devreye girer.
        var raw = "[{\"phase\": \"request\", \"guard\": \"regex\", \"action\": \"reject\"},]";

        var (outcome, phase) = OtlpLogMapper.ParseGuardrails(raw);

        Assert.Equal(GuardrailOutcome.Reject, outcome);
        Assert.Equal("request", phase);
    }

    [Theory]
    [InlineData("2ms", 2)]
    [InlineData("20786ms", 20786)]
    [InlineData("1.5s", 1500)]
    [InlineData("1m2s", 62000)]
    [InlineData("350µs", 0)]
    [InlineData("1500us", 2)]
    [InlineData("42", 42)]
    [InlineData("", null)]
    [InlineData("bilinmiyor", null)]
    public void ParseDurationMs_VariousFormats_ReturnsMilliseconds(string? raw, int? expected)
    {
        Assert.Equal(expected, OtlpLogMapper.ParseDurationMs(raw));
    }
}
