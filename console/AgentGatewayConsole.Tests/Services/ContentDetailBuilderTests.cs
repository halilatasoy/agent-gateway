using System.Text.Json;
using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;

namespace AgentGatewayConsole.Tests.Services;

public class ContentDetailBuilderTests
{
    private static readonly CaptureLimits Limits = CaptureLimits.Default;

    private static string Serialize(RequestContentDetail detail) => JsonSerializer.Serialize(detail);

    [Fact]
    public void Build_CopilotToolChain_ResolvesToolNamesAndFlagsToolResult()
    {
        var detail = TestContent.Builder().Build(TestContent.CopilotChainBody, null, null, Limits)!;

        Assert.Equal(6, detail.TotalMessages);
        Assert.Equal(0, detail.OmittedMessages);

        var view = detail.Messages.Single(m => m.Index == 4);
        Assert.Equal("assistant", view.Role);
        Assert.Equal("view", Assert.Single(view.ToolCalls).Name);
        Assert.Contains("./musteri.txt", view.ToolCalls[0].Arguments);

        var result = detail.Messages.Single(m => m.Index == 5);
        Assert.Equal("tool", result.Role);
        Assert.Equal("view", result.ToolName);
        Assert.Contains(result.Findings, f => f.Type == "TcKimlik" && f.Source == "tool" && f.ToolName == "view");
        Assert.Contains(result.Findings, f => f.Type == "Email");
        Assert.Contains("<TC_KIMLIK_NO>", result.Content);
    }

    [Fact]
    public void Build_CopilotToolChain_NoRawValueOrSystemPromptInOutput()
    {
        var json = Serialize(TestContent.Builder().Build(TestContent.CopilotChainBody, null, null, Limits)!);

        Assert.DoesNotContain("12345678901", json);
        Assert.DoesNotContain("deneme@ornek.com", json);
        Assert.DoesNotContain("support@github.com", json);
        Assert.DoesNotContain("You are Copilot", json);   // sistem istemi saklanmaz; yalnızca ±60 karakterlik bağlam
        Assert.DoesNotContain("Reads a file from disk", json);   // araç tanımları saklanmaz
    }

    [Fact]
    public void Build_SystemMessageWithData_ReportsFindingButOmitsContent()
    {
        var detail = TestContent.Builder().Build(TestContent.CopilotChainBody, null, null, Limits)!;

        var system = detail.Messages.Single(m => m.Role == "system");
        Assert.True(system.ContentOmitted);
        Assert.Null(system.Content);
        Assert.True(system.ContentLength > 0);
        Assert.Contains(detail.Findings, f => f.Source == "system" && f.Type == "Email");
    }

    [Fact]
    public void Build_LongContent_CountsFindingBeforeTruncating()
    {
        // TC metnin ortasında; kısaltma ortayı kessin ama bulgu yine sayılsın.
        var text = new string('a', 3000) + " TC 12345678901 " + new string('b', 3000);
        var body = JsonSerializer.Serialize(new { messages = new[] { new { role = "user", content = text } } });

        var detail = TestContent.Builder().Build(body, null, null, new CaptureLimits(30, 1000))!;

        var message = Assert.Single(detail.Messages);
        Assert.True(message.Truncated);
        Assert.Equal(text.Length, message.ContentLength);
        Assert.True(message.Content!.Length < 1100);
        Assert.Contains(message.Findings, f => f.Type == "TcKimlik");
    }

    [Fact]
    public void Build_ManyMessages_KeepsFlaggedAndLatestWithinLimit()
    {
        var messages = Enumerable.Range(0, 50)
            .Select(i => new { role = i % 2 == 0 ? "user" : "assistant", content = i == 3 ? "eski mesajda TC 12345678901" : $"mesaj {i}" })
            .ToArray();
        var body = JsonSerializer.Serialize(new { messages });

        var detail = TestContent.Builder().Build(body, null, null, new CaptureLimits(10, 2000))!;

        Assert.Equal(50, detail.TotalMessages);
        Assert.True(detail.Messages.Count <= 10);
        Assert.Contains(detail.Messages, m => m.Index == 3);     // bulgulu eski mesaj korunur
        Assert.Contains(detail.Messages, m => m.Index == 48);    // son kullanıcı istemi korunur
        Assert.Equal(50 - detail.Messages.Count, detail.OmittedMessages);
    }

    [Fact]
    public void Build_InvalidJsonBody_ReportsFindingsWithoutStoringMessages()
    {
        var detail = TestContent.Builder().Build("duz metin, TC 12345678901", null, null, Limits)!;

        Assert.True(detail.ParseFailed);
        Assert.Empty(detail.Messages);
        Assert.Contains(detail.Findings, f => f.Type == "TcKimlik" && f.Source == "body");
        Assert.DoesNotContain("12345678901", Serialize(detail));
    }

    [Fact]
    public void Build_GatewayResponseToolCalls_ParsesAndMasksArguments()
    {
        // Gateway'in llm.toolCalls biçimi: arguments bir nesne
        const string calls = "[{\"id\": \"call_9\", \"name\": \"send_mail\", \"arguments\": {\"to\": \"kisi@ornek.com\"}}]";

        var detail = TestContent.Builder().Build(null, calls, null, Limits)!;

        var call = Assert.Single(detail.ResponseToolCalls);
        Assert.Equal("send_mail", call.Name);
        Assert.Contains("<E_POSTA>", call.Arguments);
        Assert.Contains(detail.Findings, f => f.Source == "responseToolCall" && f.ToolName == "send_mail");
    }

    [Fact]
    public void Build_AnthropicToolBlocks_MapsToolUseAndToolResult()
    {
        const string body = """
            {"system":"Asistan","messages":[
              {"role":"user","content":[{"type":"text","text":"dosyayi oku"}]},
              {"role":"assistant","content":[{"type":"tool_use","id":"tu_1","name":"read_file","input":{"path":"a.txt"}}]},
              {"role":"user","content":[{"type":"tool_result","tool_use_id":"tu_1","content":"IBAN TR33 0006 1005 1978 6457 8413 26"}]}
            ]}
            """;

        var detail = TestContent.Builder().Build(body, null, null, Limits)!;

        Assert.Equal("read_file", detail.Messages.Single(m => m.Role == "assistant").ToolCalls.Single().Name);
        var result = detail.Messages.Single(m => m.Role == "tool");
        Assert.Equal("read_file", result.ToolName);
        Assert.Contains(result.Findings, f => f.Type == "Iban");
    }

    [Fact]
    public void Build_NoCapturedValues_ReturnsNull()
    {
        Assert.Null(TestContent.Builder().Build(null, "[]", "", Limits));
    }

    [Fact]
    public void DetectedTypes_MixedFindings_OrdersBySeverity()
    {
        var builder = TestContent.Builder();
        var detail = builder.Build(TestContent.CopilotChainBody, null, null, Limits)!;

        Assert.Equal("TcKimlik,Email", builder.DetectedTypes(detail));
    }
}
