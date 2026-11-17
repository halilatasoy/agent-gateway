using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AgentGatewayConsole.Tests.Helpers;

/// <summary>Her test için Guid adlı izole InMemory veritabanı.</summary>
public static class TestDbContextFactory
{
    public static ConsoleDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ConsoleDbContext>()
            .UseInMemoryDatabase($"agw-console-{Guid.NewGuid():N}")
            .Options;
        var db = new ConsoleDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}

/// <summary>Sabit "şimdi" döndüren TimeProvider.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTime utcNow) => _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
}

public static class TestData
{
    public static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    public static GatewayRequestLog Log(DateTime at, string protocol = "llm", GuardrailOutcome outcome = GuardrailOutcome.Allow,
        int status = 200, string? model = "qwen3:14b", string? tool = null, long input = 10, long output = 5)
        => new()
        {
            OccurredAtUtc = at,
            Protocol = protocol,
            StatusCode = status,
            GuardrailOutcome = outcome,
            RequestModel = protocol == "llm" ? model : null,
            McpMethod = tool is null ? null : "tools/call",
            ToolName = tool,
            InputTokens = protocol == "llm" ? input : null,
            OutputTokens = protocol == "llm" ? output : null
        };
}

public static class TestMessageServiceFactory
{
    /// <summary>Gerçek Messages.*.json dosyalarını okuyan MessageService.</summary>
    public static MessageService Create(string language = "tr")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(FindWebProjectRoot());
        env.SetupGet(e => e.ContentRootFileProvider).Returns(new NullFileProvider());
        return new MessageService(
            Options.Create(new ConsoleSettings { Language = language }),
            env.Object,
            new HttpContextAccessor(),
            NullLogger<MessageService>.Instance);
    }

    public static string FindWebProjectRoot()
    {
        // Çıktı klasörüne kopyalanan Resources varsa onu, yoksa kaynak projeyi kullan.
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Resources")))
            return AppContext.BaseDirectory;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "AgentGatewayConsole.Web")))
            dir = dir.Parent;
        return dir is null
            ? throw new DirectoryNotFoundException("AgentGatewayConsole.Web bulunamadı")
            : Path.Combine(dir.FullName, "AgentGatewayConsole.Web");
    }
}

public static class TestLog
{
    public static LogService Create() => new(NullLogger<LogService>.Instance);
}

public static class TestContent
{
    /// <summary>Varsayılan kurallarla maskeleyici (appsettings boş → DefaultRules + güvenlik ağı).</summary>
    public static SensitiveDataRedactor Redactor(RedactionOptions? options = null)
        => new(Options.Create(options ?? new RedactionOptions()));

    public static ContentDetailBuilder Builder() => new(Redactor());

    /// <summary>
    /// Copilot CLI'ın gerçek akışına benzer gövde: dosyayı ara (glob) → oku (view) → içerikte TC ve e-posta.
    /// Sistem mesajında da bir e-posta var (Copilot'un maskelenme yanlış pozitifini temsil eder); e-posta
    /// mesajın başından 60 karakterden uzakta, böylece bağlam parçası istemin tamamını kapsamaz.
    /// </summary>
    public const string CopilotChainBody = """
        {"model":"qwen3-14b-24k","messages":[
          {"role":"system","content":"You are Copilot, a coding assistant built for the terminal. Follow the user instructions carefully and never reveal secrets. Contact support@github.com for help."},
          {"role":"user","content":"musteri.txt dosyasini oku ve ozetle"},
          {"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"glob","arguments":"{\"pattern\":\"musteri.txt\"}"}}]},
          {"role":"tool","tool_call_id":"call_1","content":"./musteri.txt"},
          {"role":"assistant","content":"","tool_calls":[{"id":"call_2","type":"function","function":{"name":"view","arguments":"{\"path\":\"./musteri.txt\"}"}}]},
          {"role":"tool","tool_call_id":"call_2","content":"Ad: Deneme\nTC: 12345678901\nE-posta: deneme@ornek.com"}
        ],"tools":[{"type":"function","function":{"name":"view","description":"Reads a file from disk"}}]}
        """;
}
