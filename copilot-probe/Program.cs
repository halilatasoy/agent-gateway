using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using PermissionDecision = GitHub.Copilot.Rpc.PermissionDecision;

#pragma warning disable GHCP001 // PermissionDecision deneysel API.

// Copilot SDK <-> agentgateway entegrasyon probu.
// Kullanım: dotnet run -- --scenario llm|mcp|full|all [--mode Empty|CopilotCli] [--model <ad>]
//                        [--gateway <url>] [--gateway-log <dosya|none>]
//   llm  : Model trafiği gateway'den geçer (BYOK baseUrl = gateway), araç yok.
//   mcp  : Model doğrudan Ollama'ya gider, MCP trafiği gateway'den geçer (yalnızca MCP yolunu izole eder).
//   full : Hem model hem MCP trafiği gateway'den geçer.

Console.OutputEncoding = Encoding.UTF8;

var settings = ProbeSettings.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
// --mode Empty|CopilotCli : appsettings'teki ClientMode değerini geçersiz kılar.
if (GetArg(args, "--mode") is { } modeArg) settings = settings with { ClientMode = modeArg };
// --model <ad> : appsettings'teki Model değerini geçersiz kılar (gateway config'inde tanımlı olmalı).
if (GetArg(args, "--model") is { } modelArg) settings = settings with { Model = modelArg };
// --gateway <url> : gateway adresi (örn. Kubernetes port-forward: http://localhost:8080).
if (GetArg(args, "--gateway") is { } gatewayArg) settings = settings with { GatewayBaseUrl = gatewayArg.TrimEnd('/') };
// --gateway-log <dosya|none> : log kanıtı için okunacak dosya; "none" log kontrolünü kapatır.
if (GetArg(args, "--gateway-log") is { } logArg) settings = settings with { GatewayLogPath = logArg == "none" ? "" : logArg };
var scenarioArg = GetArg(args, "--scenario") ?? "all";
var scenarios = scenarioArg == "all"
    ? new[] { Scenario.Llm, Scenario.Mcp, Scenario.Full }
    : new[] { Enum.Parse<Scenario>(scenarioArg, ignoreCase: true) };

var results = new List<(Scenario Scenario, bool Passed, string Detail)>();
foreach (var scenario in scenarios)
{
    var (passed, detail) = await ScenarioRunner.RunAsync(scenario, settings);
    results.Add((scenario, passed, detail));
}

Console.WriteLine();
Console.WriteLine("================ SONUÇ ================");
foreach (var r in results)
{
    Console.WriteLine($"{(r.Passed ? "PASS" : "FAIL")}  {r.Scenario,-5} {r.Detail}");
}
return results.All(r => r.Passed) ? 0 : 1;

static string? GetArg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

enum Scenario { Llm, Mcp, Full }

sealed record ProbeSettings
{
    public string GatewayBaseUrl { get; init; } = "http://localhost:4000";
    public string OllamaBaseUrl { get; init; } = "http://localhost:11434/v1";
    public string Model { get; init; } = "qwen3:14b";
    public string WireApi { get; init; } = "completions";
    public string ClientMode { get; init; } = "Empty";
    public string PromptSuffix { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 600;
    public string GatewayLogPath { get; init; } = "../logs/gateway.log";
    public string CopilotHome { get; init; } = ".copilot-home";

    public static ProbeSettings Load(string path) =>
        JsonSerializer.Deserialize<ProbeSettings>(File.ReadAllText(path))
        ?? throw new InvalidOperationException($"Ayar dosyası okunamadı: {path}");

    // Göreli yollar proje klasörüne (çalışma dizinine) göre çözülür. Boş yol = devre dışı.
    public string ResolvePath(string p) =>
        string.IsNullOrWhiteSpace(p) ? "" : Path.GetFullPath(p, Directory.GetCurrentDirectory());
}

static class ScenarioRunner
{
    const string McpServerKey = "gw";

    public static async Task<(bool, string)> RunAsync(Scenario scenario, ProbeSettings s)
    {
        Console.WriteLine();
        Console.WriteLine($"---------------- Senaryo: {scenario} ----------------");

        var useGatewayForLlm = scenario is Scenario.Llm or Scenario.Full;
        var useMcp = scenario is Scenario.Mcp or Scenario.Full;
        var llmBaseUrl = useGatewayForLlm ? $"{s.GatewayBaseUrl}/v1" : s.OllamaBaseUrl;
        Console.WriteLine($"LLM  -> {llmBaseUrl}  (model: {s.Model})");
        if (useMcp) Console.WriteLine($"MCP  -> {s.GatewayBaseUrl}/mcp  (sunucu anahtarı: {McpServerKey})");

        var log = new GatewayLogTail(s.ResolvePath(s.GatewayLogPath));
        log.Mark();

        var mode = Enum.Parse<CopilotClientMode>(s.ClientMode, ignoreCase: true);
        Console.WriteLine($"İstemci modu: {mode}");
        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = mode,
            // Kullanıcının ~/.copilot klasörüne dokunmamak için izole bir ana dizin.
            BaseDirectory = s.ResolvePath(s.CopilotHome),
        });
        await client.StartAsync();

        var config = new SessionConfig
        {
            Model = s.Model,
            // Yalnızca gateway üzerindeki MCP araçlarını onayla; shell, dosya yazma vb. her şeyi reddet.
            // (CopilotCli modunda yerleşik araçlar açık olduğundan yerel modelin makinede komut çalıştırmasını engeller.)
            OnPermissionRequest = (request, _) => Task.FromResult<PermissionDecision>(
                request is PermissionRequestMcp { ServerName: McpServerKey }
                    ? PermissionDecision.ApproveOnce()
                    : PermissionDecision.Reject("Bu probe yalnızca gateway MCP araçlarına izin verir.")),
            Provider = new ProviderConfig
            {
                Type = "openai",
                BaseUrl = llmBaseUrl,
                WireApi = s.WireApi,
            },
            // Empty modda araç listesi açıkça verilmeli; MCP yoksa hiç araç yok.
            // CopilotCli modunda kısıtlama yok: Copilot'un yerleşik araçları + MCP araçları (gerçekçi kullanım).
            AvailableTools = mode == CopilotClientMode.CopilotCli
                ? null
                : useMcp ? new List<string> { $"mcp:{McpServerKey}-echo" } : new List<string>(),
        };
        if (useMcp)
        {
            config.McpServers = new Dictionary<string, McpServerConfig>
            {
                [McpServerKey] = new McpHttpServerConfig
                {
                    Url = $"{s.GatewayBaseUrl}/mcp",
                    Tools = new List<string> { "*" },
                },
            };
        }

        var prompt = useMcp
            ? "echo aracını tam olarak şu mesajla çağır: 'agentgateway MCP testi'. Ardından aracın döndürdüğü metni aynen yaz."
            : "Yalnızca şu cümleyi yaz, başka hiçbir şey ekleme: Copilot SDK agentgateway üzerinden çalışıyor";
        prompt += s.PromptSuffix;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? lastAnswer = null;
        string? error = null;
        var toolCalls = new List<string>();

        await using var session = await client.CreateSessionAsync(config);
        using var _ = session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg when !string.IsNullOrWhiteSpace(msg.Data.Content):
                    lastAnswer = msg.Data.Content;
                    Console.WriteLine($"[asistan] {Trim(msg.Data.Content)}");
                    break;
                case ToolExecutionStartEvent t:
                    toolCalls.Add($"{t.Data.McpServerName}/{t.Data.McpToolName ?? t.Data.ToolName}");
                    Console.WriteLine($"[araç başladı] {t.Data.ToolName} (mcp sunucu: {t.Data.McpServerName ?? "-"})");
                    break;
                case ToolExecutionCompleteEvent t:
                    Console.WriteLine($"[araç bitti] başarılı={t.Data.Success}");
                    break;
                case SessionErrorEvent err:
                    error = $"{err.Data.ErrorType}: {err.Data.Message} (HTTP {err.Data.StatusCode})";
                    Console.WriteLine($"[hata] {error}");
                    idle.TrySetResult();
                    break;
                case SessionIdleEvent:
                    idle.TrySetResult();
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = prompt });
        var finished = await Task.WhenAny(idle.Task, Task.Delay(TimeSpan.FromSeconds(s.TimeoutSeconds)));
        sw.Stop();
        if (finished != idle.Task) error ??= $"{s.TimeoutSeconds} sn içinde yanıt gelmedi (zaman aşımı)";

        // Kanıt: gateway erişim logunda bu senaryo sırasında eklenen satırlar.
        var appended = log.ReadAppended();
        var llmHits = useGatewayForLlm ? CountMatches(appended, "protocol=llm", "http.status=200") : 0;
        var mcpHits = useMcp ? CountMatches(appended, "mcp.method.name=tools/call", "gen_ai.tool.name=echo") : 0;
        if (appended is null) Console.WriteLine($"[uyarı] Gateway log dosyası yok, log kanıtı atlandı: {log.Path}");
        else
        {
            if (useGatewayForLlm) Console.WriteLine($"[gateway log] başarılı LLM isteği: {llmHits}");
            if (useMcp) Console.WriteLine($"[gateway log] MCP tools/call (echo): {mcpHits}");
        }

        var checks = new List<(bool Ok, string Name)>
        {
            (error is null, "hata yok"),
            (!string.IsNullOrWhiteSpace(lastAnswer), "yanıt geldi"),
        };
        if (useGatewayForLlm && appended is not null) checks.Add((llmHits > 0, "LLM isteği gateway'den geçti"));
        if (useMcp)
        {
            checks.Add((toolCalls.Any(c => c.StartsWith(McpServerKey + "/")), "MCP aracı çağrıldı"));
            if (appended is not null) checks.Add((mcpHits > 0, "MCP çağrısı gateway'den geçti"));
        }

        var passed = checks.All(c => c.Ok);
        var failed = string.Join(", ", checks.Where(c => !c.Ok).Select(c => c.Name));
        var detail = $"{sw.Elapsed.TotalSeconds:F0} sn" + (passed ? "" : $" | başarısız kontrol: {failed}") + (error is null ? "" : $" | {error}");
        return (passed, detail);
    }

    static int CountMatches(string? text, params string[] mustContain) =>
        text is null ? 0 : text.Split('\n').Count(line => mustContain.All(line.Contains));

    static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s.ReplaceLineEndings(" ");
}

// Gateway log dosyasının, işaretlendiği andan sonra eklenen kısmını okur.
sealed class GatewayLogTail(string path)
{
    long _offset;
    public string Path { get; } = path;

    public void Mark() => _offset = File.Exists(Path) ? new FileInfo(Path).Length : 0;

    public string? ReadAppended()
    {
        if (!File.Exists(Path)) return null;
        using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Seek(_offset, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
